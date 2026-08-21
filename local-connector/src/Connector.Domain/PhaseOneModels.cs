namespace PharmaAuto.Connector.Domain;

public enum InvoiceJobState
{
    Captured,
    LocallyValidated,
    OcrReserved,
    OcrProcessing,
    OcrValidated,
    Matching,
    AwaitingUserReview,
    Confirmed,
    Rejected,
    OcrFailed,
    MatchingFailed
}

public enum CatalogQualityFlag
{
    Unverified,
    LanguageFieldsIdentical,
    EmptyOrBlank,
    MalformedBidi,
    TruncatedOrCorrupt,
    CanonicalOverlayAvailable,
    ManuallyConfirmed
}

public enum CatalogDisplayDirection
{
    Auto,
    Ltr,
    Rtl
}

public sealed record ConnectorIdentity(
    Guid ConnectorId,
    Guid TenantId,
    string PharmacyDisplayName,
    string BaseUrl,
    string CertificateSha256,
    string DatabaseProfileId);

public sealed record PairingSession(
    Guid SessionId,
    byte[] SecretHash,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConsumedAt);

public sealed record DeviceRegistration(
    Guid DeviceId,
    string DisplayName,
    byte[] PublicKeySubjectPublicKeyInfo,
    DateTimeOffset PairedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastSeenAt);

public sealed record AccessChallenge(
    Guid ChallengeId,
    Guid DeviceId,
    string Nonce,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConsumedAt);

public sealed record InvoiceJob(
    Guid JobId,
    Guid DeviceId,
    InvoiceJobState State,
    int ExpectedPageCount,
    int UploadedPageCount,
    Guid? CurrentRevisionId,
    string? FailureCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DocumentPage(
    Guid JobId,
    int Page,
    string MimeType,
    string Sha256,
    string ObjectReference,
    long Length,
    DateTimeOffset UploadedAt);

public sealed record UploadChunk(
    Guid JobId,
    int Page,
    int ChunkIndex,
    int ChunkCount,
    string ChunkSha256,
    string PageSha256,
    string MimeType,
    string ObjectReference,
    long Length,
    DateTimeOffset UploadedAt);

public sealed record CatalogIdentifiers(
    string? ItemCode,
    string? SecondaryCode,
    string? InternationalCode,
    IReadOnlyList<string> Barcodes,
    IReadOnlyList<string> VendorItemCodes);

public sealed record LocalCatalogItem(
    string LocalItemReference,
    decimal GeniusItemId,
    string? RawArabicLabel,
    string? RawEnglishLabel,
    string? DisplayLabel,
    string? RawArabicHash,
    string? RawEnglishHash,
    CatalogDisplayDirection DisplayDirection,
    IReadOnlyList<CatalogQualityFlag> QualityFlags,
    CatalogIdentifiers Identifiers,
    string? ActiveIngredient,
    string? Strength,
    string? DosageForm,
    string? Pack,
    bool HasExpiry,
    bool Active,
    DateTimeOffset ProjectedAt);

public sealed record LocalVendor(
    string LocalVendorReference,
    decimal GeniusVendorId,
    string? Code,
    string DisplayName,
    bool Active,
    DateTimeOffset ProjectedAt);

public sealed record LocalMatchQuery(
    string Description,
    string? VendorItemCode,
    string? Barcode,
    string? ItemCode,
    string? ActiveIngredient,
    string? Strength,
    string? DosageForm,
    string? Pack,
    int Limit);

public sealed record CatalogAttributes(
    string? ActiveIngredient,
    string? Strength,
    string? DosageForm,
    string? Pack);

public sealed record LocalItemCandidate(
    string SchemaVersion,
    string LocalItemReference,
    string DisplayLabel,
    string? RawLabel,
    string? RawLabelHash,
    string LabelSource,
    CatalogDisplayDirection DisplayDirection,
    IReadOnlyList<CatalogQualityFlag> QualityFlags,
    CatalogIdentifiers Identifiers,
    CatalogAttributes Attributes,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> HardMismatches,
    bool RequiresManualConfirmation);

public sealed record LocalVendorCandidate(
    string LocalVendorReference,
    string DisplayName,
    string? Code,
    IReadOnlyList<string> ReasonCodes,
    bool RequiresManualConfirmation);

public sealed record CatalogProjectionSummary(
    int ItemCount,
    int VendorCount,
    int BarcodeCount,
    int VendorCodeCount,
    int UntrustedLabelCount,
    int IdenticalLanguageFieldCount,
    DateTimeOffset CompletedAt,
    bool GeniusWritePerformed);

public sealed record InvoiceRevisionRecord(
    Guid RevisionId,
    Guid JobId,
    int RevisionNumber,
    string Status,
    string Json,
    Guid CreatedByDeviceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt);

public sealed record AuditRecord(
    Guid EventId,
    string ActorType,
    string ActorReference,
    string Action,
    string TargetReference,
    string Result,
    Guid CorrelationId,
    DateTimeOffset OccurredAt);

public static class InvoiceJobTransitions
{
    private static readonly IReadOnlyDictionary<InvoiceJobState, HashSet<InvoiceJobState>> Allowed =
        new Dictionary<InvoiceJobState, HashSet<InvoiceJobState>>
        {
            [InvoiceJobState.Captured] = [InvoiceJobState.LocallyValidated, InvoiceJobState.Rejected],
            [InvoiceJobState.LocallyValidated] = [InvoiceJobState.OcrReserved, InvoiceJobState.Rejected],
            [InvoiceJobState.OcrReserved] = [InvoiceJobState.OcrProcessing, InvoiceJobState.OcrFailed],
            [InvoiceJobState.OcrProcessing] = [InvoiceJobState.OcrValidated, InvoiceJobState.OcrFailed],
            [InvoiceJobState.OcrValidated] = [InvoiceJobState.Matching, InvoiceJobState.MatchingFailed],
            [InvoiceJobState.Matching] =
                [InvoiceJobState.AwaitingUserReview, InvoiceJobState.MatchingFailed],
            [InvoiceJobState.AwaitingUserReview] = [InvoiceJobState.Confirmed, InvoiceJobState.Rejected],
            [InvoiceJobState.OcrFailed] = [InvoiceJobState.OcrReserved, InvoiceJobState.Rejected],
            [InvoiceJobState.MatchingFailed] = [InvoiceJobState.Matching, InvoiceJobState.Rejected]
        };

    public static void EnsureAllowed(InvoiceJobState current, InvoiceJobState next)
    {
        if (current == next)
        {
            return;
        }
        if (!Allowed.TryGetValue(current, out var allowed) || !allowed.Contains(next))
        {
            throw new InvalidOperationException(
                $"Invoice job transition {current} -> {next} is not allowed.");
        }
    }
}
