using System.Security.Cryptography;
using System.Text;
using PharmaAuto.Saas.Domain;

namespace PharmaAuto.Saas.Application;

public sealed class OcrOrchestrator(
    ISaasStore store,
    IOcrProvider provider,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(15);

    public async Task<OcrJob> ProcessAsync(
        Guid tenantId,
        Guid connectorId,
        OcrDocument document,
        CancellationToken cancellationToken)
    {
        ValidateDocument(document);

        var now = timeProvider.GetUtcNow();
        var attempt = await store.StartOcrJobAsync(
            tenantId,
            connectorId,
            document.JobId,
            document.Pages.Count,
            document.SourceSha256,
            now,
            now.Subtract(ProcessingLease),
            cancellationToken);
        if (attempt.Job.State == OcrJobState.Completed)
        {
            return attempt.Job;
        }

        var job = attempt.Job;

        OcrProviderResult providerResult;
        try
        {
            providerResult = await provider.ExtractAsync(document, cancellationToken);
        }
        catch (Exception exception)
        {
            var failedAt = timeProvider.GetUtcNow();
            var failureCode = FailureCode(exception);
            job = job with
            {
                State = OcrJobState.Failed,
                FailureCode = failureCode,
                UpdatedAt = failedAt
            };
            var cleanupToken = cancellationToken.IsCancellationRequested
                ? CancellationToken.None
                : cancellationToken;
            Exception? cleanupException = null;
            try
            {
                _ = await store.FailOcrJobAsync(
                    job,
                    attempt.AttemptId,
                    new AuditEvent(
                        Guid.NewGuid(),
                        tenantId,
                        "CONNECTOR",
                        connectorId.ToString("D"),
                        "OCR_RELEASED",
                        document.JobId.ToString("D"),
                        failureCode,
                        document.JobId,
                        failedAt),
                    cleanupToken);
            }
            catch (Exception cleanupFailure)
            {
                cleanupException = cleanupFailure;
            }
            if (exception is OperationCanceledException)
            {
                throw;
            }
            if (exception is OcrProviderException && cleanupException is null)
            {
                throw;
            }
            throw new OcrProviderException(
                failureCode,
                "OCR processing failed before a canonical result was committed.",
                cleanupException is null
                    ? exception
                    : new AggregateException(exception, cleanupException));
        }

        var completedAt = timeProvider.GetUtcNow();
        job = job with
        {
            State = OcrJobState.Completed,
            ResultJson = providerResult.Json,
            ProviderModel = providerResult.Model,
            FailureCode = null,
            UpdatedAt = completedAt
        };
        return await store.CompleteOcrJobAsync(
            job,
            attempt.AttemptId,
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

            if (page.MimeType is not ("image/jpeg" or "image/png"))
            {
                throw new ArgumentException(
                    $"Page {page.Page} has an unsupported MIME type. OCR transport accepts normalized JPEG or PNG pages only.");
            }
        }

        var sourceHash = ComputeLogicalDocumentSha256(document.Pages);
        if (!string.Equals(sourceHash, document.SourceSha256, StringComparison.Ordinal))
        {
            throw new ArgumentException("Document source hash does not match the ordered page hashes.");
        }
    }

    private static string FailureCode(Exception exception) => exception switch
    {
        OcrProviderException providerException => providerException.Code,
        OperationCanceledException => "OCR_CANCELLED",
        System.Text.Json.JsonException => "OCR_SCHEMA_INVALID",
        FormatException => "OCR_PROVIDER_FORMAT_INVALID",
        HttpRequestException => "OCR_PROVIDER_UNAVAILABLE",
        _ => "OCR_PROVIDER_UNEXPECTED"
    };
}
