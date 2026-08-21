using System.Security.Cryptography;
using PharmaAuto.Saas.Application;

namespace PharmaAuto.Saas.Infrastructure;

public sealed class EcdsaEntitlementSigner : IEntitlementSigner, IDisposable
{
    private readonly ECDsa key;

    public EcdsaEntitlementSigner(string keyId, string? privateKeyPem)
    {
        if (string.IsNullOrWhiteSpace(keyId))
        {
            throw new ArgumentException("A signing key id is required.", nameof(keyId));
        }

        KeyId = keyId;
        key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        if (!string.IsNullOrWhiteSpace(privateKeyPem))
        {
            key.ImportFromPem(privateKeyPem);
        }
    }

    public string Algorithm => "ES256";

    public string KeyId { get; }

    public string Sign(ReadOnlySpan<byte> payload)
    {
        var signature = key.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Base64Url(signature);
    }

    public string ExportPublicKeyPem() => key.ExportSubjectPublicKeyInfoPem();

    public void Dispose() => key.Dispose();

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
