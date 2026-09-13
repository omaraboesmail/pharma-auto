using System.Globalization;
using System.Text.RegularExpressions;
using PharmaAuto.Connector.Domain;

namespace PharmaAuto.Connector.Application;

internal static class CanonicalDecimalContract
{
    internal const int MaximumIntegerDigits = 12;
    internal const int DecimalMaximumLength = 19;
    internal const int PercentageMaximumLength = 8;

    private static readonly Regex DecimalPattern = new(
        "^(?:0|[1-9][0-9]{0,11})(?:\\.[0-9]{1,6})?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex PercentagePattern = new(
        "^(?:100(?:\\.0{1,4})?|(?:[0-9]|[1-9][0-9])(?:\\.[0-9]{1,4})?)$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    internal static bool TryParseDecimal(string? value, out decimal parsed)
    {
        parsed = default;
        return value is not null &&
            value.Length <= DecimalMaximumLength &&
            DecimalPattern.IsMatch(value) &&
            decimal.TryParse(
                value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out parsed) &&
            parsed <= CommercialRules.MaximumContractDecimal;
    }

    internal static bool TryParsePercentage(string? value, out decimal parsed)
    {
        parsed = default;
        return value is not null &&
            value.Length <= PercentageMaximumLength &&
            PercentagePattern.IsMatch(value) &&
            decimal.TryParse(
                value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out parsed);
    }
}
