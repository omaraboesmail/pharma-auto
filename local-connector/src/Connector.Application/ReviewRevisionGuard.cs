using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PharmaAuto.Connector.Domain;

namespace PharmaAuto.Connector.Application;

public static class ReviewRevisionGuard
{
    private static readonly JsonSerializerOptions ContractJsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) }
    };
    private static readonly HashSet<string> ForbiddenKeys =
    [
        "itm_id",
        "ven_id",
        "c_id",
        "pth_id",
        "sql",
        "sqlText",
        "geniusWriteRequested"
    ];

    public static string PrepareEditedRevision(
        string json,
        string trustedSourceJson,
        LocalVendorCandidate? validatedVendorCandidate,
        IReadOnlyDictionary<string, LocalItemCandidate> validatedItemCandidates,
        Guid jobId,
        Guid revisionId,
        Guid sourceRevisionId,
        int revisionNumber,
        string reason,
        Guid deviceId,
        DateTimeOffset createdAt)
    {
        if (EncodingLength(json) > 4 * 1024 * 1024)
        {
            throw new ArgumentException("Review revision exceeds the 4 MiB limit.", nameof(json));
        }
        var root = JsonNode.Parse(
            json,
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 96
            })?.AsObject()
            ?? throw new ArgumentException("Review revision must be a JSON object.", nameof(json));
        var trusted = JsonNode.Parse(trustedSourceJson)?.AsObject()
            ?? throw new InvalidOperationException("Trusted source revision is invalid.");
        FindForbidden(root, "$", 0);
        AddValidatedCandidates(
            root,
            trusted,
            validatedVendorCandidate,
            validatedItemCandidates);
        ValidateTrustedEvidence(root, trusted);
        root["schemaVersion"] = "1.0";
        root["jobId"] = jobId.ToString("D");
        root["revisionId"] = revisionId.ToString("D");
        root["revisionNumber"] = revisionNumber;
        root["sourceRevisionId"] = sourceRevisionId.ToString("D");
        root["status"] = "AWAITING_USER_REVIEW";
        root["createdByDeviceId"] = deviceId.ToString("D");
        root["createdAt"] = createdAt.ToString("O");
        root["revisionReason"] = reason;
        root["geniusWritePerformed"] = false;
        ValidateCommercialSemantics(root);
        return root.ToJsonString();
    }

    public static void EnsureConfirmable(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Review revision is invalid.");
        FindForbidden(root, "$", 0);
        if (root["geniusWritePerformed"]?.GetValue<bool>() != false ||
            root["status"]?.GetValue<string>() != "AWAITING_USER_REVIEW")
        {
            throw new InvalidOperationException("Review revision does not have a confirmable read-only state.");
        }
        if (string.IsNullOrWhiteSpace(root["selectedLocalVendorReference"]?.GetValue<string?>()))
        {
            throw new InvalidOperationException("A local Vendor must be manually confirmed.");
        }
        EnsureVendorSelectionIsCandidate(root);
        var sourceLines = root["sourceLines"]?.AsArray()
            ?? throw new InvalidOperationException("Review revision contains no source lines.");
        if (sourceLines.Count == 0)
        {
            throw new InvalidOperationException("Review revision contains no source lines.");
        }
        foreach (var sourceNode in sourceLines)
        {
            var source = sourceNode?.AsObject()
                ?? throw new InvalidOperationException("Review source line is invalid.");
            if (string.IsNullOrWhiteSpace(source["selectedLocalItemReference"]?.GetValue<string?>()))
            {
                throw new InvalidOperationException("Every source line requires manual local Item confirmation.");
            }
            EnsureItemSelectionIsSafeCandidate(source);
            var postingLines = source["postingLines"]?.AsArray()
                ?? throw new InvalidOperationException("Source line contains no Posting Lines.");
            if (postingLines.Count == 0)
            {
                throw new InvalidOperationException("Source line contains no Posting Lines.");
            }
            foreach (var postingNode in postingLines)
            {
                var posting = postingNode?.AsObject()
                    ?? throw new InvalidOperationException("Posting Line is invalid.");
                RequirePositiveDecimal(posting["quantity"], "quantity");
                var expiryDate = posting["expiryDate"]?.GetValue<string?>();
                if (string.IsNullOrWhiteSpace(expiryDate) ||
                    !DateOnly.TryParseExact(
                        expiryDate,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out _))
                {
                    throw new InvalidOperationException(
                        "Every Phase 1 Posting Line requires a valid yyyy-MM-dd expiry date or a future supervised override.");
                }
            }
        }
        ValidateCommercialSemantics(root);
    }

    private static void ValidateTrustedEvidence(JsonObject edited, JsonObject trusted)
    {
        RequireUnchanged(edited, trusted, "vendorEvidence", "$.");
        RequireUnchanged(edited, trusted, "vendorCandidates", "$.");
        RequireUnchanged(edited, trusted, "qualityFlags", "$.");
        RequireUnchanged(edited, trusted, "ocrEvidence", "$.");
        EnsureVendorSelectionIsCandidate(edited);
        edited["requiresManualVendorConfirmation"] = string.IsNullOrWhiteSpace(
            edited["selectedLocalVendorReference"]?.GetValue<string?>());

        var editedLines = edited["sourceLines"]?.AsArray()
            ?? throw new InvalidOperationException("Edited revision contains no source lines.");
        var trustedLines = trusted["sourceLines"]?.AsArray()
            ?? throw new InvalidOperationException("Trusted revision contains no source lines.");
        if (editedLines.Count != trustedLines.Count)
        {
            throw new InvalidOperationException("Source line identity or count cannot be changed.");
        }
        for (var index = 0; index < trustedLines.Count; index++)
        {
            var editedLine = editedLines[index]?.AsObject()
                ?? throw new InvalidOperationException("Edited source line is invalid.");
            var trustedLine = trustedLines[index]?.AsObject()
                ?? throw new InvalidOperationException("Trusted source line is invalid.");
            RequireUnchanged(editedLine, trustedLine, "sourceLineId", $"$.sourceLines[{index}].");
            RequireUnchanged(editedLine, trustedLine, "sequence", $"$.sourceLines[{index}].");
            RequireUnchanged(
                editedLine,
                trustedLine,
                "descriptionEvidence",
                $"$.sourceLines[{index}].");
            RequireUnchanged(
                editedLine,
                trustedLine,
                "vendorItemCodeEvidence",
                $"$.sourceLines[{index}].");
            RequireUnchanged(editedLine, trustedLine, "localCandidates", $"$.sourceLines[{index}].");
            RequireUnchanged(
                editedLine,
                trustedLine,
                "canonicalCandidates",
                $"$.sourceLines[{index}].");
            EnsureItemSelectionIsSafeCandidate(editedLine);
            editedLine["requiresManualItemConfirmation"] = string.IsNullOrWhiteSpace(
                editedLine["selectedLocalItemReference"]?.GetValue<string?>());
            ValidatePostingEvidence(editedLine, trustedLine, index);
        }
    }

    private static void AddValidatedCandidates(
        JsonObject edited,
        JsonObject trusted,
        LocalVendorCandidate? vendorCandidate,
        IReadOnlyDictionary<string, LocalItemCandidate> itemCandidates)
    {
        if (vendorCandidate is not null)
        {
            var serialized = JsonSerializer.SerializeToNode(
                vendorCandidate,
                ContractJsonOptions);
            AppendCandidate(
                edited["vendorCandidates"]?.AsArray(),
                serialized?.DeepClone(),
                "localVendorReference",
                vendorCandidate.LocalVendorReference);
            AppendCandidate(
                trusted["vendorCandidates"]?.AsArray(),
                serialized?.DeepClone(),
                "localVendorReference",
                vendorCandidate.LocalVendorReference);
        }

        var editedLines = edited["sourceLines"]?.AsArray() ?? [];
        var trustedLines = trusted["sourceLines"]?.AsArray() ?? [];
        foreach (var (editedNode, index) in editedLines.Select((node, index) => (node, index)))
        {
            if (index >= trustedLines.Count || editedNode is not JsonObject editedLine ||
                trustedLines[index] is not JsonObject trustedLine)
            {
                continue;
            }
            var sourceLineId = trustedLine["sourceLineId"]?.GetValue<string?>();
            if (sourceLineId is null || !itemCandidates.TryGetValue(sourceLineId, out var candidate))
            {
                continue;
            }
            var serialized = JsonSerializer.SerializeToNode(candidate, ContractJsonOptions);
            AppendCandidate(
                editedLine["localCandidates"]?.AsArray(),
                serialized?.DeepClone(),
                "localItemReference",
                candidate.LocalItemReference);
            AppendCandidate(
                trustedLine["localCandidates"]?.AsArray(),
                serialized?.DeepClone(),
                "localItemReference",
                candidate.LocalItemReference);
        }
    }

    private static void AppendCandidate(
        JsonArray? candidates,
        JsonNode? candidate,
        string referenceProperty,
        string reference)
    {
        if (candidates is null || candidate is null || candidates.Any(existing =>
                existing?[referenceProperty]?.GetValue<string?>() == reference))
        {
            return;
        }
        candidates.Add(candidate);
    }

    private static void ValidatePostingEvidence(
        JsonObject editedLine,
        JsonObject trustedLine,
        int sourceIndex)
    {
        var trustedPostings = trustedLine["postingLines"]?.AsArray()
            ?? throw new InvalidOperationException("Trusted source line contains no Posting Lines.");
        var editedPostings = editedLine["postingLines"]?.AsArray()
            ?? throw new InvalidOperationException("Edited source line contains no Posting Lines.");
        if (editedPostings.Count is < 1 or > 100)
        {
            throw new InvalidOperationException("A source line must contain 1..100 Posting Lines.");
        }
        var postingIds = new HashSet<Guid>();
        var splitIndexes = new HashSet<int>();
        decimal editedQuantity = 0m;
        decimal trustedQuantity = 0m;
        foreach (var trustedPosting in trustedPostings)
        {
            trustedQuantity += RequireNonNegativeDecimal(
                trustedPosting?["quantity"],
                "trusted quantity");
        }
        foreach (var (editedNode, postingIndex) in editedPostings.Select((node, index) => (node, index)))
        {
            var posting = editedNode?.AsObject()
                ?? throw new InvalidOperationException("Edited Posting Line is invalid.");
            if (!Guid.TryParse(posting["postingLineId"]?.GetValue<string?>(), out var postingId) ||
                !postingIds.Add(postingId))
            {
                throw new InvalidOperationException("Posting Line identities must be unique UUIDs.");
            }
            var splitIndex = posting["splitIndex"]?.GetValue<int?>() ?? 0;
            if (splitIndex < 1 || !splitIndexes.Add(splitIndex))
            {
                throw new InvalidOperationException("Posting Line split indexes must be unique positive integers.");
            }
            if ((posting["postingSequence"]?.GetValue<int?>() ?? 0) < 1)
            {
                throw new InvalidOperationException("Posting sequence must be positive.");
            }
            editedQuantity += RequirePositiveDecimal(posting["quantity"], "quantity");
            var originalEvidence = posting["originalOcrCommercialValues"];
            if (!trustedPostings.Any(trustedPosting => JsonNode.DeepEquals(
                    trustedPosting?["originalOcrCommercialValues"],
                    originalEvidence)))
            {
                throw new InvalidOperationException(
                    $"Posting Line OCR evidence cannot be changed at $.sourceLines[{sourceIndex}].postingLines[{postingIndex}].");
            }
            var bonus = posting["bonus"];
            if (!trustedPostings.Any(trustedPosting => JsonNode.DeepEquals(
                    trustedPosting?["bonus"],
                    bonus)))
            {
                throw new InvalidOperationException("Phase 1 does not allow bonus evidence to be edited.");
            }
        }
        if (editedQuantity != trustedQuantity)
        {
            throw new InvalidOperationException(
                "Posting Line quantities must equal the trusted source-line quantity.");
        }
    }

    private static void EnsureVendorSelectionIsCandidate(JsonObject root)
    {
        var selected = root["selectedLocalVendorReference"]?.GetValue<string?>();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }
        var candidates = root["vendorCandidates"]?.AsArray() ?? [];
        if (!candidates.Any(candidate =>
                candidate?["localVendorReference"]?.GetValue<string?>() == selected))
        {
            throw new InvalidOperationException(
                "Selected local Vendor is not a Connector-issued candidate.");
        }
    }

    private static void EnsureItemSelectionIsSafeCandidate(JsonObject source)
    {
        var selected = source["selectedLocalItemReference"]?.GetValue<string?>();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }
        var candidate = (source["localCandidates"]?.AsArray() ?? [])
            .FirstOrDefault(value =>
                value?["localItemReference"]?.GetValue<string?>() == selected)
            ?.AsObject();
        if (candidate is null)
        {
            throw new InvalidOperationException(
                "Selected local Item is not a Connector-issued candidate.");
        }
        if ((candidate["hardMismatches"]?.AsArray()?.Count ?? 0) > 0)
        {
            throw new InvalidOperationException(
                "Selected local Item has a blocking pharmaceutical mismatch.");
        }
    }

    private static void RequireUnchanged(
        JsonObject edited,
        JsonObject trusted,
        string property,
        string path)
    {
        if (!JsonNode.DeepEquals(edited[property], trusted[property]))
        {
            throw new InvalidOperationException(
                $"Trusted review evidence cannot be changed at {path}{property}.");
        }
    }

    private static void ValidateCommercialSemantics(JsonObject root)
    {
        var sourceLines = root["sourceLines"]?.AsArray()
            ?? throw new InvalidOperationException("Review revision contains no source lines.");
        foreach (var posting in sourceLines
                     .SelectMany(source => source?["postingLines"]?.AsArray() ?? []))
        {
            var values = posting?["commercialValues"]?.AsObject()
                ?? throw new InvalidOperationException("Posting Line commercial values are missing.");
            Require(values, "currency", "EGP");
            Require(values, "sellingUnit", "BOX");
            Require(values, "sellingPriceTaxTreatment", "INCLUSIVE");
            Require(values, "sellingPriceScope", "NEW_STOCK_ONLY");
            Require(values, "existingStockPriceBehavior", "PRESERVE");
            Require(values, "unsupportedScopeBehavior", "BLOCK_COMMIT");
            RequireNonNegativeDecimal(values["purchaseUnitPrice"], "purchaseUnitPrice");
            RequireNonNegativeDecimal(values["sellingUnitPrice"], "sellingUnitPrice");
            var discounts = values["discounts"]?.AsArray()
                ?? throw new InvalidOperationException("Exactly two discounts are required.");
            if (discounts.Count != 2)
            {
                throw new InvalidOperationException("Exactly two discounts are required.");
            }
            ValidateDiscount(discounts[0], 1, "PURCHASE_UNIT_PRICE", true);
            ValidateDiscount(discounts[1], 2, "REMAINING_LINE_SUBTOTAL", false);
        }
    }

    private static void ValidateDiscount(
        JsonNode? node,
        int sequence,
        string basis,
        bool affectsPurchaseUnitPrice)
    {
        var discount = node?.AsObject()
            ?? throw new InvalidOperationException("Discount is invalid.");
        if (discount["sequence"]?.GetValue<int>() != sequence ||
            discount["kind"]?.GetValue<string>() != "PERCENTAGE" ||
            discount["applicationBasis"]?.GetValue<string>() != basis ||
            discount["affectsPurchaseUnitPrice"]?.GetValue<bool>() != affectsPurchaseUnitPrice)
        {
            throw new InvalidOperationException($"Discount {sequence} violates the approved semantics.");
        }
        var percentage = RequireNonNegativeDecimal(discount["percentage"], $"discount {sequence}");
        if (percentage > 100m)
        {
            throw new InvalidOperationException($"Discount {sequence} must be at most 100 percent.");
        }
    }

    private static decimal RequirePositiveDecimal(JsonNode? node, string field)
    {
        var parsed = RequireNonNegativeDecimal(node, field);
        if (parsed <= 0m)
        {
            throw new InvalidOperationException($"{field} must be greater than zero.");
        }
        return parsed;
    }

    private static decimal RequireNonNegativeDecimal(JsonNode? node, string field)
    {
        var value = node?.GetValue<string>();
        if (value is null ||
            !decimal.TryParse(
                value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed) ||
            parsed < 0m)
        {
            throw new InvalidOperationException($"{field} must be a non-negative invariant decimal string.");
        }
        return parsed;
    }

    private static void Require(JsonObject values, string field, string expected)
    {
        if (values[field]?.GetValue<string>() != expected)
        {
            throw new InvalidOperationException($"{field} must remain {expected}.");
        }
    }

    private static void FindForbidden(JsonNode node, string path, int depth)
    {
        if (depth > 96)
        {
            throw new InvalidOperationException("Review revision exceeds the maximum JSON depth.");
        }
        if (node is JsonObject objectNode)
        {
            foreach (var property in objectNode)
            {
                if (ForbiddenKeys.Contains(property.Key))
                {
                    throw new InvalidOperationException(
                        $"Review revision contains forbidden ERP authority at {path}.{property.Key}.");
                }
                if (property.Value is not null)
                {
                    FindForbidden(property.Value, $"{path}.{property.Key}", depth + 1);
                }
            }
        }
        else if (node is JsonArray arrayNode)
        {
            for (var index = 0; index < arrayNode.Count; index++)
            {
                if (arrayNode[index] is not null)
                {
                    FindForbidden(arrayNode[index]!, $"{path}[{index}]", depth + 1);
                }
            }
        }
    }

    private static int EncodingLength(string value) =>
        System.Text.Encoding.UTF8.GetByteCount(value);
}
