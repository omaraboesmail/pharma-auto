using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PharmaAuto.Connector.Domain;

namespace PharmaAuto.Connector.Application;

public sealed record PairingBootstrap(
    Guid SessionId,
    Guid ConnectorId,
    string PharmacyDisplayName,
    string BaseUrl,
    string CertificateSha256,
    string OneTimeSecret,
    DateTimeOffset ExpiresAt,
    string QrPayload);

public sealed record PairingClaimResult(
    Guid DeviceId,
    Guid ConnectorId,
    string PharmacyDisplayName,
    string BaseUrl,
    string CertificateSha256);

public sealed record AccessTokenResult(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid DeviceId);

public sealed record DeviceAccessPrincipal(Guid DeviceId, DateTimeOffset ExpiresAt);

public sealed class PairingService(
    ISidecarStore store,
    ConnectorIdentity connector,
    TimeProvider timeProvider)
{
    public async Task<PairingBootstrap> CreateSessionAsync(
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var secret = Base64Url(secretBytes);
        var session = new PairingSession(
            Guid.NewGuid(),
            SHA256.HashData(secretBytes),
            now.AddMinutes(5),
            now,
            null);
        await store.SavePairingSessionAsync(session, cancellationToken);

        var query = string.Join(
            '&',
            "v=1",
            $"session={Uri.EscapeDataString(session.SessionId.ToString("D"))}",
            $"connector={Uri.EscapeDataString(connector.ConnectorId.ToString("D"))}",
            $"baseUrl={Uri.EscapeDataString(connector.BaseUrl)}",
            $"certificateSha256={Uri.EscapeDataString(connector.CertificateSha256)}",
            $"secret={Uri.EscapeDataString(secret)}");
        return new PairingBootstrap(
            session.SessionId,
            connector.ConnectorId,
            connector.PharmacyDisplayName,
            connector.BaseUrl,
            connector.CertificateSha256,
            secret,
            session.ExpiresAt,
            $"pharmaauto://pair?{query}");
    }

    public async Task<PairingClaimResult> ClaimAsync(
        Guid sessionId,
        string oneTimeSecret,
        string deviceDisplayName,
        string publicKeySubjectPublicKeyInfoBase64,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceDisplayName) || deviceDisplayName.Length > 120)
        {
            throw new ArgumentException("Device display name must contain 1..120 characters.");
        }

        byte[] secret;
        byte[] publicKey;
        try
        {
            secret = FromBase64Url(oneTimeSecret);
            publicKey = Convert.FromBase64String(publicKeySubjectPublicKeyInfoBase64);
            using var validator = ECDsa.Create();
            validator.ImportSubjectPublicKeyInfo(publicKey, out var bytesRead);
            if (bytesRead != publicKey.Length || validator.KeySize != 256)
            {
                throw new CryptographicException("Unsupported device key.");
            }
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("Pairing secret or public key encoding is invalid.", exception);
        }
        catch (CryptographicException exception)
        {
            throw new ArgumentException("Device public key must be ECDSA P-256 SPKI.", exception);
        }

        var now = timeProvider.GetUtcNow();
        var consumed = await store.ConsumePairingSessionAsync(
            sessionId,
            SHA256.HashData(secret),
            now,
            cancellationToken);
        if (!consumed)
        {
            throw new InvalidOperationException("Pairing session is invalid, expired, or already used.");
        }

        var device = new DeviceRegistration(
            Guid.NewGuid(),
            deviceDisplayName.Trim(),
            publicKey,
            now,
            null,
            now);
        await store.SaveDeviceAsync(device, cancellationToken);
        await store.AppendAuditAsync(
            new AuditRecord(
                Guid.NewGuid(),
                "DEVICE",
                device.DeviceId.ToString("D"),
                "DEVICE_PAIRED",
                device.DeviceId.ToString("D"),
                "SUCCESS",
                sessionId,
                now),
            cancellationToken);
        return new PairingClaimResult(
            device.DeviceId,
            connector.ConnectorId,
            connector.PharmacyDisplayName,
            connector.BaseUrl,
            connector.CertificateSha256);
    }

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}

public sealed class DeviceAuthenticationService(
    ISidecarStore store,
    ConnectorIdentity connector,
    byte[] tokenSigningKey,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<AccessChallenge> CreateChallengeAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var device = await store.GetDeviceAsync(deviceId, cancellationToken);
        if (device is null || device.RevokedAt is not null)
        {
            throw new UnauthorizedAccessException("Device is not registered or has been revoked.");
        }

        var now = timeProvider.GetUtcNow();
        var challenge = new AccessChallenge(
            Guid.NewGuid(),
            deviceId,
            Base64Url(RandomNumberGenerator.GetBytes(32)),
            now.AddMinutes(2),
            null);
        await store.SaveChallengeAsync(challenge, cancellationToken);
        return challenge;
    }

    public async Task<AccessTokenResult> ExchangeAsync(
        Guid deviceId,
        Guid challengeId,
        string signatureBase64,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var challenge = await store.ConsumeChallengeAsync(
            challengeId,
            deviceId,
            now,
            cancellationToken);
        var device = await store.GetDeviceAsync(deviceId, cancellationToken);
        if (challenge is null || device is null || device.RevokedAt is not null)
        {
            throw new UnauthorizedAccessException("Authentication challenge is invalid or expired.");
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException exception)
        {
            throw new UnauthorizedAccessException("Device signature encoding is invalid.", exception);
        }

        var canonical = string.Join(
            '\n',
            "PHARMA_AUTO_DEVICE_AUTH_V1",
            challenge.ChallengeId.ToString("D"),
            challenge.Nonce,
            connector.ConnectorId.ToString("D"),
            deviceId.ToString("D"));
        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(device.PublicKeySubjectPublicKeyInfo, out _);
        if (!verifier.VerifyData(
                Encoding.UTF8.GetBytes(canonical),
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        {
            throw new UnauthorizedAccessException("Device signature is invalid.");
        }

        var expiresAt = now.AddMinutes(15);
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(
            new { alg = "HS256", typ = "JWT" },
            JsonOptions));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                iss = connector.ConnectorId.ToString("D"),
                sub = deviceId.ToString("D"),
                iat = now.ToUnixTimeSeconds(),
                exp = expiresAt.ToUnixTimeSeconds(),
                jti = Guid.NewGuid().ToString("D")
            },
            JsonOptions));
        var unsigned = $"{header}.{payload}";
        var tokenSignature = Base64Url(
            HMACSHA256.HashData(tokenSigningKey, Encoding.ASCII.GetBytes(unsigned)));
        await store.TouchDeviceAsync(deviceId, now, cancellationToken);
        return new AccessTokenResult($"{unsigned}.{tokenSignature}", expiresAt, deviceId);
    }

    public async Task<DeviceAccessPrincipal?> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        var expected = Base64Url(
            HMACSHA256.HashData(
                tokenSigningKey,
                Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}")));
        if (!FixedEquals(expected, parts[2]))
        {
            return null;
        }

        try
        {
            using var payload = JsonDocument.Parse(FromBase64Url(parts[1]));
            var root = payload.RootElement;
            if (root.GetProperty("iss").GetString() != connector.ConnectorId.ToString("D") ||
                !Guid.TryParse(root.GetProperty("sub").GetString(), out var deviceId))
            {
                return null;
            }

            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("exp").GetInt64());
            if (expiresAt <= timeProvider.GetUtcNow())
            {
                return null;
            }
            var device = await store.GetDeviceAsync(deviceId, cancellationToken);
            return device is null || device.RevokedAt is not null
                ? null
                : new DeviceAccessPrincipal(deviceId, expiresAt);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool FixedEquals(string first, string second)
    {
        var firstBytes = Encoding.ASCII.GetBytes(first);
        var secondBytes = Encoding.ASCII.GetBytes(second);
        return firstBytes.Length == secondBytes.Length &&
            CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes);
    }

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
