using PharmaAuto.Saas.Domain;

namespace PharmaAuto.Saas.Application;

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

    Task<QuotaReservation> ReserveQuotaAsync(
        Guid tenantId,
        Guid connectorId,
        Guid jobId,
        int pageCount,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task SettleQuotaAsync(
        Guid tenantId,
        Guid reservationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task ReleaseQuotaAsync(
        Guid tenantId,
        Guid reservationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<OcrJob?> GetOcrJobAsync(
        Guid tenantId,
        Guid jobId,
        CancellationToken cancellationToken);

    Task SaveOcrJobAsync(OcrJob job, CancellationToken cancellationToken);

    Task<IReadOnlyList<CanonicalProduct>> SearchCanonicalProductsAsync(
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
