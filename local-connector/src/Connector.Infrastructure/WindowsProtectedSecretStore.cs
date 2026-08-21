using System.Security.Cryptography;
using System.Text;

namespace PharmaAuto.Connector.Infrastructure;

public sealed class WindowsProtectedSecretStore(string rootPath)
{
    private readonly string rootPath = Path.GetFullPath(rootPath);

    public string? TryRead(string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Connector secret storage requires Windows DPAPI.");
        }
        ValidateName(name);
        var path = Path.Combine(rootPath, $"{name}.dpapi");
        if (!File.Exists(path))
        {
            return null;
        }
        var plaintext = ProtectedData.Unprotect(
            File.ReadAllBytes(path),
            Entropy(name),
            DataProtectionScope.LocalMachine);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static void ValidateName(string name)
    {
        if (name.Length is < 1 or > 64 ||
            name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException("Protected secret name is invalid.", nameof(name));
        }
    }

    private static byte[] Entropy(string name) =>
        Encoding.UTF8.GetBytes($"PharmaAuto.Connector.Secret.{name}.v1");
}
