namespace PharmaAuto.Connector.Domain.Tests;

public sealed class CommercialRulesTests
{
    [Fact]
    public void Calculate_AppliesTheApprovedSequentialPercentageRules()
    {
        var values = CreateValues();

        var result = CommercialRules.Calculate(2m, values);

        Assert.Equal(100m, result.GrossPurchaseUnitPrice);
        Assert.Equal(90m, result.PurchaseUnitPriceAfterDiscount1);
        Assert.Equal(180m, result.LineSubtotalAfterDiscount1);
        Assert.Equal(171m, result.NetLineSubtotalAfterDiscount2);
    }

    [Fact]
    public void Calculate_RejectsAnySellingPricePolicyThatCouldRepriceOldStock()
    {
        var invalid = CreateValues() with
        {
            SellingPriceScope = "GLOBAL_ITEM",
            ExistingStockPriceBehavior = "REPRICE"
        };

        var exception = Assert.Throws<CommercialRuleException>(
            () => CommercialRules.Calculate(2m, invalid));

        Assert.Contains("sellingPriceScope must be NEW_STOCK_ONLY.", exception.Errors);
        Assert.Contains("existingStockPriceBehavior must be PRESERVE.", exception.Errors);
    }

    private static CommercialValues CreateValues() =>
        new(
            Currency: "EGP",
            PurchaseUnit: "BOX",
            PurchaseUnitPrice: 100m,
            PurchasePriceTaxTreatment: "EXCLUSIVE",
            Discounts:
            [
                new PercentageDiscount(
                    1,
                    10m,
                    DiscountApplicationBasis.PurchaseUnitPrice,
                    true),
                new PercentageDiscount(
                    2,
                    5m,
                    DiscountApplicationBasis.RemainingLineSubtotal,
                    false)
            ],
            SellingUnit: "BOX",
            SellingUnitPrice: 150m,
            SellingPriceTaxTreatment: "INCLUSIVE",
            SellingPriceScope: "NEW_STOCK_ONLY",
            ExistingStockPriceBehavior: "PRESERVE",
            UnsupportedScopeBehavior: "BLOCK_COMMIT");
}
