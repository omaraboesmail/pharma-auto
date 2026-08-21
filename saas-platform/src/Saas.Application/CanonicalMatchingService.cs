using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PharmaAuto.Saas.Domain;

namespace PharmaAuto.Saas.Application;

public sealed partial class CanonicalMatchingService(
    ISaasStore store,
    IEmbeddingProvider embeddingProvider)
{
    public async Task<IReadOnlyList<CanonicalCandidate>> SearchAsync(
        Guid tenantId,
        CanonicalSearchQuery query,
        CancellationToken cancellationToken)
    {
        if (query.Limit is < 1 or > 25)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Candidate limit must be 1..25.");
        }

        var normalizedDescription = Normalize(query.Description);
        var embedding = await embeddingProvider.EmbedAsync(
            normalizedDescription,
            cancellationToken);
        var products = await store.SearchCanonicalProductsAsync(
            tenantId,
            query with { Description = normalizedDescription },
            embedding,
            embedding is null ? null : embeddingProvider.Version,
            cancellationToken);

        return products
            .Select(product => BuildCandidate(query, normalizedDescription, product))
            .Where(candidate => candidate.ReasonCodes.Count > 0)
            .OrderBy(candidate => candidate.HardMismatches.Count)
            .ThenByDescending(candidate => CandidateRank(candidate.ReasonCodes))
            .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(query.Limit)
            .ToArray();
    }

    public static string Normalize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        normalized = Whitespace().Replace(normalized, " ");
        return normalized.ToUpper(CultureInfo.InvariantCulture);
    }

    private static CanonicalCandidate BuildCandidate(
        CanonicalSearchQuery query,
        string normalizedDescription,
        CanonicalProduct product)
    {
        var reasons = new List<string>();
        var mismatches = new List<string>();

        if (!string.IsNullOrWhiteSpace(query.VendorItemCode) &&
            product.Identifiers.Any(identifier =>
                string.Equals(identifier, query.VendorItemCode, StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add("EXACT_IDENTIFIER");
        }

        var normalizedNames = product.Aliases
            .Append(product.DisplayName)
            .Select(Normalize)
            .ToArray();
        if (normalizedNames.Contains(normalizedDescription, StringComparer.Ordinal))
        {
            reasons.Add("EXACT_NORMALIZED_NAME");
        }
        else if (normalizedNames.Any(name => TokenOverlap(name, normalizedDescription) >= 0.5m))
        {
            reasons.Add("LEXICAL_SHORTLIST");
        }

        CompareAttribute(
            query.Attributes.ActiveIngredient,
            product.Attributes.ActiveIngredient,
            "ACTIVE_INGREDIENT",
            reasons,
            mismatches);
        CompareAttribute(
            query.Attributes.Strength,
            product.Attributes.Strength,
            "STRENGTH",
            reasons,
            mismatches);
        CompareAttribute(
            query.Attributes.DosageForm,
            product.Attributes.DosageForm,
            "DOSAGE_FORM",
            reasons,
            mismatches);
        CompareAttribute(
            query.Attributes.Pack,
            product.Attributes.Pack,
            "PACK",
            reasons,
            mismatches);

        if (reasons.Remove("ATTRIBUTE_MATCH"))
        {
            reasons.Add("STRUCTURED_ATTRIBUTES");
        }
        if (reasons.Count > 0 && !reasons.Contains("EXACT_IDENTIFIER", StringComparer.Ordinal))
        {
            reasons.Add("CANONICAL_SHORTLIST");
        }

        return new CanonicalCandidate(
            product.CanonicalProductId,
            product.DisplayName,
            product.Attributes,
            reasons.Distinct(StringComparer.Ordinal).ToArray(),
            mismatches.Distinct(StringComparer.Ordinal).ToArray(),
            true);
    }

    private static void CompareAttribute(
        string? requested,
        string? actual,
        string mismatchCode,
        List<string> reasons,
        List<string> mismatches)
    {
        if (string.IsNullOrWhiteSpace(requested) || string.IsNullOrWhiteSpace(actual))
        {
            return;
        }

        if (string.Equals(Normalize(requested), Normalize(actual), StringComparison.Ordinal))
        {
            reasons.Add("ATTRIBUTE_MATCH");
        }
        else
        {
            mismatches.Add(mismatchCode);
        }
    }

    private static decimal TokenOverlap(string first, string second)
    {
        var firstTokens = first.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var secondTokens = second.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (firstTokens.Count == 0 || secondTokens.Count == 0)
        {
            return 0m;
        }

        var intersection = firstTokens.Count(secondTokens.Contains);
        return intersection / (decimal)Math.Max(firstTokens.Count, secondTokens.Count);
    }

    private static int CandidateRank(IReadOnlyList<string> reasons)
    {
        if (reasons.Contains("EXACT_IDENTIFIER", StringComparer.Ordinal))
        {
            return 4;
        }
        if (reasons.Contains("EXACT_NORMALIZED_NAME", StringComparer.Ordinal))
        {
            return 3;
        }
        if (reasons.Contains("STRUCTURED_ATTRIBUTES", StringComparer.Ordinal))
        {
            return 2;
        }
        return 1;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
