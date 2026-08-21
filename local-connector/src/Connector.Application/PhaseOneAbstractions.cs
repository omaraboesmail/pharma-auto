using PharmaAuto.Connector.Domain;

namespace PharmaAuto.Connector.Application;

public sealed record CatalogSearchHit(
    LocalCatalogItem Item,
    IReadOnlyList<string> ReasonCodes);

public sealed record GeniusItemRow(
    decimal ItemId,
    string? ItemCode,
    string? SecondaryCode,
    string? InternationalCode,
    byte[]? ArabicNameBytes,
    byte[]? EnglishNameBytes,
    string? ActiveIngredient,
    string? Strength,
    bool HasExpiry,
    bool Active);

public sealed record GeniusItemBarcodeRow(decimal ItemId, string Barcode);

public sealed record GeniusItemVendorCodeRow(
    decimal ItemId,
    decimal VendorId,
    string VendorItemCode);

public sealed record GeniusVendorRow(
    decimal VendorId,
    string? Code,
    string? ArabicName,
    string? EnglishName,
    bool Active);

public sealed record FileInspection(
    string MimeType,
    long Length,
    int? WidthPixels,
    int? HeightPixels,
    IReadOnlyList<string> QualityFlags);

public sealed record SaasOcrResponse(
    string State,
    string? ResultJson,
    string? FailureCode,
    string? ProviderModel);

public sealed record SaasCanonicalCandidate(
    Guid CanonicalProductId,
    string DisplayName,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<string> HardMismatches);

public interface ISidecarStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task SavePairingSessionAsync(PairingSession session, CancellationToken cancellationToken);

    Task<bool> ConsumePairingSessionAsync(
        Guid sessionId,
        ReadOnlyMemory<byte> secretHash,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken);

    Task SaveDeviceAsync(DeviceRegistration device, CancellationToken cancellationToken);

    Task<DeviceRegistration?> GetDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DeviceRegistration>> ListDevicesAsync(
        CancellationToken cancellationToken);

    Task<bool> RevokeDeviceAsync(
        Guid deviceId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken);

    Task TouchDeviceAsync(
        Guid deviceId,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken);

    Task SaveChallengeAsync(AccessChallenge challenge, CancellationToken cancellationToken);

    Task<AccessChallenge?> ConsumeChallengeAsync(
        Guid challengeId,
        Guid deviceId,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken);

    Task CreateJobAsync(InvoiceJob job, CancellationToken cancellationToken);

    Task<InvoiceJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InvoiceJob>> ListJobsAsync(
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<InvoiceJob>> ListJobsByStateAsync(
        IReadOnlyCollection<InvoiceJobState> states,
        int limit,
        CancellationToken cancellationToken);

    Task<bool> TransitionJobAsync(
        Guid jobId,
        InvoiceJobState expected,
        InvoiceJobState next,
        DateTimeOffset changedAt,
        string? failureCode,
        Guid? revisionId,
        CancellationToken cancellationToken);

    Task SaveChunkAsync(UploadChunk chunk, CancellationToken cancellationToken);

    Task<IReadOnlyList<UploadChunk>> GetChunksAsync(
        Guid jobId,
        int page,
        CancellationToken cancellationToken);

    Task DeleteChunksAsync(Guid jobId, int page, CancellationToken cancellationToken);

    Task SavePageAsync(DocumentPage page, CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentPage>> GetPagesAsync(
        Guid jobId,
        CancellationToken cancellationToken);

    Task SaveRevisionAsync(
        InvoiceRevisionRecord revision,
        CancellationToken cancellationToken);

    Task<InvoiceRevisionRecord?> GetRevisionAsync(
        Guid revisionId,
        CancellationToken cancellationToken);

    Task<bool> ConfirmRevisionAsync(
        Guid revisionId,
        Guid deviceId,
        DateTimeOffset confirmedAt,
        CancellationToken cancellationToken);

    Task ReplaceCatalogAsync(
        IAsyncEnumerable<LocalCatalogItem> items,
        IAsyncEnumerable<LocalVendor> vendors,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogSearchHit>> SearchItemsAsync(
        LocalMatchQuery query,
        CancellationToken cancellationToken);

    Task<LocalCatalogItem?> GetCatalogItemAsync(
        string localItemReference,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LocalVendor>> SearchVendorsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken);

    Task<LocalVendor?> GetCatalogVendorAsync(
        string localVendorReference,
        CancellationToken cancellationToken);

    Task<CatalogProjectionSummary?> GetCatalogProjectionSummaryAsync(
        CancellationToken cancellationToken);

    Task SaveCatalogProjectionSummaryAsync(
        CatalogProjectionSummary summary,
        CancellationToken cancellationToken);

    Task AppendAuditAsync(AuditRecord record, CancellationToken cancellationToken);
}

public interface IDocumentObjectStore
{
    Task<string> WriteAsync(
        string category,
        Guid jobId,
        string objectName,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken);

    Task<byte[]> ReadAsync(string objectReference, CancellationToken cancellationToken);

    Task DeleteAsync(string objectReference, CancellationToken cancellationToken);

    Task<int> DeleteExpiredAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken);
}

public interface IFileSafetyInspector
{
    Task<FileInspection> InspectAsync(
        ReadOnlyMemory<byte> content,
        string claimedMimeType,
        CancellationToken cancellationToken);
}

public interface IGeniusCatalogReader
{
    IAsyncEnumerable<GeniusItemRow> ReadItemsAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<GeniusItemBarcodeRow> ReadBarcodesAsync(
        CancellationToken cancellationToken);

    IAsyncEnumerable<GeniusItemVendorCodeRow> ReadVendorCodesAsync(
        CancellationToken cancellationToken);

    IAsyncEnumerable<GeniusVendorRow> ReadVendorsAsync(CancellationToken cancellationToken);
}

public interface ISaasClient
{
    Task<string> GetEntitlementAsync(CancellationToken cancellationToken);

    Task<SaasOcrResponse> ProcessOcrAsync(
        Guid jobId,
        string sourceSha256,
        IReadOnlyList<(DocumentPage Metadata, byte[] Content)> pages,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SaasCanonicalCandidate>> SearchCanonicalAsync(
        string description,
        string? vendorItemCode,
        string? activeIngredient,
        string? strength,
        string? dosageForm,
        string? pack,
        CancellationToken cancellationToken);
}

public interface IInvoiceWorkflowQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken);

    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken);
}
