namespace PharmaAuto.Connector.Domain;

public enum DiscountApplicationBasis
{
    PurchaseUnitPrice,
    RemainingLineSubtotal
}

public sealed record PercentageDiscount(
    int Sequence,
    decimal Percentage,
    DiscountApplicationBasis ApplicationBasis,
    bool AffectsPurchaseUnitPrice);

public sealed record CommercialValues(
    string Currency,
    string PurchaseUnit,
    decimal PurchaseUnitPrice,
    string PurchasePriceTaxTreatment,
    IReadOnlyList<PercentageDiscount> Discounts,
    string SellingUnit,
    decimal SellingUnitPrice,
    string SellingPriceTaxTreatment,
    string SellingPriceScope,
    string ExistingStockPriceBehavior,
    string UnsupportedScopeBehavior);

public sealed record CommercialCalculation(
    decimal GrossPurchaseUnitPrice,
    decimal PurchaseUnitPriceAfterDiscount1,
    decimal LineSubtotalAfterDiscount1,
    decimal NetLineSubtotalAfterDiscount2);

public static class CommercialRules
{
    public const string Currency = "EGP";
    public const string SellingUnit = "BOX";
    public const string SellingPriceTaxTreatment = "INCLUSIVE";
    public const string SellingPriceScope = "NEW_STOCK_ONLY";
    public const string ExistingStockPriceBehavior = "PRESERVE";
    public const string UnsupportedScopeBehavior = "BLOCK_COMMIT";

    public static CommercialCalculation Calculate(decimal quantity, CommercialValues values)
    {
        var errors = Validate(quantity, values);
        if (errors.Count > 0)
        {
            throw new CommercialRuleException(errors);
        }

        var discount1Multiplier = (100m - values.Discounts[0].Percentage) / 100m;
        var purchaseUnitPriceAfterDiscount1 = values.PurchaseUnitPrice * discount1Multiplier;
        var lineSubtotalAfterDiscount1 = quantity * purchaseUnitPriceAfterDiscount1;
        var discount2Multiplier = (100m - values.Discounts[1].Percentage) / 100m;
        var netLineSubtotalAfterDiscount2 = lineSubtotalAfterDiscount1 * discount2Multiplier;

        return new CommercialCalculation(
            values.PurchaseUnitPrice,
            purchaseUnitPriceAfterDiscount1,
            lineSubtotalAfterDiscount1,
            netLineSubtotalAfterDiscount2);
    }

    public static IReadOnlyList<string> Validate(decimal quantity, CommercialValues values)
    {
        var errors = new List<string>();

        if (quantity <= 0m)
        {
            errors.Add("quantity must be greater than zero.");
        }

        if (values.PurchaseUnitPrice < 0m)
        {
            errors.Add("purchaseUnitPrice cannot be negative.");
        }

        if (values.SellingUnitPrice < 0m)
        {
            errors.Add("sellingUnitPrice cannot be negative.");
        }

        RequireEqual(errors, "currency", values.Currency, Currency);
        RequireEqual(errors, "sellingUnit", values.SellingUnit, SellingUnit);
        RequireEqual(errors, "sellingPriceTaxTreatment", values.SellingPriceTaxTreatment, SellingPriceTaxTreatment);
        RequireEqual(errors, "sellingPriceScope", values.SellingPriceScope, SellingPriceScope);
        RequireEqual(errors, "existingStockPriceBehavior", values.ExistingStockPriceBehavior, ExistingStockPriceBehavior);
        RequireEqual(errors, "unsupportedScopeBehavior", values.UnsupportedScopeBehavior, UnsupportedScopeBehavior);

        if (values.Discounts.Count != 2)
        {
            errors.Add("discounts must contain exactly two sequential percentage discounts.");
            return errors;
        }

        ValidatePercentage(errors, values.Discounts[0], 1);
        ValidatePercentage(errors, values.Discounts[1], 2);

        if (values.Discounts[0].ApplicationBasis != DiscountApplicationBasis.PurchaseUnitPrice ||
            !values.Discounts[0].AffectsPurchaseUnitPrice)
        {
            errors.Add("discount 1 must affect the purchase unit price.");
        }

        if (values.Discounts[1].ApplicationBasis != DiscountApplicationBasis.RemainingLineSubtotal ||
            values.Discounts[1].AffectsPurchaseUnitPrice)
        {
            errors.Add("discount 2 must apply to the remaining line subtotal without rewriting the purchase unit price.");
        }

        return errors;
    }

    private static void ValidatePercentage(List<string> errors, PercentageDiscount discount, int sequence)
    {
        if (discount.Sequence != sequence)
        {
            errors.Add($"discount {sequence} must have sequence {sequence}.");
        }

        if (discount.Percentage is < 0m or > 100m)
        {
            errors.Add($"discount {sequence} percentage must be between 0 and 100.");
        }
    }

    private static void RequireEqual(
        List<string> errors,
        string field,
        string actual,
        string required)
    {
        if (!string.Equals(actual, required, StringComparison.Ordinal))
        {
            errors.Add($"{field} must be {required}.");
        }
    }
}

public sealed class CommercialRuleException(IReadOnlyList<string> errors)
    : Exception("The commercial values violate the approved initialization rules.")
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
