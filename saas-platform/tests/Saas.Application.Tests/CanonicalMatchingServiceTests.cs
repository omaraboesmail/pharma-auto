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

    [Fact]
    public async Task Search_RanksSemanticOnlyCandidatesBySimilarity()
    {
        var tenantId = Guid.NewGuid();
        var connectorId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);
        var lowerScore = Product("A LOWER SCORE", [0.8f, 0.6f]);
        var higherScore = Product("Z HIGHER SCORE", [1f, 0f]);
        var service = new CanonicalMatchingService(
            Store(tenantId, connectorId, now, lowerScore, higherScore),
            new FixedEmbeddingProvider());

        var results = await service.SearchAsync(
            tenantId,
            Query(),
            CancellationToken.None);

        Assert.Equal(
            [higherScore.CanonicalProductId, lowerScore.CanonicalProductId],
            results.Select(candidate => candidate.CanonicalProductId));
    }

    [Fact]
    public async Task Search_UsesTheProductionSemanticThresholdInMemory()
    {
        var tenantId = Guid.NewGuid();
        var connectorId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);
        var belowThreshold = Product("BELOW THRESHOLD", [0.5f, 0.8660254f]);
        var service = new CanonicalMatchingService(
            Store(tenantId, connectorId, now, belowThreshold),
            new FixedEmbeddingProvider());

        var results = await service.SearchAsync(
            tenantId,
            Query(),
            CancellationToken.None);

        Assert.Empty(results);
    }

    private static CanonicalProduct Product(string displayName, float[] embedding) => new(
        Guid.NewGuid(),
        displayName,
        [],
        [],
        new PharmaAttributes(null, null, null, null, null),
        "test-vector:2",
        embedding);

    private static InMemorySaasStore Store(
        Guid tenantId,
        Guid connectorId,
        DateTimeOffset now,
        params CanonicalProduct[] products) => new(
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
            products));

    private static CanonicalSearchQuery Query() => new(
        "COMPLETELY UNRELATED QUERY",
        null,
        new PharmaAttributes(null, null, null, null, null),
        "en",
        5);

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
