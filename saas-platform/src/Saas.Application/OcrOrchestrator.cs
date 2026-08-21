using System.Security.Cryptography;
using System.Text;
using PharmaAuto.Saas.Domain;

namespace PharmaAuto.Saas.Application;

public sealed class OcrOrchestrator(
    ISaasStore store,
    IOcrProvider provider,
    TimeProvider timeProvider)
{
    public async Task<OcrJob> ProcessAsync(
        Guid tenantId,
        Guid connectorId,
        OcrDocument document,
        CancellationToken cancellationToken)
    {
        ValidateDocument(document);

        var existing = await store.GetOcrJobAsync(
            tenantId,
            document.JobId,
            cancellationToken);
        if (existing is { State: OcrJobState.Completed })
        {
            return existing;
        }

        var now = timeProvider.GetUtcNow();
        var reservation = await store.ReserveQuotaAsync(
            tenantId,
            connectorId,
            document.JobId,
            document.Pages.Count,
            now,
            cancellationToken);

        var job = existing ?? new OcrJob(
            document.JobId,
            tenantId,
            connectorId,
            document.Pages.Count,
            document.SourceSha256,
            OcrJobState.Reserved,
            reservation.ReservationId,
            null,
            null,
            null,
            now,
            now);

        job = job with { State = OcrJobState.Processing, UpdatedAt = now };
        await store.SaveOcrJobAsync(job, cancellationToken);

        try
        {
            var providerResult = await provider.ExtractAsync(document, cancellationToken);
            var completedAt = timeProvider.GetUtcNow();
            job = job with
            {
                State = OcrJobState.Completed,
                ResultJson = providerResult.Json,
                ProviderModel = providerResult.Model,
                FailureCode = null,
                UpdatedAt = completedAt
            };
            await store.SaveOcrJobAsync(job, cancellationToken);
            await store.SettleQuotaAsync(
                tenantId,
                reservation.ReservationId,
                completedAt,
                cancellationToken);
            await store.AppendAuditAsync(
                new AuditEvent(
                    Guid.NewGuid(),
                    tenantId,
                    "CONNECTOR",
                    connectorId.ToString("D"),
                    "OCR_SETTLED",
                    document.JobId.ToString("D"),
                    "SUCCESS",
                    document.JobId,
                    completedAt),
                cancellationToken);
            return job;
        }
        catch (OcrProviderException exception)
        {
            var failedAt = timeProvider.GetUtcNow();
            job = job with
            {
                State = OcrJobState.Failed,
                FailureCode = exception.Code,
                UpdatedAt = failedAt
            };
            await store.SaveOcrJobAsync(job, cancellationToken);
            await store.ReleaseQuotaAsync(
                tenantId,
                reservation.ReservationId,
                failedAt,
                cancellationToken);
            throw;
        }
    }

    public static string ComputeLogicalDocumentSha256(
        IReadOnlyList<OcrDocumentPage> pages)
    {
        var lines = pages
            .OrderBy(page => page.Page)
            .Select(page => $"{page.Page}:{page.Sha256}");
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static void ValidateDocument(OcrDocument document)
    {
        if (document.Pages.Count is < 1 or > 100)
        {
            throw new ArgumentException("A document must contain between 1 and 100 pages.");
        }

        var expectedPage = 1;
        foreach (var page in document.Pages.OrderBy(page => page.Page))
        {
            if (page.Page != expectedPage++)
            {
                throw new ArgumentException("Document pages must be contiguous and start at 1.");
            }

            var actualHash = Convert.ToHexStringLower(SHA256.HashData(page.Bytes.Span));
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(actualHash),
                    Encoding.ASCII.GetBytes(page.Sha256)))
            {
                throw new ArgumentException($"Page {page.Page} hash does not match its payload.");
            }

            if (page.MimeType is not ("image/jpeg" or "image/png" or "application/pdf"))
            {
                throw new ArgumentException($"Page {page.Page} has an unsupported MIME type.");
            }
        }

        var sourceHash = ComputeLogicalDocumentSha256(document.Pages);
        if (!string.Equals(sourceHash, document.SourceSha256, StringComparison.Ordinal))
        {
            throw new ArgumentException("Document source hash does not match the ordered page hashes.");
        }
    }
}
