using System.Collections.Concurrent;
using PharmaAuto.Saas.Application;
using PharmaAuto.Saas.Domain;

namespace PharmaAuto.Saas.Infrastructure;

public sealed record InMemorySaasSeed(
    ConnectorRegistration Connector,
    SubscriptionEntitlement Entitlement,
    IReadOnlyList<CanonicalProduct> CanonicalProducts);

public sealed class InMemorySaasStore(InMemorySaasSeed seed) : ISaasStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, OcrJob> jobs = new();
    private readonly Dictionary<(Guid TenantId, Guid JobId), QuotaReservation> reservations = [];
    private readonly ConcurrentQueue<AuditEvent> audits = new();
    private int pagesReserved = seed.Entitlement.PagesReserved;
    private int pagesSettled = seed.Entitlement.PagesSettled;

    public Task<ConnectorRegistration?> GetConnectorAsync(
        Guid tenantId,
        Guid connectorId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConnectorRegistration? connector = seed.Connector.ConnectorId == connectorId &&
            seed.Connector.TenantId == tenantId
            ? seed.Connector
            : null;
        return Task.FromResult(connector);
    }

    public async Task<SubscriptionEntitlement?> GetEntitlementAsync(
        Guid tenantId,
        Guid connectorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (seed.Entitlement.TenantId != tenantId ||
                seed.Entitlement.ConnectorId != connectorId)
            {
                return null;
            }

            var status = seed.Entitlement.Status;
            if (now < seed.Entitlement.ValidFrom || now >= seed.Entitlement.ValidUntil)
            {
                status = SubscriptionStatus.Expired;
            }

            return seed.Entitlement with
            {
                Status = status,
                PagesReserved = pagesReserved,
                PagesSettled = pagesSettled
            };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<QuotaReservation> ReserveQuotaAsync(
        Guid tenantId,
        Guid connectorId,
        Guid jobId,
        int pageCount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (pageCount is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(pageCount));
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (reservations.TryGetValue((tenantId, jobId), out var existing))
            {
                if (existing.PageCount != pageCount)
                {
                    throw new InvalidOperationException(
                        "The idempotent OCR reservation was replayed with a different page count.");
                }
                return existing;
            }

            if (seed.Connector.TenantId != tenantId ||
                seed.Connector.ConnectorId != connectorId ||
                seed.Connector.Revoked)
            {
                throw new EntitlementRejectedException("Connector identity is not active for this tenant.");
            }

            if (seed.Entitlement.Status != SubscriptionStatus.Active ||
                now < seed.Entitlement.ValidFrom ||
                now >= seed.Entitlement.ValidUntil)
            {
                throw new EntitlementRejectedException("Subscription entitlement is not active.");
            }

            var remaining = seed.Entitlement.PageLimit - pagesReserved - pagesSettled;
            if (pageCount > remaining)
            {
                throw new QuotaExceededException(pageCount, remaining);
            }

            var reservation = new QuotaReservation(
                Guid.NewGuid(),
                tenantId,
                jobId,
                pageCount,
                now,
                null,
                false);
            reservations.Add((tenantId, jobId), reservation);
            pagesReserved += pageCount;
            return reservation;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SettleQuotaAsync(
        Guid tenantId,
        Guid reservationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var pair = reservations.SingleOrDefault(entry =>
                entry.Value.ReservationId == reservationId && entry.Value.TenantId == tenantId);
            if (pair.Value is null || pair.Value.SettledAt is not null || pair.Value.Released)
            {
                return;
            }

            var settled = pair.Value with { SettledAt = now };
            reservations[pair.Key] = settled;
            pagesReserved -= settled.PageCount;
            pagesSettled += settled.PageCount;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReleaseQuotaAsync(
        Guid tenantId,
        Guid reservationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var pair = reservations.SingleOrDefault(entry =>
                entry.Value.ReservationId == reservationId && entry.Value.TenantId == tenantId);
            if (pair.Value is null || pair.Value.SettledAt is not null || pair.Value.Released)
            {
                return;
            }

            reservations[pair.Key] = pair.Value with { Released = true };
            pagesReserved -= pair.Value.PageCount;
            _ = now;
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<OcrJob?> GetOcrJobAsync(
        Guid tenantId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (jobs.TryGetValue(jobId, out var job) && job.TenantId == tenantId)
        {
            return Task.FromResult<OcrJob?>(job);
        }
        return Task.FromResult<OcrJob?>(null);
    }

    public Task SaveOcrJobAsync(OcrJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        jobs.AddOrUpdate(
            job.JobId,
            job,
            (_, existing) => existing.TenantId == job.TenantId
                ? job
                : throw new InvalidOperationException("Cross-tenant OCR job collision."));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CanonicalProduct>> SearchCanonicalProductsAsync(
        Guid tenantId,
        CanonicalSearchQuery query,
        float[]? embedding,
        string? embeddingVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantId != seed.Connector.TenantId)
        {
            return Task.FromResult<IReadOnlyList<CanonicalProduct>>([]);
        }

        var queryTokens = query.Description
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var products = seed.CanonicalProducts
            .Select(product => new
            {
                Product = product,
                IdentifierMatch = !string.IsNullOrWhiteSpace(query.VendorItemCode) &&
                    product.Identifiers.Contains(
                        query.VendorItemCode,
                        StringComparer.OrdinalIgnoreCase),
                LexicalHits = product.Aliases
                    .Append(product.DisplayName)
                    .SelectMany(value => value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    .Count(queryTokens.Contains),
                VectorScore = string.Equals(
                    embeddingVersion,
                    product.EmbeddingVersion,
                    StringComparison.Ordinal)
                    ? CosineSimilarity(embedding, product.Embedding)
                    : 0d
            })
            .Where(result => result.IdentifierMatch || result.LexicalHits > 0 || result.VectorScore > 0.1)
            .OrderByDescending(result => result.IdentifierMatch)
            .ThenByDescending(result => result.LexicalHits)
            .ThenByDescending(result => result.VectorScore)
            .Take(Math.Max(query.Limit * 3, query.Limit))
            .Select(result => result.Product)
            .ToArray();
        return Task.FromResult<IReadOnlyList<CanonicalProduct>>(products);
    }

    public Task AppendAuditAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        audits.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    private static double CosineSimilarity(float[]? first, float[]? second)
    {
        if (first is null || second is null || first.Length == 0 || first.Length != second.Length)
        {
            return 0d;
        }

        double dot = 0;
        double firstMagnitude = 0;
        double secondMagnitude = 0;
        for (var index = 0; index < first.Length; index++)
        {
            dot += first[index] * second[index];
            firstMagnitude += first[index] * first[index];
            secondMagnitude += second[index] * second[index];
        }
        return firstMagnitude == 0 || secondMagnitude == 0
            ? 0
            : dot / Math.Sqrt(firstMagnitude * secondMagnitude);
    }
}
