using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using PharmaAuto.Saas.Api;
using PharmaAuto.Saas.Application;
using PharmaAuto.Saas.Domain;
using PharmaAuto.Saas.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.WriteIndented = false;
});
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);

var development = builder.Environment.IsDevelopment();
var storageProvider = builder.Configuration["Storage:Provider"]
    ?? (development ? "Memory" : "Postgres");
var ocrProviderName = builder.Configuration["Ocr:Provider"]
    ?? (development ? "Fixture" : "Gemini");
var geminiApiKey = builder.Configuration["Gemini:ApiKey"];

if (!development && string.Equals(storageProvider, "Memory", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("In-memory SaaS storage is forbidden outside Development.");
}
if (!development && string.Equals(ocrProviderName, "Fixture", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("Synthetic OCR fixture replay is forbidden outside Development.");
}

var seed = LoadSeed(builder.Environment.ContentRootPath);
if (string.Equals(storageProvider, "Memory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<ISaasStore>(new InMemorySaasStore(seed));
}
else if (string.Equals(storageProvider, "Postgres", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration.GetConnectionString("SaasPostgres");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("SaaS PostgreSQL connection string is required.");
    }
    builder.Services.AddSingleton<ISaasStore>(new PostgresSaasStore(connectionString));
}
else
{
    throw new InvalidOperationException($"Unsupported SaaS storage provider: {storageProvider}");
}

if (string.Equals(ocrProviderName, "Fixture", StringComparison.OrdinalIgnoreCase))
{
    var fixtureDirectory = builder.Configuration["Ocr:FixtureDirectory"]
        ?? Path.GetFullPath(
            Path.Combine(
                builder.Environment.ContentRootPath,
                "..",
                "..",
                "..",
                "test-data",
                "phase-0",
                "expected"));
    builder.Services.AddSingleton<IOcrProvider>(services =>
        new FixtureOcrProvider(
            fixtureDirectory,
            services.GetRequiredService<TimeProvider>()));
}
else if (string.Equals(ocrProviderName, "Gemini", StringComparison.OrdinalIgnoreCase))
{
    if (string.IsNullOrWhiteSpace(geminiApiKey))
    {
        throw new InvalidOperationException(
            "Gemini API key must come from the SaaS secret provider for live OCR.");
    }
    var model = builder.Configuration["Gemini:Model"] ?? "gemini-3.6-flash";
    var endpoint = new Uri(
        builder.Configuration["Gemini:Endpoint"]
            ?? "https://generativelanguage.googleapis.com/v1beta/interactions");
    var apiRevision = builder.Configuration["Gemini:ApiRevision"] ?? "2026-05-20";
    builder.Services.AddSingleton<IOcrProvider>(services =>
        new GeminiInteractionsOcrProvider(
            new HttpClient { Timeout = TimeSpan.FromSeconds(120) },
            new GeminiOcrOptions(geminiApiKey, model, endpoint, apiRevision),
            services.GetRequiredService<TimeProvider>()));
}
else
{
    throw new InvalidOperationException($"Unsupported OCR provider: {ocrProviderName}");
}

if (development)
{
    builder.Services.AddSingleton<IEmbeddingProvider, NullEmbeddingProvider>();
}
else
{
    var embeddingModel = builder.Configuration["Gemini:EmbeddingModel"]
        ?? "gemini-embedding-2";
    var embeddingEndpoint = new Uri(
        builder.Configuration["Gemini:EmbeddingEndpoint"]
            ?? $"https://generativelanguage.googleapis.com/v1beta/models/{embeddingModel}:embedContent");
    builder.Services.AddSingleton<IEmbeddingProvider>(services =>
        new GeminiEmbeddingProvider(
            new HttpClient { Timeout = TimeSpan.FromSeconds(20) },
            new GeminiEmbeddingOptions(
                geminiApiKey!,
                embeddingModel,
                embeddingEndpoint,
                768),
            services.GetRequiredService<ILogger<GeminiEmbeddingProvider>>()));
}
builder.Services.AddSingleton<OcrOrchestrator>();
builder.Services.AddSingleton<CanonicalMatchingService>();

var signingKeyPem = builder.Configuration["EntitlementSigning:PrivateKeyPem"];
if (!development && string.IsNullOrWhiteSpace(signingKeyPem))
{
    throw new InvalidOperationException(
        "A KMS-injected ES256 entitlement signing key is required outside Development.");
}
var signer = new EcdsaEntitlementSigner(
    builder.Configuration["EntitlementSigning:KeyId"] ?? "phase1-development-ephemeral",
    signingKeyPem);
builder.Services.AddSingleton(signer);
builder.Services.AddSingleton<IEntitlementSigner>(signer);

var sharedSecret = ReadConnectorSecret(
    builder.Configuration["ConnectorAuth:SharedSecret"],
    development);
var requireClientCertificate = builder.Configuration.GetValue(
    "ConnectorAuth:RequireClientCertificate",
    !development);
if (!development && !requireClientCertificate)
{
    throw new InvalidOperationException("Connector mTLS is required outside Development.");
}
builder.Services.AddSingleton(
    new ConnectorAuthenticationOptions(
        sharedSecret,
        requireClientCertificate,
        TimeSpan.FromMinutes(5),
        80L * 1024L * 1024L));

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 80L * 1024L * 1024L;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    if (requireClientCertificate)
    {
        options.ConfigureHttpsDefaults(https =>
        {
            https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
            https.CheckCertificateRevocation = true;
        });
    }
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseMiddleware<ConnectorAuthenticationMiddleware>();

app.MapGet(
    "/health/live",
    (IOcrProvider provider) => Results.Ok(new
    {
        status = "ok",
        service = "PharmaAuto.Saas.Api",
        storageProvider,
        ocrProvider = provider.ProviderName,
        fixtureMode = string.Equals(
            ocrProviderName,
            "Fixture",
            StringComparison.OrdinalIgnoreCase),
        geniusWritesEnabled = false
    }));

app.MapGet(
    "/api/v1/entitlements/current",
    async (
        HttpContext context,
        ISaasStore store,
        IEntitlementSigner entitlementSigner,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
    {
        var connector = context.Connector();
        var entitlement = await store.GetEntitlementAsync(
            connector.TenantId,
            connector.ConnectorId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (entitlement is null)
        {
            return Results.Problem(
                title: "Entitlement not found",
                statusCode: StatusCodes.Status403Forbidden,
                type: "https://pharma-auto.invalid/problems/entitlement");
        }

        var unsigned = MapEntitlement(entitlement, entitlementSigner, string.Empty);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            unsigned,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var response = unsigned with { Signature = entitlementSigner.Sign(payload) };
        return Results.Ok(response);
    });

app.MapGet(
    "/api/v1/signing-keys/current",
    (EcdsaEntitlementSigner entitlementSigner) => Results.Ok(new
    {
        algorithm = entitlementSigner.Algorithm,
        keyId = entitlementSigner.KeyId,
        subjectPublicKeyInfoPem = entitlementSigner.ExportPublicKeyPem()
    }));

app.MapPost(
    "/api/v1/ocr/jobs/{jobId:guid}/process",
    async (
        Guid jobId,
        ProcessOcrRequest request,
        HttpContext context,
        OcrOrchestrator orchestrator,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var pages = DecodePages(request.Pages);
            var document = new OcrDocument(jobId, request.SourceSha256, pages);
            var connector = context.Connector();
            var job = await orchestrator.ProcessAsync(
                connector.TenantId,
                connector.ConnectorId,
                document,
                cancellationToken);
            return Results.Ok(MapJob(job));
        }
        catch (FormatException)
        {
            return Results.Problem(
                title: "Invalid document encoding",
                detail: "A page is not valid base64 data.",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://pharma-auto.invalid/problems/document-validation");
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(
                title: "Invalid OCR document",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://pharma-auto.invalid/problems/document-validation");
        }
        catch (QuotaExceededException exception)
        {
            return Results.Problem(
                title: "OCR quota exhausted",
                detail: exception.Message,
                statusCode: StatusCodes.Status429TooManyRequests,
                type: "https://pharma-auto.invalid/problems/quota");
        }
        catch (EntitlementRejectedException exception)
        {
            return Results.Problem(
                title: "OCR entitlement rejected",
                detail: exception.Message,
                statusCode: StatusCodes.Status403Forbidden,
                type: "https://pharma-auto.invalid/problems/entitlement");
        }
        catch (OcrProviderException exception)
        {
            return Results.Problem(
                title: "OCR provider failed",
                detail: exception.Code,
                statusCode: StatusCodes.Status502BadGateway,
                type: "https://pharma-auto.invalid/problems/ocr-provider");
        }
    });

app.MapGet(
    "/api/v1/ocr/jobs/{jobId:guid}",
    async (
        Guid jobId,
        HttpContext context,
        ISaasStore store,
        CancellationToken cancellationToken) =>
    {
        var connector = context.Connector();
        var job = await store.GetOcrJobAsync(
            connector.TenantId,
            jobId,
            cancellationToken);
        return job is null ? Results.NotFound() : Results.Ok(MapJob(job));
    });

app.MapPost(
    "/api/v1/canonical-products/search",
    async (
        CanonicalSearchRequest request,
        HttpContext context,
        CanonicalMatchingService matching,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 2000)
        {
            return Results.Problem(
                title: "Invalid canonical search",
                detail: "description is required and must not exceed 2000 characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var attributes = request.Attributes;
        var query = new CanonicalSearchQuery(
            request.Description,
            request.VendorItemCode,
            new PharmaAttributes(
                attributes?.ActiveIngredient,
                attributes?.Strength,
                attributes?.DosageForm,
                attributes?.Pack,
                attributes?.Manufacturer),
            request.Locale,
            request.Limit);
        try
        {
            var candidates = await matching.SearchAsync(
                context.Connector().TenantId,
                query,
                cancellationToken);
            return Results.Ok(new
            {
                candidates,
                localItemIdentitySelected = false,
                requiresLocalResolution = true,
                geniusWritePerformed = false
            });
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Results.Problem(
                title: "Invalid canonical search",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    });

app.Run();

static InMemorySaasSeed LoadSeed(string contentRoot)
{
    var tenantId = Guid.Parse("721b6dde-538e-4f33-a10a-c44d7d724111");
    var connectorId = Guid.Parse("721b6dde-538e-4f33-a10a-c44d7d724222");
    var catalogPath = Path.Combine(contentRoot, "data", "canonical-products.v1.json");
    var json = File.ReadAllText(catalogPath);
    var products = JsonSerializer.Deserialize<List<CanonicalProductSeed>>(
        json,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        })
        ?? throw new InvalidOperationException("Canonical product seed is invalid.");
    if (products.Any(product =>
            product.CanonicalProductId == Guid.Empty ||
            string.IsNullOrWhiteSpace(product.DisplayName) ||
            product.Aliases is null ||
            product.Identifiers is null ||
            product.Attributes is null))
    {
        throw new InvalidOperationException("Canonical product seed contains an incomplete record.");
    }
    return new InMemorySaasSeed(
        new ConnectorRegistration(
            connectorId,
            tenantId,
            "Phase 1 Local Connector",
            null,
            false),
        new SubscriptionEntitlement(
            Guid.Parse("721b6dde-538e-4f33-a10a-c44d7d724333"),
            tenantId,
            connectorId,
            SubscriptionStatus.Active,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            500,
            0,
            0,
            true),
        products.Select(product => new CanonicalProduct(
            product.CanonicalProductId,
            product.DisplayName,
            product.Aliases,
            product.Identifiers,
            product.Attributes,
            product.EmbeddingVersion,
            null)).ToArray());
}

static byte[] ReadConnectorSecret(string? configuredSecret, bool development)
{
    if (string.IsNullOrWhiteSpace(configuredSecret))
    {
        if (!development)
        {
            throw new InvalidOperationException(
                "A connector request-signing secret is required outside Development.");
        }
        return SHA256.HashData("PHARMA_AUTO_PHASE1_SYNTHETIC_ONLY"u8);
    }

    var bytes = Convert.FromBase64String(configuredSecret);
    if (bytes.Length < 32)
    {
        throw new InvalidOperationException("Connector request-signing secret must be at least 256 bits.");
    }
    return bytes;
}

static IReadOnlyList<OcrDocumentPage> DecodePages(IReadOnlyList<OcrPageRequest> requests)
{
    if (requests.Count is < 1 or > 100)
    {
        throw new ArgumentException("pages must contain between 1 and 100 entries.");
    }

    long total = 0;
    return requests.Select(page =>
    {
        var bytes = Convert.FromBase64String(page.Base64Data);
        total += bytes.Length;
        if (bytes.Length > 20L * 1024L * 1024L || total > 50L * 1024L * 1024L)
        {
            throw new ArgumentException("Document exceeds the Phase 1 inline page or document limit.");
        }
        return new OcrDocumentPage(page.Page, page.MimeType, page.Sha256, bytes);
    }).ToArray();
}

static OcrJobResponse MapJob(OcrJob job) => new(
    job.JobId,
    job.State.ToString().ToUpperInvariant(),
    job.PageCount,
    job.SourceSha256,
    job.ProviderModel,
    job.FailureCode,
    string.IsNullOrWhiteSpace(job.ResultJson) ? null : JsonNode.Parse(job.ResultJson),
    job.CreatedAt,
    job.UpdatedAt,
    false);

static SignedEntitlementResponse MapEntitlement(
    SubscriptionEntitlement entitlement,
    IEntitlementSigner signer,
    string signature) => new(
    "1.0",
    entitlement.EntitlementId,
    entitlement.TenantId,
    entitlement.ConnectorId,
    entitlement.Status.ToString().ToUpperInvariant(),
    entitlement.ValidFrom,
    entitlement.ValidUntil,
    entitlement.PeriodStart,
    entitlement.PeriodEnd,
    entitlement.PageLimit,
    entitlement.PagesReserved,
    entitlement.PagesSettled,
    entitlement.OfflineReviewAllowed,
    false,
    signer.Algorithm,
    signer.KeyId,
    signature);

public sealed record CanonicalProductSeed(
    Guid CanonicalProductId,
    string DisplayName,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Identifiers,
    PharmaAttributes Attributes,
    string EmbeddingVersion);

public partial class Program;
