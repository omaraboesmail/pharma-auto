using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using PharmaAuto.Connector.Application;
using PharmaAuto.Connector.Domain;
using PharmaAuto.Connector.Infrastructure;
using PharmaAuto.Connector.LocalApi;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "Pharma Auto Connector");
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.WriteIndented = false;
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
});
builder.Services.AddSingleton(TimeProvider.System);

var development = builder.Environment.IsDevelopment();
var repositoryRoot = Path.GetFullPath(
    Path.Combine(builder.Environment.ContentRootPath, "..", "..", ".."));
var dataRoot = builder.Configuration["Connector:DataRoot"]
    ?? (development
        ? Path.Combine(repositoryRoot, ".local-runtime", "connector")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PharmaAuto",
            "Connector"));
dataRoot = Path.GetFullPath(dataRoot);
Directory.CreateDirectory(dataRoot);
builder.Configuration.AddJsonFile(
    Path.Combine(dataRoot, "connector-settings.json"),
    optional: true,
    reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);

var keyStore = new WindowsMachineKeyStore(Path.Combine(dataRoot, "keys"));
var protectedSecrets = new WindowsProtectedSecretStore(Path.Combine(dataRoot, "secrets"));
var certificateProvider = new ConnectorCertificateProvider();
var certificateThumbprint = builder.Configuration["Connector:TlsCertificateThumbprint"];
var certificate = string.IsNullOrWhiteSpace(certificateThumbprint)
    ? development
        ? certificateProvider.LoadOrCreateDevelopment(
            Path.Combine(dataRoot, "tls", "connector-development.pfx"),
            keyStore.GetBase64Url("development-pfx-password", 32))
        : throw new InvalidOperationException(
            "A LocalMachine Connector TLS certificate thumbprint is required outside Development.")
    : certificateProvider.LoadByThumbprint(certificateThumbprint);
var certificateSha256 = ConnectorCertificateProvider.Sha256(certificate);
var port = builder.Configuration.GetValue("Connector:Port", 7443);
var publicBaseUrl = builder.Configuration["Connector:PublicBaseUrl"]
    ?? $"https://127.0.0.1:{port}";
if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var publicBaseUri) ||
    publicBaseUri.Scheme != Uri.UriSchemeHttps)
{
    throw new InvalidOperationException("Connector PublicBaseUrl must be an absolute HTTPS URL.");
}

var connector = new ConnectorIdentity(
    Guid.Parse(
        builder.Configuration["Connector:ConnectorId"]
            ?? "721b6dde-538e-4f33-a10a-c44d7d724222"),
    Guid.Parse(
        builder.Configuration["Connector:TenantId"]
            ?? "721b6dde-538e-4f33-a10a-c44d7d724111"),
    builder.Configuration["Connector:PharmacyDisplayName"]
        ?? "Pharma Auto Phase 1 Pharmacy",
    publicBaseUri.ToString().TrimEnd('/'),
    certificateSha256,
    builder.Configuration["Genius:ProfileId"] ?? "EPLUS_GENIUS_DB539_PROFILE_1");

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 5L * 1024L * 1024L;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    void ConfigureHttps(ListenOptions listen)
    {
        listen.Protocols = HttpProtocols.Http1AndHttp2;
        listen.UseHttps(certificate);
    }

    if (development && System.Net.IPAddress.IsLoopback(
            System.Net.Dns.GetHostAddresses(publicBaseUri.Host).First()))
    {
        options.ListenLocalhost(port, ConfigureHttps);
    }
    else
    {
        options.ListenAnyIP(port, ConfigureHttps);
    }
});

var sidecar = new SqliteSidecarStore(Path.Combine(dataRoot, "sidecar", "connector.db"));
var objectStore = new EncryptedDocumentObjectStore(
    Path.Combine(dataRoot, "documents"),
    Path.Combine(dataRoot, "keys", "document-master.dpapi"));
var controlKey = keyStore.GetBase64Url("control-access", 32);
var tokenSigningKey = keyStore.GetOrCreate("device-token-signing", 32);
var localIdentityKey = keyStore.GetOrCreate("local-identity", 32);

var geniusConnectionString = builder.Configuration.GetConnectionString("GeniusReadOnly")
    ?? protectedSecrets.TryRead("genius-readonly-connection")
    ?? (development
        ? "Server=localhost\\SQL2008R2;Database=Genius_Legacy;Integrated Security=true"
        : throw new InvalidOperationException(
            "The DPAPI-injected Genius read-only connection string is required outside Development."));

var saasBaseUrl = new Uri(
    builder.Configuration["Saas:BaseUrl"] ?? "http://127.0.0.1:7081");
if (!development && saasBaseUrl.Scheme != Uri.UriSchemeHttps)
{
    throw new InvalidOperationException("SaaS BaseUrl must use HTTPS outside Development.");
}
var saasSigningSecret = ReadSecret(
    builder.Configuration["Saas:RequestSigningSecret"]
        ?? protectedSecrets.TryRead("saas-request-signing-secret"),
    development);
var saasHandler = new HttpClientHandler();
var saasClientCertificateThumbprint =
    builder.Configuration["Saas:ClientCertificateThumbprint"];
if (!string.IsNullOrWhiteSpace(saasClientCertificateThumbprint))
{
    saasHandler.ClientCertificates.Add(
        certificateProvider.LoadByThumbprint(saasClientCertificateThumbprint));
}
else if (!development)
{
    throw new InvalidOperationException(
        "A LocalMachine SaaS mTLS client certificate thumbprint is required outside Development.");
}
var saasHttpClient = new HttpClient(saasHandler, disposeHandler: true)
{
    Timeout = TimeSpan.FromMinutes(3)
};

var defenderPath = builder.Configuration["Documents:DefenderExecutable"]
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "Windows Defender",
        "MpCmdRun.exe");

builder.Services.AddSingleton(connector);
builder.Services.AddSingleton(keyStore);
builder.Services.AddSingleton<ISidecarStore>(sidecar);
builder.Services.AddSingleton<IDocumentObjectStore>(objectStore);
builder.Services.AddSingleton<IFileSafetyInspector>(
    new WindowsFileSafetyInspector(
        defenderPath,
        Path.Combine(dataRoot, "scan"),
        builder.Configuration.GetValue("Documents:RequireDefender", !development)));
builder.Services.AddSingleton<IGeniusCatalogReader>(
    new SqlGeniusCatalogReader(geniusConnectionString));
builder.Services.AddSingleton<ISaasClient>(
    new SaasClient(
        saasHttpClient,
        new SaasClientOptions(
            saasBaseUrl,
            connector.TenantId,
            connector.ConnectorId,
            saasSigningSecret),
        TimeProvider.System));
builder.Services.AddSingleton<IInvoiceWorkflowQueue, InvoiceWorkflowQueue>();
builder.Services.AddSingleton<PairingService>();
builder.Services.AddSingleton(services =>
    new DeviceAuthenticationService(
        services.GetRequiredService<ISidecarStore>(),
        connector,
        tokenSigningKey,
        services.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(services =>
    new CatalogProjectionService(
        services.GetRequiredService<IGeniusCatalogReader>(),
        services.GetRequiredService<ISidecarStore>(),
        connector,
        localIdentityKey,
        services.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<CatalogSearchService>();
builder.Services.AddSingleton<InvoiceWorkflowService>();
builder.Services.AddSingleton<CommercialEditPreviewService>();
builder.Services.AddSingleton(new ControlAuthorizationOptions(controlKey));
builder.Services.AddHostedService<SidecarInitializationService>();
builder.Services.AddHostedService<InvoiceWorkflowWorker>();
builder.Services.AddHostedService<DocumentRetentionWorker>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseMiddleware<LocalAuthorizationMiddleware>();

app.MapGet(
    "/health/live",
    () => TypedResults.Ok(new
    {
        status = "ok",
        service = "PharmaAuto.Connector.LocalApi",
        connectorId = connector.ConnectorId,
        pharmacyDisplayName = connector.PharmacyDisplayName,
        apiVersion = "1.0",
        geniusWritesEnabled = false
    }));

app.MapGet(
    "/control/v1/health",
    async (ISidecarStore store, CancellationToken cancellationToken) =>
    {
        var catalog = await store.GetCatalogProjectionSummaryAsync(cancellationToken);
        var jobs = await store.ListJobsAsync(100, cancellationToken);
        return Results.Ok(new
        {
            connectorId = connector.ConnectorId,
            connector.PharmacyDisplayName,
            connector.BaseUrl,
            connector.CertificateSha256,
            connector.DatabaseProfileId,
            catalog,
            queueDepth = jobs.Count(job => job.State is not (
                InvoiceJobState.Confirmed or InvoiceJobState.Rejected)),
            oldestPendingJob = jobs
                .Where(job => job.State is not (InvoiceJobState.Confirmed or InvoiceJobState.Rejected))
                .OrderBy(job => job.CreatedAt)
                .Select(job => job.CreatedAt)
                .Cast<DateTimeOffset?>()
                .FirstOrDefault(),
            geniusWritesEnabled = false
        });
    });

app.MapPost(
    "/control/v1/pairing-sessions",
    async (PairingService pairing, CancellationToken cancellationToken) =>
        Results.Ok(await pairing.CreateSessionAsync(cancellationToken)));

app.MapGet(
    "/control/v1/devices",
    async (ISidecarStore store, CancellationToken cancellationToken) =>
        Results.Ok(await store.ListDevicesAsync(cancellationToken)));

app.MapPost(
    "/control/v1/devices/{deviceId:guid}/revoke",
    async (
        Guid deviceId,
        ISidecarStore store,
        TimeProvider timeProvider,
        CancellationToken cancellationToken) =>
    {
        var now = timeProvider.GetUtcNow();
        var revoked = await store.RevokeDeviceAsync(deviceId, now, cancellationToken);
        if (revoked)
        {
            await store.AppendAuditAsync(
                new AuditRecord(
                    Guid.NewGuid(),
                    "LOCAL_TECHNICIAN",
                    "CONTROL_UI",
                    "DEVICE_REVOKED",
                    deviceId.ToString("D"),
                    "SUCCESS",
                    Guid.NewGuid(),
                    now),
                cancellationToken);
        }
        return revoked ? Results.NoContent() : Results.NotFound();
    });

app.MapPost(
    "/control/v1/catalog/rebuild",
    async (CatalogProjectionService projection, CancellationToken cancellationToken) =>
        Results.Ok(await projection.RebuildAsync(cancellationToken)));

app.MapGet(
    "/control/v1/jobs",
    async (ISidecarStore store, CancellationToken cancellationToken) =>
        Results.Ok((await store.ListJobsAsync(250, cancellationToken)).Select(MapJob)));

app.MapPost(
    "/api/v1/pairing/claim",
    async (
        PairingClaimRequest request,
        PairingService pairing,
        CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await pairing.ClaimAsync(
                request.SessionId,
                request.OneTimeSecret,
                request.DeviceDisplayName,
                request.PublicKeySubjectPublicKeyInfoBase64,
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(
                title: "Invalid pairing claim",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(
                title: "Pairing claim rejected",
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
    });

app.MapPost(
    "/api/v1/auth/challenges",
    async (
        CreateChallengeRequest request,
        DeviceAuthenticationService authentication,
        CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await authentication.CreateChallengeAsync(
                request.DeviceId,
                cancellationToken));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Problem(
                title: "Device authentication rejected",
                detail: exception.Message,
                statusCode: StatusCodes.Status401Unauthorized);
        }
    });

app.MapPost(
    "/api/v1/auth/tokens",
    async (
        ExchangeTokenRequest request,
        DeviceAuthenticationService authentication,
        CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await authentication.ExchangeAsync(
                request.DeviceId,
                request.ChallengeId,
                request.SignatureBase64,
                cancellationToken));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Problem(
                title: "Device authentication rejected",
                detail: exception.Message,
                statusCode: StatusCodes.Status401Unauthorized);
        }
    });

app.MapGet(
    "/api/v1/catalog/items/search",
    async (
        string? query,
        string? vendorItemCode,
        string? barcode,
        string? itemCode,
        string? activeIngredient,
        string? strength,
        string? dosageForm,
        string? pack,
        int? limit,
        CatalogSearchService search,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var candidates = await search.SearchItemsAsync(
                new LocalMatchQuery(
                    query ?? string.Empty,
                    vendorItemCode,
                    barcode,
                    itemCode,
                    activeIngredient,
                    strength,
                    dosageForm,
                    pack,
                    limit ?? 25),
                cancellationToken);
            return Results.Ok(new
            {
                candidates,
                finalLocalIdentitySelected = false,
                geniusWritePerformed = false
            });
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Results.Problem(
                title: "Invalid catalog search",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    });

app.MapGet(
    "/api/v1/catalog/vendors/search",
    async (
        string query,
        int? limit,
        CatalogSearchService search,
        CancellationToken cancellationToken) =>
        Results.Ok(new
        {
            candidates = await search.SearchVendorsAsync(
                query,
                Math.Clamp(limit ?? 25, 1, 50),
                cancellationToken),
            finalLocalIdentitySelected = false,
            geniusWritePerformed = false
        }));

app.MapPost(
    "/api/v1/invoice-jobs",
    async (
        CreateInvoiceJobRequest request,
        HttpContext context,
        InvoiceWorkflowService workflow,
        CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Created(
                string.Empty,
                MapJob(await workflow.CreateJobAsync(
                    context.Device().DeviceId,
                    request.PageCount,
                    cancellationToken)));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Results.Problem(
                title: "Invalid invoice job",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    });

app.MapPut(
    "/api/v1/invoice-jobs/{jobId:guid}/pages/{page:int}/chunks/{chunkIndex:int}",
    async (
        Guid jobId,
        int page,
        int chunkIndex,
        HttpContext context,
        InvoiceWorkflowService workflow,
        CancellationToken cancellationToken) =>
    {
        if (context.Request.ContentLength is null or < 1 or > 4 * 1024 * 1024)
        {
            return Results.Problem(
                title: "Invalid upload chunk",
                detail: "Content-Length must be 1 B..4 MiB.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        if (!int.TryParse(context.Request.Headers["X-Chunk-Count"], out var chunkCount))
        {
            return Results.Problem(
                title: "Invalid upload chunk",
                detail: "X-Chunk-Count is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        await using var stream = new MemoryStream((int)context.Request.ContentLength.Value);
        await context.Request.Body.CopyToAsync(stream, cancellationToken);
        try
        {
            var status = await workflow.UploadChunkAsync(
                context.Device().DeviceId,
                jobId,
                page,
                chunkIndex,
                chunkCount,
                context.Request.Headers["X-Chunk-SHA256"].ToString(),
                context.Request.Headers["X-Page-SHA256"].ToString(),
                context.Request.Headers["X-Page-Mime-Type"].ToString(),
                stream.ToArray(),
                cancellationToken);
            return Results.Ok(status);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.Problem(
                title: "Upload chunk rejected",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    });

app.MapGet(
    "/api/v1/invoice-jobs/{jobId:guid}/upload-status",
    async (
        Guid jobId,
        HttpContext context,
        InvoiceWorkflowService workflow,
        CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await workflow.GetUploadStatusAsync(
                context.Device().DeviceId,
                jobId,
                cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
    });

app.MapPost(
    "/api/v1/invoice-jobs/{jobId:guid}/submit",
    async (
        Guid jobId,
        HttpContext context,
        InvoiceWorkflowService workflow,
        CancellationToken cancellationToken) =>
    {
        try
        {
            await workflow.SubmitAsync(context.Device().DeviceId, jobId, cancellationToken);
            return Results.Accepted($"/api/v1/invoice-jobs/{jobId:D}");
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(
                title: "Invoice submission rejected",
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
    });

app.MapGet(
    "/api/v1/invoice-jobs/{jobId:guid}",
    async (
        Guid jobId,
        HttpContext context,
        ISidecarStore store,
        CancellationToken cancellationToken) =>
    {
        var job = await store.GetJobAsync(jobId, cancellationToken);
        return job is null
            ? Results.NotFound()
            : job.DeviceId != context.Device().DeviceId
                ? Results.Forbid()
                : Results.Ok(MapJob(job));
    });

app.MapGet(
    "/api/v1/invoice-revisions/{revisionId:guid}",
    async (
        Guid revisionId,
        HttpContext context,
        ISidecarStore store,
        CancellationToken cancellationToken) =>
    {
        var revision = await store.GetRevisionAsync(revisionId, cancellationToken);
        if (revision is null)
        {
            return Results.NotFound();
        }
        var job = await store.GetJobAsync(revision.JobId, cancellationToken);
        return job?.DeviceId != context.Device().DeviceId
            ? Results.Forbid()
            : Results.Text(revision.Json, "application/json");
    });

app.MapPost(
    "/api/v1/invoice-revisions/{revisionId:guid}/edits",
    async (
        Guid revisionId,
        SaveRevisionRequest request,
        HttpContext context,
        InvoiceWorkflowService workflow,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var revision = await workflow.SaveEditedRevisionAsync(
                context.Device().DeviceId,
                revisionId,
                request.Revision.GetRawText(),
                request.Reason,
                cancellationToken);
            return Results.Created(
                $"/api/v1/invoice-revisions/{revision.RevisionId:D}",
                new
                {
                    revision.RevisionId,
                    revision.RevisionNumber,
                    revision.Status,
                    geniusWritePerformed = false
                });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.Problem(
                title: "Review revision rejected",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    });

app.MapPost(
    "/api/v1/invoice-revisions/{revisionId:guid}/confirm",
    async (
        Guid revisionId,
        HttpContext context,
        InvoiceWorkflowService workflow,
        CancellationToken cancellationToken) =>
    {
        try
        {
            await workflow.ConfirmRevisionAsync(
                context.Device().DeviceId,
                revisionId,
                cancellationToken);
            return Results.Ok(new
            {
                revisionId,
                state = "CONFIRMED",
                commitAvailable = false,
                geniusWritePerformed = false
            });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(
                title: "Review confirmation blocked",
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict);
        }
    });

app.MapPost(
    "/api/v1/invoice-revisions/{revisionId:guid}/posting-lines/{postingLineId:guid}/commercial-edit-preview",
    (
        Guid revisionId,
        Guid postingLineId,
        CommercialEditPreviewRequest request,
        CommercialEditPreviewService service) =>
    {
        try
        {
            return Results.Ok(service.Preview(revisionId, postingLineId, request));
        }
        catch (CommercialPreviewValidationException exception)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["commercialValues"] = [.. exception.Errors]
                },
                title: "Invalid commercial edit preview",
                statusCode: StatusCodes.Status400BadRequest);
        }
    });

app.Run();

static object MapJob(InvoiceJob job) => new
{
    schemaVersion = "1.0",
    job.JobId,
    state = State(job.State),
    job.DeviceId,
    pageCount = job.ExpectedPageCount,
    job.UploadedPageCount,
    job.CurrentRevisionId,
    job.CreatedAt,
    job.UpdatedAt,
    job.FailureCode,
    geniusWritePerformed = false
};

static string State(InvoiceJobState state) => state switch
{
    InvoiceJobState.Captured => "CAPTURED",
    InvoiceJobState.LocallyValidated => "LOCALLY_VALIDATED",
    InvoiceJobState.OcrReserved => "OCR_RESERVED",
    InvoiceJobState.OcrProcessing => "OCR_PROCESSING",
    InvoiceJobState.OcrValidated => "OCR_VALIDATED",
    InvoiceJobState.Matching => "MATCHING",
    InvoiceJobState.AwaitingUserReview => "AWAITING_USER_REVIEW",
    InvoiceJobState.Confirmed => "CONFIRMED",
    InvoiceJobState.Rejected => "REJECTED",
    InvoiceJobState.OcrFailed => "OCR_FAILED",
    InvoiceJobState.MatchingFailed => "MATCHING_FAILED",
    _ => throw new ArgumentOutOfRangeException(nameof(state))
};

static byte[] ReadSecret(string? configured, bool development)
{
    if (string.IsNullOrWhiteSpace(configured))
    {
        if (!development)
        {
            throw new InvalidOperationException(
                "A DPAPI-injected SaaS request-signing secret is required outside Development.");
        }
        return SHA256.HashData("PHARMA_AUTO_PHASE1_SYNTHETIC_ONLY"u8);
    }
    var secret = Convert.FromBase64String(configured);
    if (secret.Length < 32)
    {
        throw new InvalidOperationException("SaaS request-signing secret must be at least 256 bits.");
    }
    return secret;
}

public partial class Program;
