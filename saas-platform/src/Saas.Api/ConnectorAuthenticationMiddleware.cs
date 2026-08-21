using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PharmaAuto.Saas.Application;

namespace PharmaAuto.Saas.Api;

public sealed record ConnectorAuthenticationOptions(
    byte[] SharedSecret,
    bool RequireClientCertificate,
    TimeSpan MaximumClockSkew,
    long MaximumBodyBytes);

public sealed record AuthenticatedConnector(Guid TenantId, Guid ConnectorId);

public sealed class ConnectorAuthenticationMiddleware(
    RequestDelegate next,
    ConnectorAuthenticationOptions options,
    TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> seenNonces = new();

    public async Task InvokeAsync(HttpContext context, ISaasStore store)
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1", StringComparison.Ordinal))
        {
            await next(context);
            return;
        }

        if (!TryReadHeaders(context.Request, out var headers, out var headerError))
        {
            await RejectAsync(context, headerError);
            return;
        }

        var now = timeProvider.GetUtcNow();
        if (headers.Timestamp < now - options.MaximumClockSkew ||
            headers.Timestamp > now + options.MaximumClockSkew)
        {
            await RejectAsync(context, "Connector request timestamp is outside the accepted window.");
            return;
        }

        PruneNonces(now);
        var nonceKey = $"{headers.ConnectorId:D}:{headers.Nonce}";
        if (!seenNonces.TryAdd(nonceKey, now))
        {
            await RejectAsync(context, "Connector request nonce was already used.");
            return;
        }

        var actualBodyHash = await HashBodyAsync(context.Request, options.MaximumBodyBytes);
        if (!FixedEquals(actualBodyHash, headers.BodySha256))
        {
            await RejectAsync(context, "Connector request body hash is invalid.");
            return;
        }

        var canonical = string.Join(
            '\n',
            context.Request.Method.ToUpperInvariant(),
            context.Request.Path.Value ?? "/",
            headers.Timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            headers.Nonce,
            headers.BodySha256);
        var expectedSignature = Base64Url(
            HMACSHA256.HashData(options.SharedSecret, Encoding.UTF8.GetBytes(canonical)));
        if (!FixedEquals(expectedSignature, headers.Signature))
        {
            await RejectAsync(context, "Connector request signature is invalid.");
            return;
        }

        var connector = await store.GetConnectorAsync(
            headers.TenantId,
            headers.ConnectorId,
            context.RequestAborted);
        if (connector is null || connector.Revoked)
        {
            await RejectAsync(context, "Connector identity is not active.");
            return;
        }

        if (options.RequireClientCertificate)
        {
            var certificate = await context.Connection.GetClientCertificateAsync(
                context.RequestAborted);
            if (certificate is null ||
                string.IsNullOrWhiteSpace(connector.CertificateThumbprint) ||
                !FixedEquals(
                    certificate.Thumbprint.ToUpperInvariant(),
                    connector.CertificateThumbprint.ToUpperInvariant()))
            {
                await RejectAsync(context, "Connector client certificate is missing or revoked.");
                return;
            }
        }

        context.Items[typeof(AuthenticatedConnector)] = new AuthenticatedConnector(
            headers.TenantId,
            headers.ConnectorId);
        await next(context);
    }

    private static async Task<string> HashBodyAsync(HttpRequest request, long maximumBytes)
    {
        if (request.ContentLength is > 0 && request.ContentLength > maximumBytes)
        {
            throw new BadHttpRequestException("Request body exceeds the configured limit.", 413);
        }

        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        var bufferedBody = new MemoryStream(
            request.ContentLength is > 0 and <= int.MaxValue
                ? (int)request.ContentLength.Value
                : 0);
        long total = 0;
        int read;
        try
        {
            while ((read = await request.Body.ReadAsync(
                       buffer,
                       request.HttpContext.RequestAborted)) > 0)
            {
                total += read;
                if (total > maximumBytes)
                {
                    throw new BadHttpRequestException(
                        "Request body exceeds the configured limit.",
                        413);
                }
                incremental.AppendData(buffer, 0, read);
                await bufferedBody.WriteAsync(
                    buffer.AsMemory(0, read),
                    request.HttpContext.RequestAborted);
            }
            bufferedBody.Position = 0;
            request.Body = bufferedBody;
            request.HttpContext.Response.RegisterForDispose(bufferedBody);
            return Convert.ToHexStringLower(incremental.GetHashAndReset());
        }
        catch
        {
            await bufferedBody.DisposeAsync();
            throw;
        }
    }

    private static bool TryReadHeaders(
        HttpRequest request,
        out SignedHeaders headers,
        out string error)
    {
        headers = default;
        error = "Connector authentication headers are missing or malformed.";
        if (!Guid.TryParse(request.Headers["X-Tenant-Id"], out var tenantId) ||
            !Guid.TryParse(request.Headers["X-Connector-Id"], out var connectorId) ||
            !long.TryParse(
                request.Headers["X-Request-Timestamp"],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var timestampSeconds))
        {
            return false;
        }

        var nonce = request.Headers["X-Request-Nonce"].ToString();
        var bodyHash = request.Headers["X-Content-SHA256"].ToString();
        var signature = request.Headers["X-Request-Signature"].ToString();
        if (nonce.Length is < 16 or > 128 ||
            bodyHash.Length != 64 ||
            signature.Length is < 43 or > 128)
        {
            return false;
        }

        headers = new SignedHeaders(
            tenantId,
            connectorId,
            DateTimeOffset.FromUnixTimeSeconds(timestampSeconds),
            nonce,
            bodyHash.ToLowerInvariant(),
            signature);
        return true;
    }

    private void PruneNonces(DateTimeOffset now)
    {
        foreach (var nonce in seenNonces)
        {
            if (nonce.Value < now - options.MaximumClockSkew)
            {
                seenNonces.TryRemove(nonce.Key, out _);
            }
        }
    }

    private static bool FixedEquals(string first, string second)
    {
        var firstBytes = Encoding.ASCII.GetBytes(first);
        var secondBytes = Encoding.ASCII.GetBytes(second);
        return firstBytes.Length == secondBytes.Length &&
            CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes);
    }

    private static Task RejectAsync(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return context.Response.WriteAsJsonAsync(
            new
            {
                type = "https://pharma-auto.invalid/problems/connector-authentication",
                title = "Connector authentication failed",
                status = StatusCodes.Status401Unauthorized,
                detail
            },
            context.RequestAborted);
    }

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private readonly record struct SignedHeaders(
        Guid TenantId,
        Guid ConnectorId,
        DateTimeOffset Timestamp,
        string Nonce,
        string BodySha256,
        string Signature);
}

public static class AuthenticatedConnectorExtensions
{
    public static AuthenticatedConnector Connector(this HttpContext context) =>
        context.Items.TryGetValue(typeof(AuthenticatedConnector), out var value) &&
        value is AuthenticatedConnector connector
            ? connector
            : throw new InvalidOperationException("No authenticated Connector is available.");
}
