using System.Security.Cryptography;

namespace PharmaAuto.Connector.Infrastructure;

public sealed class WindowsMachineKeyStore(string rootPath)
{
    private readonly string rootPath = Path.GetFullPath(rootPath);

    public byte[] GetOrCreate(string name, int length)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Connector key storage requires Windows DPAPI.");
        }
        if (length is < 16 or > 128 ||
            name.Length is < 1 or > 64 ||
            name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException("Machine key name or size is invalid.");
        }

        Directory.CreateDirectory(rootPath);
        var path = Path.Combine(rootPath, $"{name}.dpapi");
        var entropy = System.Text.Encoding.UTF8.GetBytes($"PharmaAuto.Connector.{name}.v1");
        if (File.Exists(path))
        {
            return ProtectedData.Unprotect(
                File.ReadAllBytes(path),
                entropy,
                DataProtectionScope.LocalMachine);
        }

        var key = RandomNumberGenerator.GetBytes(length);
        var protectedKey = ProtectedData.Protect(key, entropy, DataProtectionScope.LocalMachine);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, protectedKey);
        File.Move(temporary, path);
        return key;
    }

    public string GetBase64Url(string name, int length)
    {
        var key = GetOrCreate(name, length);
        return Convert.ToBase64String(key)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
