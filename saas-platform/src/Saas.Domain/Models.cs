namespace PharmaAuto.Saas.Domain;

public enum SubscriptionStatus
{
    Active,
    Suspended,
    Expired
}

public enum OcrJobState
{
    Reserved,
    Processing,
    Completed,
    Failed
}

public sealed record ConnectorRegistration(
    Guid ConnectorId,
    Guid TenantId,
    string DisplayName,
    string? CertificateThumbprint,
    bool Revoked);

public sealed record SubscriptionEntitlement(
    Guid EntitlementId,
    Guid TenantId,
    Guid ConnectorId,
    SubscriptionStatus Status,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int PageLimit,
    int PagesReserved,
    int PagesSettled,
    bool OfflineReviewAllowed);

public sealed record QuotaReservation(
    Guid ReservationId,
    Guid TenantId,
    Guid JobId,
    int PageCount,
    DateTimeOffset ReservedAt,
    DateTimeOffset? SettledAt,
    bool Released);

public sealed record OcrDocumentPage(
    int Page,
    string MimeType,
    string Sha256,
    ReadOnlyMemory<byte> Bytes);

public sealed record OcrDocument(
    Guid JobId,
    string SourceSha256,
    IReadOnlyList<OcrDocumentPage> Pages);

public sealed record OcrProviderResult(
    string Model,
    string Json,
    int InputUnits,
    int OutputUnits,
    DateTimeOffset ProcessedAt);

public sealed record OcrJob(
    Guid JobId,
    Guid TenantId,
    Guid ConnectorId,
    int PageCount,
    string SourceSha256,
    OcrJobState State,
    Guid ReservationId,
    string? ResultJson,
    string? ProviderModel,
    string? FailureCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PharmaAttributes(
    string? ActiveIngredient,
    string? Strength,
    string? DosageForm,
    string? Pack,
    string? Manufacturer);

public sealed record CanonicalProduct(
    Guid CanonicalProductId,
    string DisplayName,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Identifiers,
    PharmaAttributes Attributes,
    string EmbeddingVersion,
    float[]? Embedding);

public sealed record CanonicalSearchQuery(
    string Description,
    string? VendorItemCode,
    PharmaAttributes Attributes,
    string Locale,
    int Limit);

public sealed record CanonicalCandidate(
    Guid CanonicalProductId,
    string DisplayName,
    PharmaAttributes Attributes,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> HardMismatches,
    bool RequiresLocalResolution);

public sealed record AuditEvent(
    Guid EventId,
    Guid TenantId,
    string ActorType,
    string ActorReference,
    string Action,
    string TargetReference,
    string Result,
    Guid CorrelationId,
    DateTimeOffset OccurredAt);

public sealed class QuotaExceededException(int requested, int remaining)
    : Exception($"OCR quota exhausted: requested {requested} pages with {remaining} remaining.")
{
    public int Requested { get; } = requested;

    public int Remaining { get; } = remaining;
}

public sealed class EntitlementRejectedException(string reason)
    : Exception(reason);

public sealed class OcrProviderException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
}
