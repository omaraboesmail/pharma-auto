using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using PharmaAuto.Connector.Infrastructure;

namespace PharmaAuto.Connector.ControlUi;

public sealed class ControlApiClient : IDisposable
{
    private readonly HttpClient client;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) }
    };

    public ControlApiClient(ControlUiSettings settings)
    {
        var expectedCertificateHash = settings.CertificateSha256;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate is not null && FixedEquals(
                    Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())),
                    expectedCertificateHash)
        };
        client = new HttpClient(handler)
        {
            BaseAddress = settings.BaseUrl,
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.Add("X-Control-Key", settings.ControlKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<ControlHealth?> GetHealthAsync(CancellationToken cancellationToken) =>
        client.GetFromJsonAsync<ControlHealth>(
            "/control/v1/health",
            jsonOptions,
            cancellationToken);

    public Task<IReadOnlyList<ControlJob>?> GetJobsAsync(CancellationToken cancellationToken) =>
        client.GetFromJsonAsync<IReadOnlyList<ControlJob>>(
            "/control/v1/jobs",
            jsonOptions,
            cancellationToken);

    public Task<IReadOnlyList<ControlDevice>?> GetDevicesAsync(CancellationToken cancellationToken) =>
        client.GetFromJsonAsync<IReadOnlyList<ControlDevice>>(
            "/control/v1/devices",
            jsonOptions,
            cancellationToken);

    public async Task<PairingBootstrap> CreatePairingAsync(CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(
            "/control/v1/pairing-sessions",
            content: null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PairingBootstrap>(
            jsonOptions,
            cancellationToken)
            ?? throw new InvalidOperationException("Pairing response was empty.");
    }

    public async Task<CatalogSummary> RebuildCatalogAsync(CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(
            "/control/v1/catalog/rebuild",
            content: null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CatalogSummary>(
            jsonOptions,
            cancellationToken)
            ?? throw new InvalidOperationException("Catalog rebuild response was empty.");
    }

    public async Task RevokeDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(
            $"/control/v1/devices/{deviceId:D}/revoke",
            content: null,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public void Dispose() => client.Dispose();

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var problem = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"Connector control request failed with HTTP {(int)response.StatusCode}: " +
            Truncate(problem, 500));
    }

    private static bool FixedEquals(string first, string second)
    {
        var firstBytes = System.Text.Encoding.ASCII.GetBytes(first);
        var secondBytes = System.Text.Encoding.ASCII.GetBytes(second);
        return firstBytes.Length == secondBytes.Length &&
            CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes);
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];
}

public sealed record ControlUiSettings(
    Uri BaseUrl,
    string DataRoot,
    string ControlKey,
    string CertificateSha256)
{
    public static ControlUiSettings Load()
    {
        var dataRoot = Environment.GetEnvironmentVariable("PHARMA_AUTO_CONNECTOR_DATA_ROOT")
            ?? Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\PharmaAuto\Connector",
                "DataRoot",
                null) as string
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "PharmaAuto",
                "Connector");
        var installedSettings = LoadInstalledSettings(dataRoot);
        var baseUrl = new Uri(
            Environment.GetEnvironmentVariable("PHARMA_AUTO_CONNECTOR_BASE_URL")
                ?? $"https://localhost:{installedSettings.Port}");
        var keyStore = new WindowsMachineKeyStore(Path.Combine(dataRoot, "keys"));
        var controlKey = keyStore.GetBase64Url("control-access", 32);
        var configuredHash = Environment.GetEnvironmentVariable(
            "PHARMA_AUTO_CONNECTOR_CERTIFICATE_SHA256");
        var installedHash = string.IsNullOrWhiteSpace(installedSettings.CertificateThumbprint)
            ? null
            : LoadInstalledCertificateHash(installedSettings.CertificateThumbprint);
        var certificateHash = configuredHash
            ?? installedHash
            ?? LoadDevelopmentCertificateHash(dataRoot, keyStore);
        return new ControlUiSettings(baseUrl, dataRoot, controlKey, certificateHash);
    }

    private static (int Port, string? CertificateThumbprint) LoadInstalledSettings(
        string dataRoot)
    {
        var path = Path.Combine(dataRoot, "connector-settings.json");
        if (!File.Exists(path))
        {
            return (7443, null);
        }
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        if (!document.RootElement.TryGetProperty("Connector", out var connector))
        {
            throw new InvalidOperationException("Installed Connector settings are invalid.");
        }
        var port = connector.TryGetProperty("Port", out var portNode)
            ? portNode.GetInt32()
            : 7443;
        var thumbprint = connector.TryGetProperty(
                "TlsCertificateThumbprint",
                out var thumbprintNode)
            ? thumbprintNode.GetString()
            : null;
        return (port, thumbprint);
    }

    private static string LoadInstalledCertificateHash(string thumbprint)
    {
        using var certificate = new ConnectorCertificateProvider().LoadByThumbprint(thumbprint);
        return ConnectorCertificateProvider.Sha256(certificate);
    }

    private static string LoadDevelopmentCertificateHash(
        string dataRoot,
        WindowsMachineKeyStore keyStore)
    {
        var pfxPath = Path.Combine(dataRoot, "tls", "connector-development.pfx");
        if (!File.Exists(pfxPath))
        {
            throw new InvalidOperationException(
                "Connector TLS certificate is unavailable. Install or start the Connector service first.");
        }
        var certificate = new ConnectorCertificateProvider().LoadOrCreateDevelopment(
            pfxPath,
            keyStore.GetBase64Url("development-pfx-password", 32));
        using (certificate)
        {
            return ConnectorCertificateProvider.Sha256(certificate);
        }
    }
}

public sealed record ControlHealth(
    Guid ConnectorId,
    string PharmacyDisplayName,
    string BaseUrl,
    string CertificateSha256,
    string DatabaseProfileId,
    CatalogSummary? Catalog,
    int QueueDepth,
    DateTimeOffset? OldestPendingJob,
    bool GeniusWritesEnabled);

public sealed record CatalogSummary(
    int ItemCount,
    int VendorCount,
    int BarcodeCount,
    int VendorCodeCount,
    int UntrustedLabelCount,
    int IdenticalLanguageFieldCount,
    DateTimeOffset CompletedAt,
    bool GeniusWritePerformed);

public sealed record ControlJob(
    Guid JobId,
    string State,
    int PageCount,
    int UploadedPageCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? FailureCode,
    bool GeniusWritePerformed);

public sealed record ControlDevice(
    Guid DeviceId,
    string DisplayName,
    DateTimeOffset PairedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastSeenAt);

public sealed record PairingBootstrap(
    Guid SessionId,
    Guid ConnectorId,
    string PharmacyDisplayName,
    string BaseUrl,
    string CertificateSha256,
    string OneTimeSecret,
    DateTimeOffset ExpiresAt,
    string QrPayload);
