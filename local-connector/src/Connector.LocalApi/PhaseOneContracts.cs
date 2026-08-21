using System.Text.Json;

namespace PharmaAuto.Connector.LocalApi;

public sealed record PairingClaimRequest(
    Guid SessionId,
    string OneTimeSecret,
    string DeviceDisplayName,
    string PublicKeySubjectPublicKeyInfoBase64);

public sealed record CreateChallengeRequest(Guid DeviceId);

public sealed record ExchangeTokenRequest(
    Guid DeviceId,
    Guid ChallengeId,
    string SignatureBase64);

public sealed record CreateInvoiceJobRequest(int PageCount);

public sealed record SaveRevisionRequest(JsonElement Revision, string Reason);
