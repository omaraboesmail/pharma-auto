using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PharmaAuto.Connector.Infrastructure;

public sealed class ConnectorCertificateProvider
{
    public X509Certificate2 LoadByThumbprint(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var normalized = thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        var certificate = store.Certificates
            .Find(X509FindType.FindByThumbprint, normalized, validOnly: true)
            .OfType<X509Certificate2>()
            .SingleOrDefault(certificate => certificate.HasPrivateKey);
        return certificate
            ?? throw new InvalidOperationException(
                "Configured Connector TLS certificate was not found with a private key.");
    }

    public X509Certificate2 LoadOrCreateDevelopment(string pfxPath, string password)
    {
        if (File.Exists(pfxPath))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(
                pfxPath,
                password,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pfxPath))!);
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN=Pharma Auto Connector {Environment.MachineName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1") },
                true));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(Environment.MachineName);
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        foreach (var address in LocalIpv4Addresses())
        {
            san.AddIpAddress(address);
        }
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        using var generated = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(2));
        var bytes = generated.Export(X509ContentType.Pfx, password);
        var temporary = pfxPath + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, pfxPath);
        return X509CertificateLoader.LoadPkcs12(
            bytes,
            password,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
    }

    public static string Sha256(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData));

    private static IEnumerable<IPAddress> LocalIpv4Addresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(address))
            .Distinct();
    }
}
