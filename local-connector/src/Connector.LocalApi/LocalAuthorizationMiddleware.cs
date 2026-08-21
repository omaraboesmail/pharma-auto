using System.Security.Cryptography;
using System.Text;
using PharmaAuto.Connector.Application;

namespace PharmaAuto.Connector.LocalApi;

public sealed record ControlAuthorizationOptions(string ControlKey);

public sealed record AuthenticatedDevice(Guid DeviceId, DateTimeOffset ExpiresAt);

public sealed class LocalAuthorizationMiddleware(
    RequestDelegate next,
    ControlAuthorizationOptions controlOptions)
{
    private static readonly PathString[] PublicPaths =
    [
        new("/health/live"),
        new("/api/v1/pairing/claim"),
        new("/api/v1/auth/challenges"),
        new("/api/v1/auth/tokens")
    ];

    public async Task InvokeAsync(
        HttpContext context,
        DeviceAuthenticationService authenticationService)
    {
        if (PublicPaths.Any(path => context.Request.Path.Equals(path)))
        {
            await next(context);
            return;
        }

        if (context.Request.Path.StartsWithSegments("/control/v1"))
        {
            if (!IsLoopback(context.Connection.RemoteIpAddress) ||
                !FixedEquals(
                    context.Request.Headers["X-Control-Key"].ToString(),
                    controlOptions.ControlKey))
            {
                await RejectAsync(context, StatusCodes.Status403Forbidden, "Control access denied.");
                return;
            }
            await next(context);
            return;
        }

        if (context.Request.Path.StartsWithSegments("/api/v1"))
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal) ||
                await authenticationService.ValidateTokenAsync(
                    authorization[7..].Trim(),
                    context.RequestAborted) is not { } principal)
            {
                await RejectAsync(context, StatusCodes.Status401Unauthorized, "Device access token is invalid.");
                return;
            }
            context.Items[typeof(AuthenticatedDevice)] = new AuthenticatedDevice(
                principal.DeviceId,
                principal.ExpiresAt);
        }
        await next(context);
    }

    private static bool IsLoopback(System.Net.IPAddress? address) =>
        address is not null && System.Net.IPAddress.IsLoopback(address);

    private static bool FixedEquals(string first, string second)
    {
        var firstBytes = Encoding.UTF8.GetBytes(first);
        var secondBytes = Encoding.UTF8.GetBytes(second);
        return firstBytes.Length == secondBytes.Length &&
            CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes);
    }

    private static Task RejectAsync(HttpContext context, int status, string detail)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(
            new
            {
                type = "https://pharma-auto.invalid/problems/local-authorization",
                title = "Local Connector authorization failed",
                status,
                detail
            },
            context.RequestAborted);
    }
}

public static class AuthenticatedDeviceExtensions
{
    public static AuthenticatedDevice Device(this HttpContext context) =>
        context.Items.TryGetValue(typeof(AuthenticatedDevice), out var value) &&
        value is AuthenticatedDevice device
            ? device
            : throw new InvalidOperationException("No authenticated Android device is available.");
}
