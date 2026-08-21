using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using PharmaAuto.Connector.Application;

namespace PharmaAuto.Connector.Infrastructure;

public sealed class WindowsFileSafetyInspector(
    string? defenderExecutablePath,
    string scanDirectory,
    bool requireDefender) : IFileSafetyInspector
{
    private const int MaximumPixels = 50_000_000;
    private static readonly string[] ProhibitedPdfMarkers =
    [
        "/Encrypt",
        "/EmbeddedFiles",
        "/JavaScript",
        "/JS",
        "/Launch",
        "/OpenAction",
        "/RichMedia"
    ];

    public async Task<FileInspection> InspectAsync(
        ReadOnlyMemory<byte> content,
        string claimedMimeType,
        CancellationToken cancellationToken)
    {
        if (content.Length is < 16 or > 20 * 1024 * 1024)
        {
            throw new InvalidOperationException("Page size is outside the declared 16 B..20 MiB limit.");
        }

        var (mimeType, width, height) = InspectMagic(content.Span);
        if (!string.Equals(mimeType, claimedMimeType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Claimed MIME type {claimedMimeType} does not match {mimeType} content.");
        }
        if (width is > 0 && height is > 0 && (long)width * height > MaximumPixels)
        {
            throw new InvalidOperationException("Image dimensions exceed the 50 megapixel limit.");
        }

        await ScanWithDefenderAsync(content, mimeType, cancellationToken);
        return new FileInspection(mimeType, content.Length, width, height, []);
    }

    private async Task ScanWithDefenderAsync(
        ReadOnlyMemory<byte> content,
        string mimeType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(defenderExecutablePath) ||
            !File.Exists(defenderExecutablePath))
        {
            if (requireDefender)
            {
                throw new InvalidOperationException("Microsoft Defender scanner is required but unavailable.");
            }
            return;
        }

        Directory.CreateDirectory(scanDirectory);
        var extension = mimeType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "application/pdf" => ".pdf",
            _ => ".bin"
        };
        var path = Path.Combine(scanDirectory, $"scan-{Guid.NewGuid():N}{extension}");
        try
        {
            await File.WriteAllBytesAsync(path, content.ToArray(), cancellationToken);
            var startInfo = new ProcessStartInfo
            {
                FileName = defenderExecutablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-Scan");
            startInfo.ArgumentList.Add("-ScanType");
            startInfo.ArgumentList.Add("3");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(path);
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Microsoft Defender scan process did not start.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            await process.WaitForExitAsync(timeout.Token);
            _ = await outputTask;
            _ = await errorTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Microsoft Defender rejected or could not scan the document (exit {process.ExitCode}).");
            }
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static (string MimeType, int? Width, int? Height) InspectMagic(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 24 &&
            bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            if (!bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
            {
                throw new InvalidOperationException("PNG does not contain a valid IHDR header.");
            }
            return (
                "image/png",
                checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4))),
                checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4))));
        }
        if (bytes[0] == 0xff && bytes[1] == 0xd8)
        {
            var dimensions = ReadJpegDimensions(bytes);
            if (bytes[^2] != 0xff || bytes[^1] != 0xd9)
            {
                throw new InvalidOperationException("JPEG end marker is missing.");
            }
            return ("image/jpeg", dimensions.Width, dimensions.Height);
        }
        if (bytes.Length >= 8 && bytes[..5].SequenceEqual("%PDF-"u8))
        {
            var text = Encoding.Latin1.GetString(bytes);
            if (!text.Contains("%%EOF", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("PDF end marker is missing.");
            }
            var marker = ProhibitedPdfMarkers.FirstOrDefault(candidate =>
                text.Contains(candidate, StringComparison.OrdinalIgnoreCase));
            if (marker is not null)
            {
                throw new InvalidOperationException(
                    $"PDF contains prohibited active or embedded content marker {marker}.");
            }
            return ("application/pdf", null, null);
        }
        throw new InvalidOperationException("File magic is not JPEG, PNG, or PDF.");
    }

    private static (int Width, int Height) ReadJpegDimensions(ReadOnlySpan<byte> bytes)
    {
        var index = 2;
        while (index + 8 < bytes.Length)
        {
            if (bytes[index++] != 0xff)
            {
                continue;
            }
            var marker = bytes[index++];
            if (marker is 0xd8 or 0xd9 || marker is >= 0xd0 and <= 0xd7)
            {
                continue;
            }
            if (index + 2 > bytes.Length)
            {
                break;
            }
            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index, 2));
            if (segmentLength < 2 || index + segmentLength > bytes.Length)
            {
                throw new InvalidOperationException("JPEG segment length is invalid.");
            }
            if (marker is 0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or
                0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf)
            {
                if (segmentLength < 7)
                {
                    throw new InvalidOperationException("JPEG frame header is invalid.");
                }
                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index + 5, 2));
                return (width, height);
            }
            index += segmentLength;
        }
        throw new InvalidOperationException("JPEG dimensions could not be read.");
    }
}
