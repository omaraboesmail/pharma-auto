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
    private readonly Dictionary<Guid, Guid> processingAttempts = [];
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

    public async Task<OcrProcessingAttempt> StartOcrJobAsync(
        Guid tenantId,
        Guid connectorId,
        Guid jobId,
        int pageCount,
        string sourceSha256,
        DateTimeOffset now,
        DateTimeOffset staleBefore,
        CancellationToken cancellationToken)
    {
        if (pageCount is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(pageCount));
        }
        if (sourceSha256.Length != 64)
        {
            throw new ArgumentException("A SHA-256 source binding is required.", nameof(sourceSha256));
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var key = (tenantId, jobId);
            if (jobs.TryGetValue(jobId, out var existingJob))
            {
                EnsureSameOcrJob(
                    existingJob,
                    tenantId,
                    connectorId,
                    pageCount,
                    sourceSha256);
                if (!reservations.TryGetValue(key, out var existingReservation) ||
                    existingReservation.ReservationId != existingJob.ReservationId ||
                    !processingAttempts.TryGetValue(jobId, out var existingAttemptId))
                {
                    throw new InvalidOperationException(
                        "OCR job, reservation, and processing attempt do not agree.");
                }

                if (existingJob.State == OcrJobState.Completed)
                {
                    if (existingReservation.SettledAt is null || existingReservation.Released)
                    {
                        throw new InvalidOperationException(
                            "Completed OCR job and quota settlement do not agree.");
                    }
                    return new OcrProcessingAttempt(existingJob, existingAttemptId);
                }

                if (existingJob.State is OcrJobState.Processing or OcrJobState.Reserved)
                {
                    if (existingReservation.SettledAt is not null || existingReservation.Released)
                    {
                        throw new InvalidOperationException(
                            "Active OCR job and quota reservation do not agree.");
                    }
                    if (existingJob.State == OcrJobState.Processing &&
                        existingJob.UpdatedAt > staleBefore)
                    {
                        throw new InvalidOperationException(
                            "The OCR job is already being processed by an active attempt.");
                    }

                    var recoveredAttemptId = Guid.NewGuid();
                    var recoveredJob = existingJob with
                    {
                        State = OcrJobState.Processing,
                        ResultJson = null,
                        ProviderModel = null,
                        FailureCode = null,
                        UpdatedAt = now
                    };
                    jobs[jobId] = recoveredJob;
                    processingAttempts[jobId] = recoveredAttemptId;
                    return new OcrProcessingAttempt(recoveredJob, recoveredAttemptId);
                }

                if (existingJob.State != OcrJobState.Failed ||
                    existingReservation.SettledAt is not null ||
                    !existingReservation.Released)
                {
                    throw new InvalidOperationException(
                        "Failed OCR job and released quota reservation do not agree.");
                }

                EnsureEntitled(tenantId, connectorId, now);
                EnsureQuotaAvailable(pageCount);
                var reactivated = existingReservation with
                {
                    ReservedAt = now,
                    SettledAt = null,
                    Released = false
                };
                var retryAttemptId = Guid.NewGuid();
                var retryJob = existingJob with
                {
                    State = OcrJobState.Processing,
                    ResultJson = null,
                    ProviderModel = null,
                    FailureCode = null,
                    UpdatedAt = now
                };
                reservations[key] = reactivated;
                pagesReserved += pageCount;
                jobs[jobId] = retryJob;
                processingAttempts[jobId] = retryAttemptId;
                return new OcrProcessingAttempt(retryJob, retryAttemptId);
            }

            EnsureEntitled(tenantId, connectorId, now);
            EnsureQuotaAvailable(pageCount);

            var reservation = new QuotaReservation(
                Guid.NewGuid(),
                tenantId,
                jobId,
                pageCount,
                now,
                null,
                false);
            var attemptId = Guid.NewGuid();
            var job = new OcrJob(
                jobId,
                tenantId,
                connectorId,
                pageCount,
                sourceSha256,
                OcrJobState.Processing,
                reservation.ReservationId,
                null,
                null,
                null,
                now,
                now);
            reservations.Add(key, reservation);
            pagesReserved += pageCount;
            jobs[jobId] = job;
            processingAttempts[jobId] = attemptId;
            return new OcrProcessingAttempt(job, attemptId);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OcrJob> CompleteOcrJobAsync(
        OcrJob job,
        Guid attemptId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        if (job.State != OcrJobState.Completed || string.IsNullOrWhiteSpace(job.ResultJson))
        {
            throw new ArgumentException("A completed OCR job must include a result.", nameof(job));
        }
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = RequireMatchingJob(job);
            EnsureMatchingAttempt(job.JobId, attemptId);
            EnsureMatchingAudit(job, auditEvent, settle: true);
            if (existing.State == OcrJobState.Completed)
            {
                return existing;
            }
            var key = (job.TenantId, job.JobId);
            if (!reservations.TryGetValue(key, out var reservation) ||
                reservation.ReservationId != job.ReservationId ||
                reservation.SettledAt is not null ||
                reservation.Released)
            {
                throw new InvalidOperationException(
                    "OCR completion requires the active reservation bound to the job.");
            }
            reservations[key] = reservation with { SettledAt = job.UpdatedAt };
            pagesReserved -= reservation.PageCount;
            pagesSettled += reservation.PageCount;
            jobs[job.JobId] = job;
            audits.Enqueue(auditEvent);
            return job;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OcrJob> FailOcrJobAsync(
        OcrJob job,
        Guid attemptId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        if (job.State != OcrJobState.Failed || string.IsNullOrWhiteSpace(job.FailureCode))
        {
            throw new ArgumentException("A failed OCR job must include a failure code.", nameof(job));
        }
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = RequireMatchingJob(job);
            EnsureMatchingAttempt(job.JobId, attemptId);
            EnsureMatchingAudit(job, auditEvent, settle: false);
            if (existing.State is OcrJobState.Completed or OcrJobState.Failed)
            {
                return existing;
            }
            var key = (job.TenantId, job.JobId);
            if (!reservations.TryGetValue(key, out var reservation) ||
                reservation.ReservationId != job.ReservationId ||
                reservation.SettledAt is not null ||
                reservation.Released)
            {
                throw new InvalidOperationException(
                    "OCR failure requires the active reservation bound to the job.");
            }
            reservations[key] = reservation with { Released = true };
            pagesReserved -= reservation.PageCount;
            jobs[job.JobId] = job;
            audits.Enqueue(auditEvent);
            return job;
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

    public Task<IReadOnlyList<CanonicalProductSearchHit>> SearchCanonicalProductsAsync(
        Guid tenantId,
        CanonicalSearchQuery query,
        float[]? embedding,
        string? embeddingVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenantId != seed.Connector.TenantId)
        {
            return Task.FromResult<IReadOnlyList<CanonicalProductSearchHit>>([]);
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
            .Select(result => new CanonicalProductSearchHit(
                result.Product,
                result.VectorScore > 0.1))
            .ToArray();
        return Task.FromResult<IReadOnlyList<CanonicalProductSearchHit>>(products);
    }

    public Task AppendAuditAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        audits.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    private void EnsureEntitled(Guid tenantId, Guid connectorId, DateTimeOffset now)
    {
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
    }

    private void EnsureQuotaAvailable(int pageCount)
    {
        var remaining = seed.Entitlement.PageLimit - pagesReserved - pagesSettled;
        if (pageCount > remaining)
        {
            throw new QuotaExceededException(pageCount, remaining);
        }
    }

    private static void EnsureSameOcrJob(
        OcrJob existing,
        Guid tenantId,
        Guid connectorId,
        int pageCount,
        string sourceSha256)
    {
        if (existing.TenantId != tenantId ||
            existing.ConnectorId != connectorId ||
            existing.PageCount != pageCount ||
            !string.Equals(existing.SourceSha256, sourceSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The OCR job ID was replayed with a different connector or source document.");
        }
    }

    private void EnsureMatchingAttempt(Guid jobId, Guid attemptId)
    {
        if (!processingAttempts.TryGetValue(jobId, out var currentAttemptId) ||
            currentAttemptId != attemptId)
        {
            throw new InvalidOperationException(
                "The OCR result belongs to a superseded processing attempt.");
        }
    }

    private static void EnsureMatchingAudit(
        OcrJob job,
        AuditEvent auditEvent,
        bool settle)
    {
        if (auditEvent.TenantId != job.TenantId ||
            auditEvent.CorrelationId != job.JobId ||
            !string.Equals(auditEvent.ActorType, "CONNECTOR", StringComparison.Ordinal) ||
            !string.Equals(
                auditEvent.ActorReference,
                job.ConnectorId.ToString("D"),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                auditEvent.Action,
                settle ? "OCR_SETTLED" : "OCR_RELEASED",
                StringComparison.Ordinal) ||
            !string.Equals(
                auditEvent.TargetReference,
                job.JobId.ToString("D"),
                StringComparison.OrdinalIgnoreCase) ||
            auditEvent.OccurredAt != job.UpdatedAt ||
            !string.Equals(
                auditEvent.Result,
                settle ? "SUCCESS" : job.FailureCode,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("OCR audit identity does not match the job.", nameof(auditEvent));
        }
    }

    private OcrJob RequireMatchingJob(OcrJob job)
    {
        if (!jobs.TryGetValue(job.JobId, out var existing) ||
            existing.TenantId != job.TenantId ||
            existing.ConnectorId != job.ConnectorId ||
            existing.PageCount != job.PageCount ||
            !string.Equals(existing.SourceSha256, job.SourceSha256, StringComparison.Ordinal) ||
            existing.ReservationId != job.ReservationId)
        {
            throw new InvalidOperationException(
                "OCR job identity or immutable source binding does not match.");
        }
        return existing;
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
