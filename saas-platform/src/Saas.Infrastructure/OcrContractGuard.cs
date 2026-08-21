using System.Globalization;
using System.Text.Json;
using PharmaAuto.Saas.Domain;

namespace PharmaAuto.Saas.Infrastructure;

public static class OcrContractGuard
{
    private static readonly HashSet<string> RootKeys =
    [
        "schemaVersion",
        "resultId",
        "jobId",
        "provider",
        "document",
        "supplier",
        "invoiceNumber",
        "invoiceDate",
        "currency",
        "sourceLines",
        "totals",
        "qualityFlags"
    ];

    private static readonly HashSet<string> ForbiddenKeys =
    [
        "itm_id",
        "itmId",
        "ven_id",
        "venId",
        "c_id",
        "cId",
        "pth_id",
        "pthId",
        "sql",
        "sqlText"
    ];

    public static void Validate(string json, OcrDocument document)
    {
        using var parsed = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });
        var root = parsed.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("OCR result root must be an object.");
        }

        var actualRootKeys = root.EnumerateObject().Select(property => property.Name).ToHashSet();
        if (!actualRootKeys.SetEquals(RootKeys))
        {
            throw Invalid("OCR result root fields do not match the v1 allowlist.");
        }

        if (root.GetProperty("schemaVersion").GetString() != "1.0" ||
            root.GetProperty("jobId").GetString() != document.JobId.ToString("D"))
        {
            throw Invalid("OCR result schema or job identity does not match the request.");
        }

        var documentElement = root.GetProperty("document");
        if (documentElement.GetProperty("sourceSha256").GetString() != document.SourceSha256 ||
            documentElement.GetProperty("pageCount").GetInt32() != document.Pages.Count)
        {
            throw Invalid("OCR result document evidence does not match the submitted document.");
        }

        var sourceLines = root.GetProperty("sourceLines");
        if (sourceLines.ValueKind != JsonValueKind.Array || sourceLines.GetArrayLength() is < 1 or > 1000)
        {
            throw Invalid("OCR result must contain 1..1000 source lines.");
        }

        var expectedSequence = 1;
        foreach (var line in sourceLines.EnumerateArray())
        {
            if (line.GetProperty("sequence").GetInt32() != expectedSequence++)
            {
                throw Invalid("OCR source-line sequence must be contiguous and start at 1.");
            }
            ValidateDecimalField(line.GetProperty("quantity"), "quantity");
            ValidateDecimalField(line.GetProperty("purchaseUnitPrice"), "purchaseUnitPrice");
            ValidateDecimalField(line.GetProperty("discount1Percentage"), "discount1Percentage");
            ValidateDecimalField(line.GetProperty("discount2Percentage"), "discount2Percentage");
            ValidateDecimalField(line.GetProperty("sellingUnitPrice"), "sellingUnitPrice");
        }

        FindForbiddenKeys(root, "$", []);
    }

    private static void ValidateDecimalField(JsonElement field, string name)
    {
        var value = field.GetProperty("normalizedValue");
        if (value.ValueKind == JsonValueKind.Null)
        {
            return;
        }
        if (value.ValueKind != JsonValueKind.String ||
            !decimal.TryParse(
                value.GetString(),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed < 0m)
        {
            throw Invalid($"OCR {name} must be a non-negative invariant decimal string or null.");
        }
    }

    private static void FindForbiddenKeys(
        JsonElement element,
        string path,
        HashSet<string> visited)
    {
        _ = visited;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (ForbiddenKeys.Contains(property.Name))
                {
                    throw Invalid($"OCR output contains forbidden authority field at {path}.{property.Name}.");
                }
                FindForbiddenKeys(property.Value, $"{path}.{property.Name}", visited);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                FindForbiddenKeys(item, $"{path}[{index++}]", visited);
            }
        }
    }

    private static OcrProviderException Invalid(string message) =>
        new("OCR_SCHEMA_INVALID", message);
}
