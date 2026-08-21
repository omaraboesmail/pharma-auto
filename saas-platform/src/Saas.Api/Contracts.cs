using System.Text.Json.Nodes;

namespace PharmaAuto.Saas.Api;

public sealed record OcrPageRequest(
    int Page,
    string MimeType,
    string Sha256,
    string Base64Data);

public sealed record ProcessOcrRequest(
    string SourceSha256,
    IReadOnlyList<OcrPageRequest> Pages);

public sealed record PharmaAttributesRequest(
    string? ActiveIngredient,
    string? Strength,
    string? DosageForm,
    string? Pack,
    string? Manufacturer);

public sealed record CanonicalSearchRequest(
    string Description,
    string? VendorItemCode,
    PharmaAttributesRequest? Attributes,
    string Locale,
    int Limit);

public sealed record OcrJobResponse(
    Guid JobId,
    string State,
    int PageCount,
    string SourceSha256,
    string? ProviderModel,
    string? FailureCode,
    JsonNode? Result,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool GeniusWritePerformed);

public sealed record SignedEntitlementResponse(
    string SchemaVersion,
    Guid EntitlementId,
    Guid TenantId,
    Guid ConnectorId,
    string SubscriptionStatus,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidUntil,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int PageLimit,
    int PagesReserved,
    int PagesSettled,
    bool OfflineReviewAllowed,
    bool GeniusWritesAllowed,
    string Algorithm,
    string KeyId,
    string Signature);
