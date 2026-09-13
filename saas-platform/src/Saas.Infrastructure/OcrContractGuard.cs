using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PharmaAuto.Saas.Domain;

namespace PharmaAuto.Saas.Infrastructure;

public static class OcrContractGuard
{
    private static readonly Regex Sha256Pattern = Pattern("^[a-f0-9]{64}$");
    private static readonly Regex DecimalPattern =
        Pattern("^(0|[1-9][0-9]{0,11})([.][0-9]{1,6})?$");
    private static readonly Regex PercentagePattern = Pattern(
        "^(100([.]0{1,4})?|([0-9]|[1-9][0-9])([.][0-9]{1,4})?)$");
    private static readonly Regex DateTimePattern = Pattern(
        "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}([.][0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})$");

    private static readonly HashSet<string> RootKeys =
    [
        "schemaVersion", "resultId", "jobId", "provider", "document", "supplier",
        "invoiceNumber", "invoiceDate", "currency", "sourceLines", "totals",
        "qualityFlags"
    ];
    private static readonly HashSet<string> ProviderKeys =
        ["name", "model", "processedAt"];
    private static readonly HashSet<string> DocumentKeys =
        ["sourceSha256", "pageCount", "mimeTypes"];
    private static readonly HashSet<string> EvidenceKeys =
        ["rawValue", "normalizedValue", "page", "boundingBox", "evidenceText", "warnings"];
    private static readonly HashSet<string> BoundingBoxKeys =
        ["x", "y", "width", "height"];
    private static readonly HashSet<string> SourceLineKeys =
    [
        "sourceLineId", "sequence", "description", "vendorItemCode", "quantity",
        "unit", "purchaseUnitPrice", "discount1Percentage", "discount2Percentage",
        "sellingUnitPrice", "expiryDate", "batch"
    ];
    private static readonly HashSet<string> TotalsKeys =
        ["subtotal", "discount", "tax", "total"];
    private static readonly HashSet<string> AllowedQualityFlags =
    [
        "LOW_IMAGE_QUALITY", "ROTATED_PAGE", "MISSING_INVOICE_NUMBER",
        "MISSING_INVOICE_DATE", "MIXED_LANGUAGE", "TABLE_STRUCTURE_UNCERTAIN",
        "TOTALS_MISMATCH", "MANUAL_REVIEW_REQUIRED"
    ];
    private static readonly HashSet<string> AllowedMimeTypes =
        ["image/jpeg", "image/png"];
    private static readonly HashSet<string> ForbiddenKeys =
    [
        "itm_id", "itmId", "ven_id", "venId", "c_id", "cId", "pth_id",
        "pthId", "sql", "sqlText"
    ];

    public static void Validate(string json, OcrDocument document)
    {
        try
        {
            using var parsed = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64
                });
            ValidateRoot(parsed.RootElement, document);
        }
        catch (OcrProviderException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            JsonException or
            InvalidOperationException or
            FormatException or
            OverflowException)
        {
            throw Invalid("OCR result is not a structurally valid v1 document.", exception);
        }
    }

    private static void ValidateRoot(JsonElement root, OcrDocument document)
    {
        RequireExactObject(root, RootKeys, "$");
        RequireString(root.GetProperty("schemaVersion"), "$.schemaVersion", 3, 3, "1.0");
        RequireGuid(root.GetProperty("resultId"), "$.resultId");
        if (RequireGuid(root.GetProperty("jobId"), "$.jobId") != document.JobId)
        {
            throw Invalid("OCR result job identity does not match the request.");
        }

        ValidateProvider(root.GetProperty("provider"));
        ValidateDocument(root.GetProperty("document"), document);
        var pageCount = document.Pages.Count;
        ValidateEvidence(root.GetProperty("supplier"), "$.supplier", pageCount, ValueKind.Text);
        ValidateEvidence(
            root.GetProperty("invoiceNumber"),
            "$.invoiceNumber",
            pageCount,
            ValueKind.Text);
        ValidateEvidence(
            root.GetProperty("invoiceDate"),
            "$.invoiceDate",
            pageCount,
            ValueKind.Date);
        ValidateEvidence(
            root.GetProperty("currency"),
            "$.currency",
            pageCount,
            ValueKind.Currency);
        ValidateSourceLines(root.GetProperty("sourceLines"), pageCount);
        ValidateTotals(root.GetProperty("totals"), pageCount);
        ValidateQualityFlags(root.GetProperty("qualityFlags"));
        FindForbiddenKeys(root, "$");
    }

    private static void ValidateProvider(JsonElement provider)
    {
        RequireExactObject(provider, ProviderKeys, "$.provider");
        RequireString(provider.GetProperty("name"), "$.provider.name", 6, 6, "GEMINI");
        RequireString(provider.GetProperty("model"), "$.provider.model", 1, 128);
        var processedAt = RequireString(
            provider.GetProperty("processedAt"),
            "$.provider.processedAt",
            20,
            40);
        if (!DateTimePattern.IsMatch(processedAt) ||
            !DateTimeOffset.TryParse(
                processedAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _))
        {
            throw Invalid("$.provider.processedAt must be an RFC 3339 timestamp.");
        }
    }

    private static void ValidateDocument(JsonElement resultDocument, OcrDocument document)
    {
        RequireExactObject(resultDocument, DocumentKeys, "$.document");
        var sourceSha256 = RequireString(
            resultDocument.GetProperty("sourceSha256"),
            "$.document.sourceSha256",
            64,
            64);
        if (!Sha256Pattern.IsMatch(sourceSha256) ||
            !string.Equals(sourceSha256, document.SourceSha256, StringComparison.Ordinal))
        {
            throw Invalid("OCR result source hash does not match the submitted document.");
        }

        var pageCount = RequireInteger(
            resultDocument.GetProperty("pageCount"),
            "$.document.pageCount");
        if (pageCount != document.Pages.Count || pageCount is < 1 or > 100)
        {
            throw Invalid("OCR result page count does not match the submitted document.");
        }

        var mimeTypes = resultDocument.GetProperty("mimeTypes");
        if (mimeTypes.ValueKind != JsonValueKind.Array || mimeTypes.GetArrayLength() < 1)
        {
            throw Invalid("$.document.mimeTypes must be a non-empty array.");
        }
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in mimeTypes.EnumerateArray())
        {
            var mimeType = RequireString(value, "$.document.mimeTypes[]", 9, 10);
            if (!AllowedMimeTypes.Contains(mimeType) || !actual.Add(mimeType))
            {
                throw Invalid(
                    "$.document.mimeTypes contains an unsupported or duplicate value.");
            }
        }
        var expected = document.Pages
            .Select(page => page.MimeType)
            .ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected))
        {
            throw Invalid("OCR result MIME evidence does not match the submitted pages.");
        }
    }

    private static void ValidateSourceLines(JsonElement sourceLines, int pageCount)
    {
        if (sourceLines.ValueKind != JsonValueKind.Array ||
            sourceLines.GetArrayLength() is < 1 or > 1000)
        {
            throw Invalid("OCR result must contain 1..1000 source lines.");
        }

        var expectedSequence = 1;
        var sourceLineIds = new HashSet<Guid>();
        foreach (var line in sourceLines.EnumerateArray())
        {
            var path = $"$.sourceLines[{expectedSequence - 1}]";
            RequireExactObject(line, SourceLineKeys, path);
            var sourceLineId = RequireGuid(
                line.GetProperty("sourceLineId"),
                $"{path}.sourceLineId");
            if (!sourceLineIds.Add(sourceLineId))
            {
                throw Invalid("OCR source-line identities must be unique.");
            }
            if (RequireInteger(line.GetProperty("sequence"), $"{path}.sequence") !=
                expectedSequence++)
            {
                throw Invalid(
                    "OCR source-line sequence must be contiguous and start at 1.");
            }

            ValidateEvidence(
                line.GetProperty("description"),
                $"{path}.description",
                pageCount,
                ValueKind.Text);
            ValidateEvidence(
                line.GetProperty("vendorItemCode"),
                $"{path}.vendorItemCode",
                pageCount,
                ValueKind.Text);
            ValidateEvidence(
                line.GetProperty("quantity"),
                $"{path}.quantity",
                pageCount,
                ValueKind.Decimal);
            ValidateEvidence(
                line.GetProperty("unit"),
                $"{path}.unit",
                pageCount,
                ValueKind.Text);
            ValidateEvidence(
                line.GetProperty("purchaseUnitPrice"),
                $"{path}.purchaseUnitPrice",
                pageCount,
                ValueKind.Decimal);
            ValidateEvidence(
                line.GetProperty("discount1Percentage"),
                $"{path}.discount1Percentage",
                pageCount,
                ValueKind.Percentage);
            ValidateEvidence(
                line.GetProperty("discount2Percentage"),
                $"{path}.discount2Percentage",
                pageCount,
                ValueKind.Percentage);
            ValidateEvidence(
                line.GetProperty("sellingUnitPrice"),
                $"{path}.sellingUnitPrice",
                pageCount,
                ValueKind.Decimal);
            ValidateEvidence(
                line.GetProperty("expiryDate"),
                $"{path}.expiryDate",
                pageCount,
                ValueKind.Date);
            ValidateEvidence(
                line.GetProperty("batch"),
                $"{path}.batch",
                pageCount,
                ValueKind.Text);
        }
    }

    private static void ValidateTotals(JsonElement totals, int pageCount)
    {
        RequireExactObject(totals, TotalsKeys, "$.totals");
        foreach (var name in TotalsKeys)
        {
            ValidateEvidence(
                totals.GetProperty(name),
                $"$.totals.{name}",
                pageCount,
                ValueKind.Decimal);
        }
    }

    private static void ValidateQualityFlags(JsonElement flags)
    {
        if (flags.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("$.qualityFlags must be an array.");
        }
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var flag in flags.EnumerateArray())
        {
            var value = RequireString(flag, "$.qualityFlags[]", 1, 128);
            if (!AllowedQualityFlags.Contains(value) || !values.Add(value))
            {
                throw Invalid(
                    "$.qualityFlags contains an unsupported or duplicate value.");
            }
        }
    }

    private static void ValidateEvidence(
        JsonElement field,
        string path,
        int pageCount,
        ValueKind valueKind)
    {
        RequireExactObject(field, EvidenceKeys, path);
        RequireNullableString(
            field.GetProperty("rawValue"),
            $"{path}.rawValue",
            2000);
        RequireNullableString(
            field.GetProperty("evidenceText"),
            $"{path}.evidenceText",
            2000);

        var pageElement = field.GetProperty("page");
        int? page = pageElement.ValueKind == JsonValueKind.Null
            ? null
            : RequireInteger(pageElement, $"{path}.page");
        if (page is not null && (page < 1 || page > pageCount))
        {
            throw Invalid($"{path}.page falls outside the submitted document.");
        }

        var boundingBox = field.GetProperty("boundingBox");
        if (boundingBox.ValueKind != JsonValueKind.Null)
        {
            if (page is null)
            {
                throw Invalid($"{path}.boundingBox requires a source page.");
            }
            ValidateBoundingBox(boundingBox, $"{path}.boundingBox");
        }
        ValidateWarnings(field.GetProperty("warnings"), $"{path}.warnings");

        var normalized = field.GetProperty("normalizedValue");
        switch (valueKind)
        {
            case ValueKind.Text:
                RequireNullableString(normalized, $"{path}.normalizedValue", 1000);
                break;
            case ValueKind.Decimal:
                ValidateDecimal(normalized, $"{path}.normalizedValue", percentage: false);
                break;
            case ValueKind.Percentage:
                ValidateDecimal(normalized, $"{path}.normalizedValue", percentage: true);
                break;
            case ValueKind.Date:
                ValidateDate(normalized, $"{path}.normalizedValue");
                break;
            case ValueKind.Currency:
                if (normalized.ValueKind != JsonValueKind.Null)
                {
                    RequireString(
                        normalized,
                        $"{path}.normalizedValue",
                        3,
                        3,
                        "EGP");
                }
                break;
            default:
                throw new UnreachableException();
        }
    }

    private static void ValidateBoundingBox(JsonElement boundingBox, string path)
    {
        RequireExactObject(boundingBox, BoundingBoxKeys, path);
        var x = RequireNumber(boundingBox.GetProperty("x"), $"{path}.x");
        var y = RequireNumber(boundingBox.GetProperty("y"), $"{path}.y");
        var width = RequireNumber(
            boundingBox.GetProperty("width"),
            $"{path}.width");
        var height = RequireNumber(
            boundingBox.GetProperty("height"),
            $"{path}.height");
        if (x is < 0 or > 1 || y is < 0 or > 1 ||
            width is <= 0 or > 1 || height is <= 0 or > 1 ||
            x + width > 1.000000001 || y + height > 1.000000001)
        {
            throw Invalid($"{path} must fit inside normalized page bounds.");
        }
    }

    private static void ValidateWarnings(JsonElement warnings, string path)
    {
        if (warnings.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"{path} must be an array.");
        }
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var warning in warnings.EnumerateArray())
        {
            var value = RequireString(warning, $"{path}[]", 1, 128);
            if (!values.Add(value))
            {
                throw Invalid($"{path} must contain unique values.");
            }
        }
    }

    private static void ValidateDecimal(
        JsonElement value,
        string path,
        bool percentage)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return;
        }
        var text = RequireString(value, path, 1, percentage ? 8 : 19);
        if (!(percentage ? PercentagePattern : DecimalPattern).IsMatch(text) ||
            !decimal.TryParse(
                text,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed < 0m ||
            percentage && parsed > 100m)
        {
            throw Invalid(
                percentage
                    ? $"{path} must be an invariant percentage from 0 through 100 or null."
                    : $"{path} must be a non-negative invariant decimal string or null.");
        }
    }

    private static void ValidateDate(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return;
        }
        var text = RequireString(value, path, 10, 10);
        if (!DateOnly.TryParseExact(
                text,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            throw Invalid($"{path} must be an ISO calendar date or null.");
        }
    }

    private static void RequireExactObject(
        JsonElement element,
        HashSet<string> expectedKeys,
        string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{path} must be an object.");
        }
        var properties = element.EnumerateObject().ToArray();
        var actualKeys = properties
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (properties.Length != expectedKeys.Count ||
            !actualKeys.SetEquals(expectedKeys))
        {
            throw Invalid($"{path} fields do not match the v1 allowlist.");
        }
    }

    private static Guid RequireGuid(JsonElement value, string path)
    {
        var text = RequireString(value, path, 36, 36);
        if (!Guid.TryParseExact(text, "D", out var parsed) || parsed == Guid.Empty)
        {
            throw Invalid($"{path} must be a non-empty canonical UUID.");
        }
        return parsed;
    }

    private static string RequireString(
        JsonElement value,
        string path,
        int minimumLength,
        int maximumLength,
        string? exactValue = null)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"{path} must be a string.");
        }
        var text = value.GetString()!;
        if (text.Length < minimumLength ||
            text.Length > maximumLength ||
            exactValue is not null &&
            !string.Equals(text, exactValue, StringComparison.Ordinal))
        {
            throw Invalid($"{path} has an invalid value or length.");
        }
        return text;
    }

    private static void RequireNullableString(
        JsonElement value,
        string path,
        int maximumLength)
    {
        if (value.ValueKind != JsonValueKind.Null)
        {
            _ = RequireString(value, path, 0, maximumLength);
        }
    }

    private static int RequireInteger(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var parsed))
        {
            throw Invalid($"{path} must be an integer.");
        }
        return parsed;
    }

    private static double RequireNumber(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var parsed) ||
            !double.IsFinite(parsed))
        {
            throw Invalid($"{path} must be a finite number.");
        }
        return parsed;
    }

    private static void FindForbiddenKeys(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (ForbiddenKeys.Contains(property.Name))
                {
                    throw Invalid(
                        $"OCR output contains forbidden authority field at {path}.{property.Name}.");
                }
                FindForbiddenKeys(property.Value, $"{path}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                FindForbiddenKeys(item, $"{path}[{index++}]");
            }
        }
    }

    private static Regex Pattern(string pattern) => new(
        pattern,
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static OcrProviderException Invalid(
        string message,
        Exception? inner = null) =>
        new("OCR_SCHEMA_INVALID", message, inner);

    private enum ValueKind
    {
        Text,
        Decimal,
        Percentage,
        Date,
        Currency
    }
}
