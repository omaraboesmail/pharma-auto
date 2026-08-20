using System.Globalization;
using PharmaAuto.Connector.Domain;

namespace PharmaAuto.Connector.Application;

public sealed record PercentageDiscountContract(
    int Sequence,
    string Kind,
    string Percentage,
    string ApplicationBasis,
    bool AffectsPurchaseUnitPrice);

public sealed record CommercialValuesContract(
    string Currency,
    string PurchaseUnit,
    string PurchaseUnitPrice,
    string PurchasePriceTaxTreatment,
    IReadOnlyList<PercentageDiscountContract> Discounts,
    string SellingUnit,
    string SellingUnitPrice,
    string SellingPriceTaxTreatment,
    string SellingPriceScope,
    string ExistingStockPriceBehavior,
    string UnsupportedScopeBehavior);

public sealed record CommercialEditPreviewRequest(
    Guid ExpectedRevisionId,
    string Quantity,
    CommercialValuesContract CommercialValues);

public sealed record CommercialEditPreviewResponse(
    Guid RevisionId,
    Guid PostingLineId,
    string Currency,
    string GrossPurchaseUnitPrice,
    string PurchaseUnitPriceAfterDiscount1,
    string LineSubtotalAfterDiscount1,
    string NetLineSubtotalAfterDiscount2,
    string SellingUnit,
    string SellingUnitPrice,
    string SellingPriceTaxTreatment,
    string SellingPriceScope,
    string ExistingStockPriceBehavior,
    string UnsupportedScopeBehavior,
    bool GeniusWritePerformed);

public sealed class CommercialEditPreviewService
{
    private const string DecimalFormat = "0.######";

    public CommercialEditPreviewResponse Preview(
        Guid revisionId,
        Guid postingLineId,
        CommercialEditPreviewRequest request)
    {
        var errors = new List<string>();

        if (request.ExpectedRevisionId != revisionId)
        {
            errors.Add("expectedRevisionId must match the revisionId path parameter.");
        }

        if (!TryParseDecimal(request.Quantity, "quantity", errors, out var quantity))
        {
            throw new CommercialPreviewValidationException(errors);
        }

        var contract = request.CommercialValues;
        if (!TryParseDecimal(contract.PurchaseUnitPrice, "purchaseUnitPrice", errors, out var purchaseUnitPrice) ||
            !TryParseDecimal(contract.SellingUnitPrice, "sellingUnitPrice", errors, out var sellingUnitPrice))
        {
            throw new CommercialPreviewValidationException(errors);
        }

        if (contract.Discounts.Count != 2)
        {
            errors.Add("discounts must contain exactly two entries.");
            throw new CommercialPreviewValidationException(errors);
        }

        var discounts = contract.Discounts
            .Select(discount => MapDiscount(discount, errors))
            .ToArray();

        if (errors.Count > 0)
        {
            throw new CommercialPreviewValidationException(errors);
        }

        var values = new CommercialValues(
            contract.Currency,
            contract.PurchaseUnit,
            purchaseUnitPrice,
            contract.PurchasePriceTaxTreatment,
            discounts,
            contract.SellingUnit,
            sellingUnitPrice,
            contract.SellingPriceTaxTreatment,
            contract.SellingPriceScope,
            contract.ExistingStockPriceBehavior,
            contract.UnsupportedScopeBehavior);

        CommercialCalculation calculation;
        try
        {
            calculation = CommercialRules.Calculate(quantity, values);
        }
        catch (CommercialRuleException exception)
        {
            throw new CommercialPreviewValidationException(exception.Errors);
        }

        return new CommercialEditPreviewResponse(
            revisionId,
            postingLineId,
            CommercialRules.Currency,
            Format(calculation.GrossPurchaseUnitPrice),
            Format(calculation.PurchaseUnitPriceAfterDiscount1),
            Format(calculation.LineSubtotalAfterDiscount1),
            Format(calculation.NetLineSubtotalAfterDiscount2),
            CommercialRules.SellingUnit,
            Format(values.SellingUnitPrice),
            CommercialRules.SellingPriceTaxTreatment,
            CommercialRules.SellingPriceScope,
            CommercialRules.ExistingStockPriceBehavior,
            CommercialRules.UnsupportedScopeBehavior,
            false);
    }

    private static PercentageDiscount MapDiscount(
        PercentageDiscountContract discount,
        List<string> errors)
    {
        if (!string.Equals(discount.Kind, "PERCENTAGE", StringComparison.Ordinal))
        {
            errors.Add($"discount {discount.Sequence} kind must be PERCENTAGE.");
        }

        _ = TryParseDecimal(
            discount.Percentage,
            $"discount {discount.Sequence} percentage",
            errors,
            out var percentage);

        var applicationBasis = discount.ApplicationBasis switch
        {
            "PURCHASE_UNIT_PRICE" => DiscountApplicationBasis.PurchaseUnitPrice,
            "REMAINING_LINE_SUBTOTAL" => DiscountApplicationBasis.RemainingLineSubtotal,
            _ => AddInvalidApplicationBasis(discount, errors)
        };

        return new PercentageDiscount(
            discount.Sequence,
            percentage,
            applicationBasis,
            discount.AffectsPurchaseUnitPrice);
    }

    private static DiscountApplicationBasis AddInvalidApplicationBasis(
        PercentageDiscountContract discount,
        List<string> errors)
    {
        errors.Add($"discount {discount.Sequence} applicationBasis is unsupported.");
        return DiscountApplicationBasis.RemainingLineSubtotal;
    }

    private static bool TryParseDecimal(
        string value,
        string field,
        List<string> errors,
        out decimal parsed)
    {
        if (decimal.TryParse(
            value,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out parsed) && parsed >= 0m)
        {
            return true;
        }

        errors.Add($"{field} must be a non-negative decimal string using a dot separator.");
        return false;
    }

    private static string Format(decimal value) =>
        value.ToString(DecimalFormat, CultureInfo.InvariantCulture);
}

public sealed class CommercialPreviewValidationException(IReadOnlyList<string> errors)
    : Exception("The commercial edit preview request is invalid.")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
