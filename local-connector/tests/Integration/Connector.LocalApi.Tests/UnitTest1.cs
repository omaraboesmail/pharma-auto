using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PharmaAuto.Connector.LocalApi.Tests;

public sealed class CommercialEditPreviewEndpointTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public CommercialEditPreviewEndpointTests(WebApplicationFactory<Program> factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task Liveness_ReportsThatGeniusWritesAreDisabled()
    {
        var response = await client.GetFromJsonAsync<HealthContract>("/health/live");

        Assert.NotNull(response);
        Assert.Equal("ok", response.Status);
        Assert.False(response.GeniusWritesEnabled);
    }

    [Fact]
    public async Task Preview_ReturnsTheApprovedCalculationWithoutWritingToGenius()
    {
        var revisionId = Guid.Parse("018f47a0-7b6c-7c32-8d52-9b880b2d6323");
        var postingLineId = Guid.Parse("018f47a0-7b6c-7c32-8d52-9b880b2d6331");
        var request = new
        {
            expectedRevisionId = revisionId,
            quantity = "2",
            commercialValues = new
            {
                currency = "EGP",
                purchaseUnit = "BOX",
                purchaseUnitPrice = "100.00",
                purchasePriceTaxTreatment = "EXCLUSIVE",
                discounts = new object[]
                {
                    new
                    {
                        sequence = 1,
                        kind = "PERCENTAGE",
                        percentage = "10.00",
                        applicationBasis = "PURCHASE_UNIT_PRICE",
                        affectsPurchaseUnitPrice = true
                    },
                    new
                    {
                        sequence = 2,
                        kind = "PERCENTAGE",
                        percentage = "5.00",
                        applicationBasis = "REMAINING_LINE_SUBTOTAL",
                        affectsPurchaseUnitPrice = false
                    }
                },
                sellingUnit = "BOX",
                sellingUnitPrice = "150.00",
                sellingPriceTaxTreatment = "INCLUSIVE",
                sellingPriceScope = "NEW_STOCK_ONLY",
                existingStockPriceBehavior = "PRESERVE",
                unsupportedScopeBehavior = "BLOCK_COMMIT"
            }
        };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/invoice-revisions/{revisionId}/posting-lines/{postingLineId}/commercial-edit-preview",
            request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PreviewContract>();
        Assert.NotNull(body);
        Assert.Equal("90", body.PurchaseUnitPriceAfterDiscount1);
        Assert.Equal("171", body.NetLineSubtotalAfterDiscount2);
        Assert.Equal("NEW_STOCK_ONLY", body.SellingPriceScope);
        Assert.Equal("PRESERVE", body.ExistingStockPriceBehavior);
        Assert.False(body.GeniusWritePerformed);
    }

    private sealed record HealthContract(string Status, bool GeniusWritesEnabled);

    private sealed record PreviewContract(
        string PurchaseUnitPriceAfterDiscount1,
        string NetLineSubtotalAfterDiscount2,
        string SellingPriceScope,
        string ExistingStockPriceBehavior,
        bool GeniusWritePerformed);
}
