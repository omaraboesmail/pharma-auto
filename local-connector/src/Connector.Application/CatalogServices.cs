using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PharmaAuto.Connector.Domain;

namespace PharmaAuto.Connector.Application;

public sealed class CatalogProjectionService(
    IGeniusCatalogReader reader,
    ISidecarStore store,
    ConnectorIdentity connector,
    byte[] localIdentityKey,
    TimeProvider timeProvider)
{
    public async Task<CatalogProjectionSummary> RebuildAsync(
        CancellationToken cancellationToken)
    {
        var barcodes = new Dictionary<decimal, List<string>>();
        await foreach (var barcode in reader.ReadBarcodesAsync(cancellationToken))
        {
            Add(barcodes, barcode.ItemId, barcode.Barcode);
        }

        var vendorCodes = new Dictionary<decimal, List<string>>();
        await foreach (var vendorCode in reader.ReadVendorCodesAsync(cancellationToken))
        {
            Add(vendorCodes, vendorCode.ItemId, vendorCode.VendorItemCode);
        }

        var projectedAt = timeProvider.GetUtcNow();
        var metrics = new ProjectionMetrics();
        var items = ProjectItemsAsync(
            barcodes,
            vendorCodes,
            projectedAt,
            metrics,
            cancellationToken);
        var vendors = ProjectVendorsAsync(projectedAt, metrics, cancellationToken);
        await store.ReplaceCatalogAsync(items, vendors, cancellationToken);

        var summary = new CatalogProjectionSummary(
            metrics.ItemCount,
            metrics.VendorCount,
            barcodes.Sum(entry => entry.Value.Count),
            vendorCodes.Sum(entry => entry.Value.Count),
            metrics.UntrustedLabelCount,
            metrics.IdenticalLanguageFieldCount,
            timeProvider.GetUtcNow(),
            false);
        await store.SaveCatalogProjectionSummaryAsync(summary, cancellationToken);
        await store.AppendAuditAsync(
            new AuditRecord(
                Guid.NewGuid(),
                "CONNECTOR",
                connector.ConnectorId.ToString("D"),
                "CATALOG_PROJECTION_REBUILT",
                connector.DatabaseProfileId,
                "SUCCESS_READ_ONLY",
                Guid.NewGuid(),
                summary.CompletedAt),
            cancellationToken);
        return summary;
    }

    private async IAsyncEnumerable<LocalCatalogItem> ProjectItemsAsync(
        IReadOnlyDictionary<decimal, List<string>> barcodes,
        IReadOnlyDictionary<decimal, List<string>> vendorCodes,
        DateTimeOffset projectedAt,
        ProjectionMetrics metrics,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var row in reader.ReadItemsAsync(cancellationToken))
        {
            var decoded = GeniusRawNameDecoder.Decode(
                row.ArabicNameBytes,
                row.EnglishNameBytes);
            metrics.ItemCount++;
            if (decoded.QualityFlags.Contains(CatalogQualityFlag.Unverified))
            {
                metrics.UntrustedLabelCount++;
            }
            if (decoded.QualityFlags.Contains(CatalogQualityFlag.LanguageFieldsIdentical))
            {
                metrics.IdenticalLanguageFieldCount++;
            }

            yield return new LocalCatalogItem(
                EncodeIdentity("item", row.ItemId),
                row.ItemId,
                decoded.ArabicLabel,
                decoded.EnglishLabel,
                decoded.DisplayLabel,
                decoded.ArabicHash,
                decoded.EnglishHash,
                decoded.Direction,
                decoded.QualityFlags,
                new CatalogIdentifiers(
                    NormalizeIdentifier(row.ItemCode),
                    NormalizeIdentifier(row.SecondaryCode),
                    NormalizeIdentifier(row.InternationalCode),
                    barcodes.GetValueOrDefault(row.ItemId) ?? [],
                    vendorCodes.GetValueOrDefault(row.ItemId) ?? []),
                NullIfBlank(row.ActiveIngredient),
                NullIfBlank(row.Strength),
                null,
                null,
                row.HasExpiry,
                row.Active,
                projectedAt);
        }
    }

    private async IAsyncEnumerable<LocalVendor> ProjectVendorsAsync(
        DateTimeOffset projectedAt,
        ProjectionMetrics metrics,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var row in reader.ReadVendorsAsync(cancellationToken))
        {
            metrics.VendorCount++;
            var displayName = NullIfBlank(row.ArabicName)
                ?? NullIfBlank(row.EnglishName)
                ?? NormalizeIdentifier(row.Code)
                ?? $"Vendor {row.VendorId.ToString(CultureInfo.InvariantCulture)}";
            yield return new LocalVendor(
                EncodeIdentity("vendor", row.VendorId),
                row.VendorId,
                NormalizeIdentifier(row.Code),
                displayName,
                row.Active,
                projectedAt);
        }
    }

    private string EncodeIdentity(string kind, decimal identity)
    {
        var message = Encoding.UTF8.GetBytes(
            string.Join(
                '|',
                connector.DatabaseProfileId,
                kind,
                identity.ToString(CultureInfo.InvariantCulture)));
        var hash = HMACSHA256.HashData(localIdentityKey, message);
        return $"{kind}_{Base64Url(hash.AsSpan(0, 18))}";
    }

    private static void Add(Dictionary<decimal, List<string>> target, decimal itemId, string value)
    {
        var normalized = NormalizeIdentifier(value);
        if (normalized is null)
        {
            return;
        }
        if (!target.TryGetValue(itemId, out var values))
        {
            values = [];
            target.Add(itemId, values);
        }
        if (!values.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(normalized);
        }
    }

    private static string? NormalizeIdentifier(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class ProjectionMetrics
    {
        public int ItemCount { get; set; }

        public int VendorCount { get; set; }

        public int UntrustedLabelCount { get; set; }

        public int IdenticalLanguageFieldCount { get; set; }
    }
}

public sealed record DecodedGeniusNames(
    string? ArabicLabel,
    string? EnglishLabel,
    string? DisplayLabel,
    string? ArabicHash,
    string? EnglishHash,
    CatalogDisplayDirection Direction,
    IReadOnlyList<CatalogQualityFlag> QualityFlags);

public static class GeniusRawNameDecoder
{
    private static readonly Encoding ArabicEncoding;

    static GeniusRawNameDecoder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ArabicEncoding = Encoding.GetEncoding(
            1256,
            new EncoderReplacementFallback("?"),
            new DecoderReplacementFallback("�"));
    }

    public static DecodedGeniusNames Decode(byte[]? arabicBytes, byte[]? englishBytes)
    {
        var arabic = DecodeSingle(arabicBytes);
        var english = DecodeSingle(englishBytes);
        var arabicDisplay = DisplayCandidate(arabic);
        var englishDisplay = DisplayCandidate(english);
        var flags = new HashSet<CatalogQualityFlag> { CatalogQualityFlag.Unverified };
        if (arabicBytes is { Length: > 0 } &&
            englishBytes is { Length: > 0 } &&
            arabicBytes.AsSpan().SequenceEqual(englishBytes))
        {
            flags.Add(CatalogQualityFlag.LanguageFieldsIdentical);
        }
        if (string.IsNullOrWhiteSpace(arabic) && string.IsNullOrWhiteSpace(english))
        {
            flags.Add(CatalogQualityFlag.EmptyOrBlank);
        }

        var display = !string.IsNullOrWhiteSpace(arabicDisplay) ? arabicDisplay : englishDisplay;
        if (ContainsUnsafeBidiControl(arabic) || ContainsUnsafeBidiControl(english))
        {
            flags.Add(CatalogQualityFlag.MalformedBidi);
        }
        if (ContainsReplacementOrControl(arabic) || ContainsReplacementOrControl(english))
        {
            flags.Add(CatalogQualityFlag.TruncatedOrCorrupt);
        }

        return new DecodedGeniusNames(
            arabic,
            english,
            display,
            Hash(arabicBytes),
            Hash(englishBytes),
            Direction(display),
            flags.OrderBy(flag => flag).ToArray());
    }

    private static string? DecodeSingle(byte[]? storedBytes)
    {
        if (storedBytes is not { Length: > 0 })
        {
            return null;
        }
        var reversed = storedBytes.ToArray();
        Array.Reverse(reversed);
        return ArabicEncoding.GetString(reversed);
    }

    private static string? DisplayCandidate(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : raw.TrimEnd('\0').Trim();

    private static string? Hash(byte[]? bytes) => bytes is { Length: > 0 }
        ? Convert.ToHexStringLower(SHA256.HashData(bytes))
        : null;

    private static CatalogDisplayDirection Direction(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return CatalogDisplayDirection.Auto;
        }
        return value.Any(character => character is >= '\u0600' and <= '\u08ff')
            ? CatalogDisplayDirection.Rtl
            : CatalogDisplayDirection.Ltr;
    }

    private static bool ContainsUnsafeBidiControl(string? value) =>
        value?.Any(character => character is '\u202A' or '\u202B' or '\u202D' or
            '\u202E' or '\u202C' or '\u2066' or '\u2067' or '\u2068' or '\u2069') == true;

    private static bool ContainsReplacementOrControl(string? value) =>
        value?.Any(character => character == '�' || (char.IsControl(character) &&
            character is not ('\t' or '\r' or '\n'))) == true;
}

public sealed partial class CatalogSearchService(ISidecarStore store)
{
    public async Task<IReadOnlyList<LocalItemCandidate>> SearchItemsAsync(
        LocalMatchQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Limit is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Candidate limit must be 1..50.");
        }
        var hits = await store.SearchItemsAsync(query, cancellationToken);
        return hits
            .Select(hit => MapCandidate(hit, query))
            .OrderBy(candidate => candidate.HardMismatches.Count)
            .ThenByDescending(candidate => ReasonRank(candidate.ReasonCodes))
            .ThenBy(candidate => candidate.DisplayLabel, StringComparer.CurrentCultureIgnoreCase)
            .Take(query.Limit)
            .ToArray();
    }

    public async Task<IReadOnlyList<LocalVendorCandidate>> SearchVendorsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var vendors = await store.SearchVendorsAsync(query, limit, cancellationToken);
        return vendors.Select(vendor => new LocalVendorCandidate(
            vendor.LocalVendorReference,
            vendor.DisplayName,
            vendor.Code,
            string.Equals(vendor.Code, query, StringComparison.OrdinalIgnoreCase)
                ? ["EXACT_IDENTIFIER"]
                : ["LEXICAL_NAME"],
            true)).ToArray();
    }

    public async Task<LocalItemCandidate?> ResolveItemAsync(
        string localItemReference,
        LocalMatchQuery query,
        CancellationToken cancellationToken)
    {
        var item = await store.GetCatalogItemAsync(localItemReference, cancellationToken);
        return item is null
            ? null
            : MapCandidate(
                new CatalogSearchHit(item, ["MANUAL_CATALOG_SEARCH"]),
                query);
    }

    public async Task<LocalVendorCandidate?> ResolveVendorAsync(
        string localVendorReference,
        CancellationToken cancellationToken)
    {
        var vendor = await store.GetCatalogVendorAsync(
            localVendorReference,
            cancellationToken);
        return vendor is null
            ? null
            : new LocalVendorCandidate(
                vendor.LocalVendorReference,
                vendor.DisplayName,
                vendor.Code,
                ["MANUAL_CATALOG_SEARCH"],
                true);
    }

    private static LocalItemCandidate MapCandidate(CatalogSearchHit hit, LocalMatchQuery query)
    {
        var item = hit.Item;
        var mismatches = new List<string>();
        Compare(query.ActiveIngredient, item.ActiveIngredient, "ACTIVE_INGREDIENT", mismatches);
        Compare(query.Strength, item.Strength, "STRENGTH", mismatches);
        Compare(query.DosageForm, item.DosageForm, "DOSAGE_FORM", mismatches);
        Compare(query.Pack, item.Pack, "PACK", mismatches);
        var rawLabel = item.RawArabicLabel ?? item.RawEnglishLabel;
        var rawHash = item.RawArabicHash ?? item.RawEnglishHash;
        return new LocalItemCandidate(
            "1.0",
            item.LocalItemReference,
            item.DisplayLabel ?? item.Identifiers.ItemCode ?? "Unlabelled local Item",
            rawLabel,
            rawHash,
            "GENIUS_RAW",
            item.DisplayDirection,
            item.QualityFlags,
            item.Identifiers,
            new CatalogAttributes(
                item.ActiveIngredient,
                item.Strength,
                item.DosageForm,
                item.Pack),
            hit.ReasonCodes,
            mismatches,
            true);
    }

    private static void Compare(
        string? requested,
        string? actual,
        string mismatch,
        List<string> mismatches)
    {
        if (!string.IsNullOrWhiteSpace(requested) &&
            !string.IsNullOrWhiteSpace(actual) &&
            !string.Equals(Normalize(requested), Normalize(actual), StringComparison.Ordinal))
        {
            mismatches.Add(mismatch);
        }
    }

    private static string Normalize(string value) =>
        Whitespace().Replace(value.Normalize(NormalizationForm.FormKC).Trim(), " ")
            .ToUpperInvariant();

    private static int ReasonRank(IReadOnlyList<string> reasons)
    {
        if (reasons.Contains("EXACT_IDENTIFIER", StringComparer.Ordinal)) return 5;
        if (reasons.Contains("VENDOR_ITEM_CODE", StringComparer.Ordinal)) return 4;
        if (reasons.Contains("PREVIOUS_CONFIRMED_MAPPING", StringComparer.Ordinal)) return 3;
        if (reasons.Contains("EXACT_NORMALIZED_NAME", StringComparer.Ordinal)) return 2;
        return 1;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
