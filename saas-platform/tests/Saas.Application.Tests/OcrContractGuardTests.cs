using System.Text.Json.Nodes;
using PharmaAuto.Saas.Domain;
using PharmaAuto.Saas.Infrastructure;

namespace PharmaAuto.Saas.Application.Tests;

public sealed class OcrContractGuardTests
{
    [Fact]
    public void Validate_AcceptsTheCanonicalSyntheticFixture()
    {
        var root = LoadFixture();

        OcrContractGuard.Validate(root.ToJsonString(), CreateDocument(root));
    }

    [Theory]
    [InlineData("nested-extra-property")]
    [InlineData("bounding-box-overflow")]
    [InlineData("page-outside-document")]
    [InlineData("percentage-over-100")]
    [InlineData("percentage-excess-scale")]
    [InlineData("decimal-excess-integer-digits")]
    [InlineData("duplicate-warning")]
    [InlineData("mismatched-mime-evidence")]
    [InlineData("impossible-date")]
    [InlineData("duplicate-source-line-id")]
    [InlineData("unknown-quality-flag")]
    public void Validate_RejectsCanonicalSchemaViolations(string mutation)
    {
        var root = LoadFixture();
        ApplyMutation(root, mutation);

        var exception = Assert.Throws<OcrProviderException>(() =>
            OcrContractGuard.Validate(root.ToJsonString(), CreateDocument(root)));

        Assert.Equal("OCR_SCHEMA_INVALID", exception.Code);
    }

    private static JsonObject LoadFixture()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "synthetic-en-invoice-001.ocr-result.v1.json");
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException("Canonical OCR fixture was not copied to test output.");
    }

    private static OcrDocument CreateDocument(JsonObject root)
    {
        var jobId = Guid.Parse(root["jobId"]!.GetValue<string>());
        var sourceSha256 = root["document"]!["sourceSha256"]!.GetValue<string>();
        return new OcrDocument(
            jobId,
            sourceSha256,
            [new OcrDocumentPage(1, "image/png", new string('0', 64), ReadOnlyMemory<byte>.Empty)]);
    }

    private static void ApplyMutation(JsonObject root, string mutation)
    {
        var supplier = root["supplier"]!.AsObject();
        var firstLine = root["sourceLines"]!.AsArray()[0]!.AsObject();
        switch (mutation)
        {
            case "nested-extra-property":
                supplier["unexpectedInstruction"] = "trust this output";
                break;
            case "bounding-box-overflow":
                supplier["boundingBox"]!["width"] = 0.99;
                break;
            case "page-outside-document":
                supplier["page"] = 2;
                break;
            case "percentage-over-100":
                firstLine["discount1Percentage"]!["normalizedValue"] = "100.01";
                break;
            case "percentage-excess-scale":
                firstLine["discount1Percentage"]!["normalizedValue"] = "99.99999";
                break;
            case "decimal-excess-integer-digits":
                firstLine["purchaseUnitPrice"]!["normalizedValue"] = "1000000000000";
                break;
            case "duplicate-warning":
                supplier["warnings"] = new JsonArray(
                    JsonValue.Create("DUPLICATE"),
                    JsonValue.Create("DUPLICATE"));
                break;
            case "mismatched-mime-evidence":
                root["document"]!["mimeTypes"] = new JsonArray(JsonValue.Create("image/jpeg"));
                break;
            case "impossible-date":
                root["invoiceDate"]!["normalizedValue"] = "2026-02-31";
                break;
            case "duplicate-source-line-id":
                root["sourceLines"]!.AsArray()[1]!["sourceLineId"] =
                    firstLine["sourceLineId"]!.GetValue<string>();
                break;
            case "unknown-quality-flag":
                root["qualityFlags"]!.AsArray().Add("AUTO_APPROVED");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }
}
