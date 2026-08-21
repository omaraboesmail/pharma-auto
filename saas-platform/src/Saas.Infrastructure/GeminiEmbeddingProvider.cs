using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using PharmaAuto.Saas.Application;

namespace PharmaAuto.Saas.Infrastructure;

public sealed record GeminiEmbeddingOptions(
    string ApiKey,
    string Model,
    Uri Endpoint,
    int OutputDimensions);

public sealed class GeminiEmbeddingProvider(
    HttpClient httpClient,
    GeminiEmbeddingOptions options,
    ILogger<GeminiEmbeddingProvider> logger) : IEmbeddingProvider
{
    public string Version => $"{options.Model}:{options.OutputDimensions}";

    public async Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var retrievalInstruction =
            "Represent this pharmacy product description for semantic catalog retrieval: " +
            text[..Math.Min(text.Length, 4_000)];
        var payload = new JsonObject
        {
            ["model"] = $"models/{options.Model}",
            ["content"] = new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = retrievalInstruction }
                }
            },
            ["embedContentConfig"] = new JsonObject
            {
                ["outputDimensionality"] = options.OutputDimensions
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("x-goog-api-key", options.ApiKey);
        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Gemini embedding returned HTTP {Status}; canonical matching used lexical retrieval.",
                    (int)response.StatusCode);
                return null;
            }
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var root = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken);
            var values = root?["embedding"]?["values"]?.AsArray()
                ?? root?["embeddings"]?[0]?["values"]?.AsArray();
            if (values is null || values.Count != options.OutputDimensions)
            {
                logger.LogWarning(
                    "Gemini embedding returned an unexpected vector shape; canonical matching used lexical retrieval.");
                return null;
            }
            var vector = values.Select(value => value?.GetValue<float>() ?? float.NaN).ToArray();
            if (vector.Any(value => !float.IsFinite(value)))
            {
                logger.LogWarning(
                    "Gemini embedding contained a non-finite value; canonical matching used lexical retrieval.");
                return null;
            }
            Normalize(vector);
            return vector;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Gemini embedding timed out; canonical matching used lexical retrieval.");
            return null;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(
                exception,
                "Gemini embedding was unavailable; canonical matching used lexical retrieval.");
            return null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            logger.LogWarning(
                exception,
                "Gemini embedding response was invalid; canonical matching used lexical retrieval.");
            return null;
        }
    }

    private static void Normalize(float[] vector)
    {
        var magnitude = Math.Sqrt(vector.Sum(value => (double)value * value));
        if (magnitude <= 0d)
        {
            return;
        }
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(vector[index] / magnitude);
        }
    }
}
