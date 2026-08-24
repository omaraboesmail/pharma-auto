using PharmaAuto.Saas.Application;
using PharmaAuto.Saas.Domain;
using PharmaAuto.Saas.Infrastructure;

namespace PharmaAuto.Saas.Application.Tests;

public sealed class CanonicalMatchingServiceTests
{
    [Fact]
    public async Task Search_PreservesVectorOnlyCandidatesWithAnExplicitReason()
    {
        var tenantId = Guid.NewGuid();
        var connectorId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);
        var product = new CanonicalProduct(
            Guid.NewGuid(),
            "SYNTHETIC PRODUCT ALPHA",
            ["ALPHA ONLY"],
            [],
            new PharmaAttributes(null, null, null, null, null),
            "test-vector:2",
            [1f, 0f]);
        var store = new InMemorySaasStore(
            new InMemorySaasSeed(
                new ConnectorRegistration(connectorId, tenantId, "Test", null, false),
                new SubscriptionEntitlement(
                    Guid.NewGuid(),
                    tenantId,
                    connectorId,
                    SubscriptionStatus.Active,
                    now.AddDays(-1),
                    now.AddDays(1),
                    now.AddDays(-1),
                    now.AddDays(1),
                    10,
                    0,
                    0,
                    true),
                [product]));
        var service = new CanonicalMatchingService(store, new FixedEmbeddingProvider());

        var results = await service.SearchAsync(
            tenantId,
            new CanonicalSearchQuery(
                "COMPLETELY UNRELATED QUERY",
                null,
                new PharmaAttributes(null, null, null, null, null),
                "en",
                5),
            CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(product.CanonicalProductId, result.CanonicalProductId);
        Assert.Contains("SEMANTIC_RETRIEVAL", result.ReasonCodes);
        Assert.Contains("CANONICAL_SHORTLIST", result.ReasonCodes);
    }

    private sealed class FixedEmbeddingProvider : IEmbeddingProvider
    {
        public string Version => "test-vector:2";

        public Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = text;
            return Task.FromResult<float[]?>([1f, 0f]);
        }
    }
}
