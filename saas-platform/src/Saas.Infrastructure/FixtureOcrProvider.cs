using System.Text.Json;
using System.Text.Json.Nodes;
using PharmaAuto.Saas.Application;
using PharmaAuto.Saas.Domain;

namespace PharmaAuto.Saas.Infrastructure;

public sealed class FixtureOcrProvider(string expectedResultDirectory, TimeProvider timeProvider)
    : IOcrProvider
{
    private readonly SemaphoreSlim cacheGate = new(1, 1);
    private IReadOnlyDictionary<string, string>? fixturePaths;

    public string ProviderName => "GEMINI_FIXTURE_REPLAY";

    public async Task<OcrProviderResult> ExtractAsync(
        OcrDocument document,
        CancellationToken cancellationToken)
    {
        var paths = await GetFixturePathsAsync(cancellationToken);
        if (!paths.TryGetValue(document.SourceSha256, out var fixturePath))
        {
            throw new OcrProviderException(
                "FIXTURE_NOT_FOUND",
                "No approved synthetic OCR fixture matches the document hash.");
        }

        var json = await File.ReadAllTextAsync(fixturePath, cancellationToken);
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new OcrProviderException("FIXTURE_INVALID", "OCR fixture is not a JSON object.");
        var processedAt = timeProvider.GetUtcNow();
        root["resultId"] = Guid.NewGuid().ToString("D");
        root["jobId"] = document.JobId.ToString("D");
        root["provider"] = new JsonObject
        {
            ["name"] = "GEMINI",
            ["model"] = "synthetic-fixture-replay-v1",
            ["processedAt"] = processedAt.ToString("O")
        };
        root["document"] = new JsonObject
        {
            ["sourceSha256"] = document.SourceSha256,
            ["pageCount"] = document.Pages.Count,
            ["mimeTypes"] = new JsonArray(
                document.Pages
                    .Select(page => page.MimeType)
                    .Distinct(StringComparer.Ordinal)
                    .Select(value => (JsonNode?)JsonValue.Create(value))
                    .ToArray<JsonNode?>())
        };

        var resultJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        OcrContractGuard.Validate(resultJson, document);
        return new OcrProviderResult(
            "synthetic-fixture-replay-v1",
            resultJson,
            document.Pages.Count,
            resultJson.Length,
            processedAt);
    }

    private async Task<IReadOnlyDictionary<string, string>> GetFixturePathsAsync(
        CancellationToken cancellationToken)
    {
        if (fixturePaths is not null)
        {
            return fixturePaths;
        }

        await cacheGate.WaitAsync(cancellationToken);
        try
        {
            if (fixturePaths is not null)
            {
                return fixturePaths;
            }

            if (!Directory.Exists(expectedResultDirectory))
            {
                throw new OcrProviderException(
                    "FIXTURE_DIRECTORY_MISSING",
                    "The configured synthetic OCR fixture directory does not exist.");
            }

            var discovered = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(
                         expectedResultDirectory,
                         "*.ocr-result.v1.json",
                         SearchOption.TopDirectoryOnly))
            {
                var root = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken))
                    ?.AsObject();
                var sourceHash = root?["document"]?["sourceSha256"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(sourceHash) || !discovered.TryAdd(sourceHash, path))
                {
                    throw new OcrProviderException(
                        "FIXTURE_INDEX_INVALID",
                        "Synthetic OCR fixtures contain a missing or duplicate source hash.");
                }
            }

            fixturePaths = discovered;
            return fixturePaths;
        }
        finally
        {
            cacheGate.Release();
        }
    }
}
