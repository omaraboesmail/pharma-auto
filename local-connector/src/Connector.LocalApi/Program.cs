using PharmaAuto.Connector.Application;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<CommercialEditPreviewService>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet(
        "/health/live",
        () => TypedResults.Ok(new HealthResponse(
            "ok",
            "PharmaAuto.Connector.LocalApi",
            false)))
    .WithName("getLiveness");

app.MapPost(
        "/api/v1/invoice-revisions/{revisionId:guid}/posting-lines/{postingLineId:guid}/commercial-edit-preview",
        (Guid revisionId,
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
        })
    .WithName("previewPostingLineCommercialEdit");

app.Run();

public sealed record HealthResponse(
    string Status,
    string Service,
    bool GeniusWritesEnabled);

public partial class Program;
