using System.Globalization;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PharmaAuto.Connector.Application;
using PharmaAuto.Connector.Domain;

namespace PharmaAuto.Connector.Infrastructure;

public sealed record SaasClientOptions(
    Uri BaseUrl,
    Guid TenantId,
    Guid ConnectorId,
    byte[] RequestSigningSecret);

public sealed class SaasClient(
    HttpClient httpClient,
    SaasClientOptions options,
    TimeProvider timeProvider) : ISaasClient
{
    public async Task<string> GetEntitlementAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            "/api/v1/entitlements/current",
            null,
            cancellationToken);
        var entitlementJson = await ReadSuccessBodyAsync(response, cancellationToken);
        using var keyResponse = await SendAsync(
            HttpMethod.Get,
            "/api/v1/signing-keys/current",
            null,
            cancellationToken);
        var signingKeyJson = await ReadSuccessBodyAsync(keyResponse, cancellationToken);
        VerifyEntitlement(entitlementJson, signingKeyJson);
        return entitlementJson;
    }

    public async Task<SaasOcrResponse> ProcessOcrAsync(
        Guid jobId,
        string sourceSha256,
        IReadOnlyList<(DocumentPage Metadata, byte[] Content)> pages,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["sourceSha256"] = sourceSha256,
            ["pages"] = new JsonArray(pages
                .OrderBy(page => page.Metadata.Page)
                .Select(page => (JsonNode?)new JsonObject
                {
                    ["page"] = page.Metadata.Page,
                    ["mimeType"] = page.Metadata.MimeType,
                    ["sha256"] = page.Metadata.Sha256,
                    ["base64Data"] = Convert.ToBase64String(page.Content)
                })
                .ToArray())
        };
        var path = $"/api/v1/ocr/jobs/{jobId:D}/process";
        using var response = await SendAsync(HttpMethod.Post, path, body, cancellationToken);
        var json = await ReadSuccessBodyAsync(response, cancellationToken);
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new WorkflowException("SAAS_RESPONSE_INVALID", "SaaS OCR response is invalid.");
        return new SaasOcrResponse(
            root["state"]?.GetValue<string>() ?? "FAILED",
            root["result"]?.ToJsonString(),
            root["failureCode"]?.GetValue<string?>(),
            root["providerModel"]?.GetValue<string?>());
    }

    public async Task<IReadOnlyList<SaasCanonicalCandidate>> SearchCanonicalAsync(
        string description,
        string? vendorItemCode,
        string? activeIngredient,
        string? strength,
        string? dosageForm,
        string? pack,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["description"] = description,
            ["vendorItemCode"] = vendorItemCode,
            ["attributes"] = new JsonObject
            {
                ["activeIngredient"] = activeIngredient,
                ["strength"] = strength,
                ["dosageForm"] = dosageForm,
                ["pack"] = pack,
                ["manufacturer"] = null
            },
            ["locale"] = ContainsArabic(description) ? "ar-EG" : "en-EG",
            ["limit"] = 10
        };
        using var response = await SendAsync(
            HttpMethod.Post,
            "/api/v1/canonical-products/search",
            body,
            cancellationToken);
        var json = await ReadSuccessBodyAsync(response, cancellationToken);
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new WorkflowException("SAAS_RESPONSE_INVALID", "SaaS match response is invalid.");
        var candidates = root["candidates"]?.AsArray() ?? [];
        return candidates.Select(node =>
        {
            var candidate = node?.AsObject()
                ?? throw new WorkflowException("SAAS_RESPONSE_INVALID", "Canonical candidate is invalid.");
            return new SaasCanonicalCandidate(
                Guid.Parse(candidate["canonicalProductId"]!.GetValue<string>()),
                candidate["displayName"]!.GetValue<string>(),
                ReadStrings(candidate["reasonCodes"]),
                ReadStrings(candidate["hardMismatches"]));
        }).ToArray();
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        JsonNode? body,
        CancellationToken cancellationToken)
    {
        var bodyBytes = body is null
            ? Array.Empty<byte>()
            : Encoding.UTF8.GetBytes(body.ToJsonString());
        var bodyHash = Convert.ToHexStringLower(SHA256.HashData(bodyBytes));
        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        var canonical = string.Join(
            '\n',
            method.Method.ToUpperInvariant(),
            path,
            timestamp.ToString(CultureInfo.InvariantCulture),
            nonce,
            bodyHash);
        var signature = Base64Url(
            HMACSHA256.HashData(
                options.RequestSigningSecret,
                Encoding.UTF8.GetBytes(canonical)));

        var request = new HttpRequestMessage(method, new Uri(options.BaseUrl, path));
        request.Headers.Add("X-Tenant-Id", options.TenantId.ToString("D"));
        request.Headers.Add("X-Connector-Id", options.ConnectorId.ToString("D"));
        request.Headers.Add(
            "X-Request-Timestamp",
            timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("X-Request-Nonce", nonce);
        request.Headers.Add("X-Content-SHA256", bodyHash);
        request.Headers.Add("X-Request-Signature", signature);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(bodyBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        try
        {
            return await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private static async Task<string> ReadSuccessBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new WorkflowException(
                $"SAAS_HTTP_{(int)response.StatusCode}",
                "SaaS rejected the request; raw invoice content was not logged.");
        }
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static IReadOnlyList<string> ReadStrings(JsonNode? node) =>
        node?.AsArray()
            .Select(value => value?.GetValue<string>() ?? string.Empty)
            .Where(value => value.Length > 0)
            .ToArray() ?? [];

    private static bool ContainsArabic(string value) =>
        value.Any(character => character is >= '\u0600' and <= '\u08ff');

    private void VerifyEntitlement(string entitlementJson, string signingKeyJson)
    {
        var entitlement = JsonNode.Parse(entitlementJson)?.AsObject()
            ?? throw new WorkflowException(
                "ENTITLEMENT_INVALID",
                "SaaS entitlement is not a JSON object.");
        var keyDocument = JsonNode.Parse(signingKeyJson)?.AsObject()
            ?? throw new WorkflowException(
                "ENTITLEMENT_KEY_INVALID",
                "SaaS signing-key response is invalid.");
        var signatureText = entitlement["signature"]?.GetValue<string>();
        var keyId = entitlement["keyId"]?.GetValue<string>();
        var algorithm = entitlement["algorithm"]?.GetValue<string>();
        var publicKeyPem = keyDocument["subjectPublicKeyInfoPem"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(signatureText) ||
            string.IsNullOrWhiteSpace(keyId) ||
            algorithm != "ES256" ||
            keyDocument["algorithm"]?.GetValue<string>() != algorithm ||
            keyDocument["keyId"]?.GetValue<string>() != keyId ||
            string.IsNullOrWhiteSpace(publicKeyPem))
        {
            throw new WorkflowException(
                "ENTITLEMENT_KEY_INVALID",
                "SaaS entitlement signing metadata is inconsistent.");
        }

        var signatureMarker = $"\"signature\":\"{signatureText}\"";
        var markerIndex = entitlementJson.IndexOf(signatureMarker, StringComparison.Ordinal);
        if (markerIndex < 0 ||
            markerIndex != entitlementJson.LastIndexOf(signatureMarker, StringComparison.Ordinal))
        {
            throw new WorkflowException(
                "ENTITLEMENT_SIGNATURE_INVALID",
                "SaaS entitlement signature field is not canonical.");
        }
        var unsignedJson = string.Concat(
            entitlementJson.AsSpan(0, markerIndex),
            "\"signature\":\"\"",
            entitlementJson.AsSpan(markerIndex + signatureMarker.Length));
        var payload = Encoding.UTF8.GetBytes(unsignedJson);
        byte[] signature;
        try
        {
            signature = DecodeBase64Url(signatureText);
        }
        catch (FormatException exception)
        {
            throw new WorkflowException(
                "ENTITLEMENT_SIGNATURE_INVALID",
                $"SaaS entitlement signature is malformed: {exception.Message}");
        }
        using var verifier = ECDsa.Create();
        verifier.ImportFromPem(publicKeyPem);
        if (signature.Length != 64 || !verifier.VerifyData(
                payload,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        {
            throw new WorkflowException(
                "ENTITLEMENT_SIGNATURE_INVALID",
                "SaaS entitlement signature verification failed.");
        }

        var tenantId = ReadGuid(entitlement, "tenantId");
        var connectorId = ReadGuid(entitlement, "connectorId");
        var validFrom = ReadTimestamp(entitlement, "validFrom");
        var validUntil = ReadTimestamp(entitlement, "validUntil");
        var now = timeProvider.GetUtcNow();
        var pageLimit = entitlement["pageLimit"]?.GetValue<int>() ?? 0;
        var pagesSettled = entitlement["pagesSettled"]?.GetValue<int>() ?? -1;
        if (entitlement["schemaVersion"]?.GetValue<string>() != "1.0" ||
            tenantId != options.TenantId ||
            connectorId != options.ConnectorId ||
            entitlement["subscriptionStatus"]?.GetValue<string>() != "ACTIVE" ||
            validFrom > now.AddMinutes(5) ||
            validUntil <= now ||
            pageLimit <= 0 ||
            pagesSettled is < 0 || pagesSettled > pageLimit ||
            entitlement["offlineReviewAllowed"]?.GetValue<bool>() != true ||
            entitlement["geniusWritesAllowed"]?.GetValue<bool>() != false)
        {
            throw new WorkflowException(
                "ENTITLEMENT_NOT_ACTIVE",
                "SaaS entitlement does not authorize the Phase 1 read-only workflow.");
        }
    }

    private static Guid ReadGuid(JsonObject source, string name) =>
        Guid.TryParse(source[name]?.GetValue<string>(), out var value)
            ? value
            : throw new WorkflowException(
                "ENTITLEMENT_INVALID",
                $"SaaS entitlement {name} is invalid.");

    private static DateTimeOffset ReadTimestamp(JsonObject source, string name) =>
        DateTimeOffset.TryParse(
            source[name]?.GetValue<string>(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var value)
            ? value
            : throw new WorkflowException(
                "ENTITLEMENT_INVALID",
                $"SaaS entitlement {name} is invalid.");

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
