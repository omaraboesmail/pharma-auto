using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PharmaAuto.Connector.Application;
using PharmaAuto.Connector.Domain;

namespace PharmaAuto.Connector.LocalApi.Tests;

public sealed class CommercialEditPreviewEndpointTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    private readonly WebApplicationFactory<Program> factory;

    public CommercialEditPreviewEndpointTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
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
        await AuthenticateAsync();

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

    private async Task AuthenticateAsync()
    {
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var scope = factory.Services.CreateScope();
        var pairing = scope.ServiceProvider.GetRequiredService<PairingService>();
        var bootstrap = await pairing.CreateSessionAsync(CancellationToken.None);

        using var claimResponse = await client.PostAsJsonAsync(
            "/api/v1/pairing/claim",
            new
            {
                bootstrap.SessionId,
                bootstrap.OneTimeSecret,
                deviceDisplayName = "CI commercial-preview client",
                publicKeySubjectPublicKeyInfoBase64 = Convert.ToBase64String(
                    deviceKey.ExportSubjectPublicKeyInfo())
            });
        claimResponse.EnsureSuccessStatusCode();
        var claim = await claimResponse.Content.ReadFromJsonAsync<PairingClaimResult>()
            ?? throw new InvalidOperationException("Pairing claim response was empty.");

        using var challengeResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/challenges",
            new { claim.DeviceId });
        challengeResponse.EnsureSuccessStatusCode();
        var challenge = await challengeResponse.Content.ReadFromJsonAsync<AccessChallenge>()
            ?? throw new InvalidOperationException("Authentication challenge response was empty.");
        var canonical = string.Join(
            '\n',
            "PHARMA_AUTO_DEVICE_AUTH_V1",
            challenge.ChallengeId.ToString("D"),
            challenge.Nonce,
            claim.ConnectorId.ToString("D"),
            claim.DeviceId.ToString("D"));
        var signature = deviceKey.SignData(
            Encoding.UTF8.GetBytes(canonical),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        using var tokenResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/tokens",
            new
            {
                claim.DeviceId,
                challenge.ChallengeId,
                signatureBase64 = Convert.ToBase64String(signature)
            });
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<AccessTokenResult>()
            ?? throw new InvalidOperationException("Authentication token response was empty.");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);
    }

    private sealed record HealthContract(string Status, bool GeniusWritesEnabled);

    private sealed record PreviewContract(
        string PurchaseUnitPriceAfterDiscount1,
        string NetLineSubtotalAfterDiscount2,
        string SellingPriceScope,
        string ExistingStockPriceBehavior,
        bool GeniusWritePerformed);
}
