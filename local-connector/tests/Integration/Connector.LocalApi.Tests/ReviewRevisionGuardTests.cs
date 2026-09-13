using System.Text.Json;
using PharmaAuto.Connector.Application;

namespace PharmaAuto.Connector.LocalApi.Tests;

public sealed class ReviewRevisionGuardTests
{
    [Fact]
    public void EnsureConfirmable_AcceptsCanonicalNumericBoundaries()
    {
        var revision = CreateRevision(
            quantity: "999999999999.999999",
            purchaseUnitPrice: "999999999999.999999",
            sellingUnitPrice: "0.000001",
            discount1Percentage: "100.0000",
            discount2Percentage: "99.9999");

        ReviewRevisionGuard.EnsureConfirmable(revision);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1e3")]
    [InlineData("1,000")]
    [InlineData(".5")]
    [InlineData("1.")]
    [InlineData("01")]
    [InlineData("1.1234567")]
    [InlineData("1000000000000")]
    public void EnsureConfirmable_RejectsNonCanonicalDecimalStrings(string value)
    {
        var revision = CreateRevision(purchaseUnitPrice: value);

        var exception = Assert.Throws<InvalidOperationException>(
            () => ReviewRevisionGuard.EnsureConfirmable(revision));

        Assert.Contains("canonical DECIMAL(18,6)", exception.Message);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1e2")]
    [InlineData("1,0")]
    [InlineData(".5")]
    [InlineData("1.")]
    [InlineData("01")]
    [InlineData("99.99999")]
    [InlineData("100.0001")]
    [InlineData("101")]
    public void EnsureConfirmable_RejectsNonCanonicalPercentageStrings(string value)
    {
        var revision = CreateRevision(discount1Percentage: value);

        var exception = Assert.Throws<InvalidOperationException>(
            () => ReviewRevisionGuard.EnsureConfirmable(revision));

        Assert.Contains("canonical percentage", exception.Message);
    }

    [Fact]
    public void Preview_CalculatesMaximumContractValuesWithoutDecimalOverflow()
    {
        var revisionId = Guid.NewGuid();
        var service = new CommercialEditPreviewService();
        var response = service.Preview(
            revisionId,
            Guid.NewGuid(),
            new CommercialEditPreviewRequest(
                revisionId,
                "999999999999.999999",
                CreateCommercialContract(
                    "999999999999.999999",
                    "0",
                    "0",
                    "999999999999.999999")));

        Assert.Equal(revisionId, response.RevisionId);
        Assert.False(response.GeniusWritePerformed);
    }

    [Fact]
    public void Preview_RejectsAContractDecimalThatCouldOverflowDomainArithmetic()
    {
        var revisionId = Guid.NewGuid();
        var service = new CommercialEditPreviewService();
        var request = new CommercialEditPreviewRequest(
            revisionId,
            "1000000000000",
            CreateCommercialContract("1", "0", "0", "1"));

        var exception = Assert.Throws<CommercialPreviewValidationException>(
            () => service.Preview(revisionId, Guid.NewGuid(), request));

        Assert.Contains(exception.Errors, error => error.Contains("DECIMAL(18,6)"));
    }

    private static string CreateRevision(
        string quantity = "1",
        string purchaseUnitPrice = "100.00",
        string sellingUnitPrice = "150.00",
        string discount1Percentage = "10.00",
        string discount2Percentage = "5.00") =>
        JsonSerializer.Serialize(new
        {
            status = "AWAITING_USER_REVIEW",
            geniusWritePerformed = false,
            selectedLocalVendorReference = "local-vendor:test",
            vendorCandidates = new[]
            {
                new { localVendorReference = "local-vendor:test" }
            },
            sourceLines = new[]
            {
                new
                {
                    selectedLocalItemReference = "local-item:test",
                    localCandidates = new[]
                    {
                        new
                        {
                            localItemReference = "local-item:test",
                            hardMismatches = Array.Empty<string>()
                        }
                    },
                    postingLines = new[]
                    {
                        new
                        {
                            quantity,
                            expiryDate = "2028-06-30",
                            commercialValues = new
                            {
                                currency = "EGP",
                                purchaseUnit = "BOX",
                                purchaseUnitPrice,
                                purchasePriceTaxTreatment = "EXCLUSIVE",
                                discounts = new object[]
                                {
                                    new
                                    {
                                        sequence = 1,
                                        kind = "PERCENTAGE",
                                        percentage = discount1Percentage,
                                        applicationBasis = "PURCHASE_UNIT_PRICE",
                                        affectsPurchaseUnitPrice = true
                                    },
                                    new
                                    {
                                        sequence = 2,
                                        kind = "PERCENTAGE",
                                        percentage = discount2Percentage,
                                        applicationBasis = "REMAINING_LINE_SUBTOTAL",
                                        affectsPurchaseUnitPrice = false
                                    }
                                },
                                sellingUnit = "BOX",
                                sellingUnitPrice,
                                sellingPriceTaxTreatment = "INCLUSIVE",
                                sellingPriceScope = "NEW_STOCK_ONLY",
                                existingStockPriceBehavior = "PRESERVE",
                                unsupportedScopeBehavior = "BLOCK_COMMIT"
                            }
                        }
                    }
                }
            }
        });

    private static CommercialValuesContract CreateCommercialContract(
        string purchaseUnitPrice,
        string discount1Percentage,
        string discount2Percentage,
        string sellingUnitPrice) =>
        new(
            "EGP",
            "BOX",
            purchaseUnitPrice,
            "EXCLUSIVE",
            [
                new(1, "PERCENTAGE", discount1Percentage, "PURCHASE_UNIT_PRICE", true),
                new(2, "PERCENTAGE", discount2Percentage, "REMAINING_LINE_SUBTOTAL", false)
            ],
            "BOX",
            sellingUnitPrice,
            "INCLUSIVE",
            "NEW_STOCK_ONLY",
            "PRESERVE",
            "BLOCK_COMMIT");
}
