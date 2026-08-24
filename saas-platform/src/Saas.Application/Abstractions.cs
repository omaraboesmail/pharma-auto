using PharmaAuto.Saas.Domain;

namespace PharmaAuto.Saas.Application;

public sealed record CanonicalProductSearchHit(
    CanonicalProduct Product,
    bool SemanticMatch);

public sealed record OcrProcessingAttempt(
    OcrJob Job,
    Guid AttemptId);

public interface ISaasStore
{
    Task<ConnectorRegistration?> GetConnectorAsync(
        Guid tenantId,
        Guid connectorId,
        CancellationToken cancellationToken);

    Task<SubscriptionEntitlement?> GetEntitlementAsync(
        Guid tenantId,
        Guid connectorId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<OcrProcessingAttempt> StartOcrJobAsync(
        Guid tenantId,
        Guid connectorId,
        Guid jobId,
        int pageCount,
        string sourceSha256,
        DateTimeOffset now,
        DateTimeOffset staleBefore,
        CancellationToken cancellationToken);

    Task<OcrJob> CompleteOcrJobAsync(
        OcrJob job,
        Guid attemptId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<OcrJob> FailOcrJobAsync(
        OcrJob job,
        Guid attemptId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken);

    Task<OcrJob?> GetOcrJobAsync(
        Guid tenantId,
        Guid jobId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CanonicalProductSearchHit>> SearchCanonicalProductsAsync(
        Guid tenantId,
        CanonicalSearchQuery query,
        float[]? embedding,
        string? embeddingVersion,
        CancellationToken cancellationToken);

    Task AppendAuditAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}

public interface IOcrProvider
{
    string ProviderName { get; }

    Task<OcrProviderResult> ExtractAsync(
        OcrDocument document,
        CancellationToken cancellationToken);
}

public interface IEmbeddingProvider
{
    string Version { get; }

    Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken);
}

public interface IEntitlementSigner
{
    string Algorithm { get; }

    string KeyId { get; }

    string Sign(ReadOnlySpan<byte> payload);
}
