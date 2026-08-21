using System.Security.Cryptography;
using System.Text;
using PharmaAuto.Connector.Application;

namespace PharmaAuto.Connector.Infrastructure;

public sealed class EncryptedDocumentObjectStore : IDocumentObjectStore
{
    private static readonly byte[] Magic = "PAO1"u8.ToArray();
    private readonly string rootPath;
    private readonly byte[] masterKey;

    public EncryptedDocumentObjectStore(string rootPath, string keyPath)
    {
        this.rootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(this.rootPath);
        masterKey = LoadOrCreateMasterKey(Path.GetFullPath(keyPath));
    }

    public async Task<string> WriteAsync(
        string category,
        Guid jobId,
        string objectName,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken)
    {
        if (category is not ("chunks" or "pages") ||
            objectName.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException("Document object category or name is invalid.");
        }

        var relative = Path.Combine(
            category,
            jobId.ToString("D"),
            $"{objectName}-{Guid.NewGuid():N}.pao");
        var finalPath = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        var temporaryPath = finalPath + ".tmp";

        var dataKey = RandomNumberGenerator.GetBytes(32);
        var wrapNonce = RandomNumberGenerator.GetBytes(12);
        var wrapTag = new byte[16];
        var wrappedKey = new byte[dataKey.Length];
        var dataNonce = RandomNumberGenerator.GetBytes(12);
        var dataTag = new byte[16];
        var ciphertext = new byte[plaintext.Length];
        var aad = Encoding.UTF8.GetBytes(relative.Replace('\\', '/'));
        try
        {
            using (var wrapper = new AesGcm(masterKey, 16))
            {
                wrapper.Encrypt(wrapNonce, dataKey, wrappedKey, wrapTag, aad);
            }
            using (var cipher = new AesGcm(dataKey, 16))
            {
                cipher.Encrypt(dataNonce, plaintext.Span, ciphertext, dataTag, aad);
            }

            await using var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(Magic, cancellationToken);
            await stream.WriteAsync(wrapNonce, cancellationToken);
            await stream.WriteAsync(wrapTag, cancellationToken);
            await stream.WriteAsync(wrappedKey, cancellationToken);
            await stream.WriteAsync(dataNonce, cancellationToken);
            await stream.WriteAsync(dataTag, cancellationToken);
            await stream.WriteAsync(ciphertext, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Close();
            File.Move(temporaryPath, finalPath);
            return relative.Replace('\\', '/');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<byte[]> ReadAsync(
        string objectReference,
        CancellationToken cancellationToken)
    {
        var path = Resolve(objectReference);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        const int headerLength = 4 + 12 + 16 + 32 + 12 + 16;
        if (bytes.Length < headerLength || !bytes.AsSpan(0, 4).SequenceEqual(Magic))
        {
            throw new CryptographicException("Encrypted document object header is invalid.");
        }

        var offset = 4;
        var wrapNonce = bytes.AsSpan(offset, 12);
        offset += 12;
        var wrapTag = bytes.AsSpan(offset, 16);
        offset += 16;
        var wrappedKey = bytes.AsSpan(offset, 32);
        offset += 32;
        var dataNonce = bytes.AsSpan(offset, 12);
        offset += 12;
        var dataTag = bytes.AsSpan(offset, 16);
        offset += 16;
        var ciphertext = bytes.AsSpan(offset);
        var dataKey = new byte[32];
        var plaintext = new byte[ciphertext.Length];
        var aad = Encoding.UTF8.GetBytes(objectReference.Replace('\\', '/'));
        try
        {
            using (var wrapper = new AesGcm(masterKey, 16))
            {
                wrapper.Decrypt(wrapNonce, wrappedKey, wrapTag, dataKey, aad);
            }
            using (var cipher = new AesGcm(dataKey, 16))
            {
                cipher.Decrypt(dataNonce, ciphertext, dataTag, plaintext, aad);
            }
            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public Task DeleteAsync(string objectReference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(objectReference);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    public Task<int> DeleteExpiredAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken)
    {
        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(rootPath, "*.pao", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetLastWriteTimeUtc(path) < olderThan.UtcDateTime)
            {
                File.Delete(path);
                deleted++;
            }
        }
        return Task.FromResult(deleted);
    }

    private string Resolve(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Document object reference must be relative.");
        }
        var resolved = Path.GetFullPath(
            Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Document object reference escapes the storage root.");
        }
        return resolved;
    }

    private static byte[] LoadOrCreateMasterKey(string keyPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Connector document protection requires Windows DPAPI.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        if (File.Exists(keyPath))
        {
            var protectedKey = File.ReadAllBytes(keyPath);
            return ProtectedData.Unprotect(
                protectedKey,
                "PharmaAuto.Connector.DocumentKey.v1"u8.ToArray(),
                DataProtectionScope.LocalMachine);
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var protectedBytes = ProtectedData.Protect(
            key,
            "PharmaAuto.Connector.DocumentKey.v1"u8.ToArray(),
            DataProtectionScope.LocalMachine);
        var temporary = keyPath + ".tmp";
        File.WriteAllBytes(temporary, protectedBytes);
        File.Move(temporary, keyPath);
        return key;
    }
}
