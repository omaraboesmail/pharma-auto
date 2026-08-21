using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PharmaAuto.Connector.Domain;

namespace PharmaAuto.Connector.Application;

public sealed record UploadPageStatus(
    int Page,
    bool Complete,
    int ChunkCount,
    IReadOnlyList<int> ReceivedChunks,
    string? Sha256,
    string? MimeType);

public sealed record InvoiceUploadStatus(
    Guid JobId,
    int ExpectedPageCount,
    int UploadedPageCount,
    IReadOnlyList<UploadPageStatus> Pages);

public sealed class InvoiceWorkflowService(
    ISidecarStore store,
    IDocumentObjectStore objectStore,
    IFileSafetyInspector fileInspector,
    ISaasClient saasClient,
    CatalogSearchService catalogSearch,
    IInvoiceWorkflowQueue queue,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions ContractJsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) }
    };
    private const int MaximumChunkBytes = 4 * 1024 * 1024;
    private const int MaximumPageBytes = 20 * 1024 * 1024;

    public async Task<InvoiceJob> CreateJobAsync(
        Guid deviceId,
        int expectedPageCount,
        CancellationToken cancellationToken)
    {
        if (expectedPageCount is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedPageCount),
                "An invoice must contain 1..100 pages.");
        }
        var now = timeProvider.GetUtcNow();
        var job = new InvoiceJob(
            Guid.NewGuid(),
            deviceId,
            InvoiceJobState.Captured,
            expectedPageCount,
            0,
            null,
            null,
            now,
            now);
        await store.CreateJobAsync(job, cancellationToken);
        await store.AppendAuditAsync(
            new AuditRecord(
                Guid.NewGuid(),
                "DEVICE",
                deviceId.ToString("D"),
                "INVOICE_CAPTURED",
                job.JobId.ToString("D"),
                "SUCCESS",
                job.JobId,
                now),
            cancellationToken);
        return job;
    }

    public async Task<UploadPageStatus> UploadChunkAsync(
        Guid deviceId,
        Guid jobId,
        int page,
        int chunkIndex,
        int chunkCount,
        string chunkSha256,
        string pageSha256,
        string mimeType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var job = await RequireOwnedJobAsync(deviceId, jobId, cancellationToken);
        if (job.State != InvoiceJobState.Captured)
        {
            throw new InvalidOperationException("Uploads are accepted only while a job is Captured.");
        }
        if (page is < 1 || page > job.ExpectedPageCount ||
            chunkCount is < 1 or > 1000 ||
            chunkIndex < 0 ||
            chunkIndex >= chunkCount)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "Upload page or chunk range is invalid.");
        }
        if (content.Length is < 1 or > MaximumChunkBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(content), "Chunk exceeds the 4 MiB limit.");
        }
        RequireSha256(chunkSha256, nameof(chunkSha256));
        RequireSha256(pageSha256, nameof(pageSha256));
        var actualChunkHash = Convert.ToHexStringLower(SHA256.HashData(content.Span));
        if (!FixedEquals(actualChunkHash, chunkSha256))
        {
            throw new ArgumentException("Chunk hash does not match its payload.", nameof(chunkSha256));
        }

        var existingChunks = await store.GetChunksAsync(jobId, page, cancellationToken);
        var matching = existingChunks.FirstOrDefault(chunk => chunk.ChunkIndex == chunkIndex);
        if (matching is not null)
        {
            if (!FixedEquals(matching.ChunkSha256, chunkSha256) ||
                matching.ChunkCount != chunkCount ||
                !FixedEquals(matching.PageSha256, pageSha256))
            {
                throw new InvalidOperationException(
                    "A resumable chunk index was replayed with different content.");
            }
            return await BuildPageStatusAsync(jobId, page, cancellationToken);
        }

        if (existingChunks.Any(chunk =>
                chunk.ChunkCount != chunkCount ||
                !FixedEquals(chunk.PageSha256, pageSha256) ||
                !string.Equals(chunk.MimeType, mimeType, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Chunk metadata conflicts with the existing page upload.");
        }

        var objectReference = await objectStore.WriteAsync(
            "chunks",
            jobId,
            $"page-{page:D3}-chunk-{chunkIndex:D4}",
            content,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        await store.SaveChunkAsync(
            new UploadChunk(
                jobId,
                page,
                chunkIndex,
                chunkCount,
                chunkSha256,
                pageSha256,
                mimeType,
                objectReference,
                content.Length,
                now),
            cancellationToken);

        var chunks = await store.GetChunksAsync(jobId, page, cancellationToken);
        if (chunks.Count == chunkCount)
        {
            await FinalizePageAsync(job, page, chunks, cancellationToken);
        }
        return await BuildPageStatusAsync(jobId, page, cancellationToken);
    }

    public async Task<InvoiceUploadStatus> GetUploadStatusAsync(
        Guid deviceId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await RequireOwnedJobAsync(deviceId, jobId, cancellationToken);
        var pages = await store.GetPagesAsync(jobId, cancellationToken);
        var statuses = new List<UploadPageStatus>();
        for (var page = 1; page <= job.ExpectedPageCount; page++)
        {
            var complete = pages.FirstOrDefault(candidate => candidate.Page == page);
            if (complete is not null)
            {
                statuses.Add(new UploadPageStatus(
                    page,
                    true,
                    1,
                    [0],
                    complete.Sha256,
                    complete.MimeType));
            }
            else
            {
                var chunks = await store.GetChunksAsync(jobId, page, cancellationToken);
                statuses.Add(new UploadPageStatus(
                    page,
                    false,
                    chunks.FirstOrDefault()?.ChunkCount ?? 0,
                    chunks.Select(chunk => chunk.ChunkIndex).Order().ToArray(),
                    chunks.FirstOrDefault()?.PageSha256,
                    chunks.FirstOrDefault()?.MimeType));
            }
        }
        return new InvoiceUploadStatus(
            jobId,
            job.ExpectedPageCount,
            pages.Count,
            statuses);
    }

    public async Task SubmitAsync(
        Guid deviceId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await RequireOwnedJobAsync(deviceId, jobId, cancellationToken);
        if (job.State != InvoiceJobState.LocallyValidated)
        {
            throw new InvalidOperationException(
                "Every page must be uploaded and locally validated before OCR submission.");
        }
        await queue.EnqueueAsync(jobId, cancellationToken);
    }

    public async Task ProcessAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await store.GetJobAsync(jobId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice job does not exist.");
        if (job.State != InvoiceJobState.LocallyValidated &&
            job.State != InvoiceJobState.OcrFailed)
        {
            return;
        }

        var expected = job.State;
        if (!await store.TransitionJobAsync(
                jobId,
                expected,
                InvoiceJobState.OcrReserved,
                timeProvider.GetUtcNow(),
                null,
                null,
                cancellationToken))
        {
            return;
        }

        try
        {
            _ = await saasClient.GetEntitlementAsync(cancellationToken);
            await RequireTransitionAsync(
                jobId,
                InvoiceJobState.OcrReserved,
                InvoiceJobState.OcrProcessing,
                cancellationToken);
            var pages = await store.GetPagesAsync(jobId, cancellationToken);
            var orderedPages = new List<(DocumentPage Metadata, byte[] Content)>();
            foreach (var page in pages.OrderBy(page => page.Page))
            {
                orderedPages.Add((
                    page,
                    await objectStore.ReadAsync(page.ObjectReference, cancellationToken)));
            }

            var sourceSha256 = ComputeLogicalDocumentSha256(pages);
            var ocrResponse = await saasClient.ProcessOcrAsync(
                jobId,
                sourceSha256,
                orderedPages,
                cancellationToken);
            if (!string.Equals(ocrResponse.State, "COMPLETED", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(ocrResponse.ResultJson))
            {
                throw new WorkflowException(
                    "OCR_PROVIDER_FAILED",
                    ocrResponse.FailureCode ?? "OCR job did not complete.");
            }

            await RequireTransitionAsync(
                jobId,
                InvoiceJobState.OcrProcessing,
                InvoiceJobState.OcrValidated,
                cancellationToken);
            await RequireTransitionAsync(
                jobId,
                InvoiceJobState.OcrValidated,
                InvoiceJobState.Matching,
                cancellationToken);

            var revision = await BuildRevisionAsync(
                job,
                ocrResponse.ResultJson,
                cancellationToken);
            await store.SaveRevisionAsync(revision, cancellationToken);
            await RequireTransitionAsync(
                jobId,
                InvoiceJobState.Matching,
                InvoiceJobState.AwaitingUserReview,
                cancellationToken,
                revision.RevisionId);
        }
        catch (WorkflowException exception)
        {
            var current = await store.GetJobAsync(jobId, cancellationToken);
            if (current is null)
            {
                return;
            }
            var failedState = current.State is InvoiceJobState.OcrReserved or
                InvoiceJobState.OcrProcessing
                ? InvoiceJobState.OcrFailed
                : InvoiceJobState.MatchingFailed;
            if (current.State != failedState)
            {
                _ = await store.TransitionJobAsync(
                    jobId,
                    current.State,
                    failedState,
                    timeProvider.GetUtcNow(),
                    exception.Code,
                    null,
                    cancellationToken);
            }
        }
    }

    public async Task<InvoiceRevisionRecord> SaveEditedRevisionAsync(
        Guid deviceId,
        Guid sourceRevisionId,
        string revisionJson,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            throw new ArgumentException("A concise revision reason is required.", nameof(reason));
        }
        var source = await store.GetRevisionAsync(sourceRevisionId, cancellationToken)
            ?? throw new InvalidOperationException("Source revision does not exist.");
        var job = await RequireOwnedJobAsync(deviceId, source.JobId, cancellationToken);
        if (job.State != InvoiceJobState.AwaitingUserReview)
        {
            throw new InvalidOperationException("Invoice is not awaiting review.");
        }

        var revisionId = Guid.NewGuid();
        var validatedSelections = await ResolveAdditionalSelectionsAsync(
            revisionJson,
            source.Json,
            cancellationToken);
        var guardedJson = ReviewRevisionGuard.PrepareEditedRevision(
            revisionJson,
            source.Json,
            validatedSelections.Vendor,
            validatedSelections.Items,
            source.JobId,
            revisionId,
            source.RevisionId,
            source.RevisionNumber + 1,
            reason,
            deviceId,
            timeProvider.GetUtcNow());
        var revision = new InvoiceRevisionRecord(
            revisionId,
            source.JobId,
            source.RevisionNumber + 1,
            "AWAITING_USER_REVIEW",
            guardedJson,
            deviceId,
            timeProvider.GetUtcNow(),
            null);
        await store.SaveRevisionAsync(revision, cancellationToken);
        _ = await store.TransitionJobAsync(
            job.JobId,
            InvoiceJobState.AwaitingUserReview,
            InvoiceJobState.AwaitingUserReview,
            timeProvider.GetUtcNow(),
            null,
            revision.RevisionId,
            cancellationToken);
        return revision;
    }

    private async Task<ValidatedSelections> ResolveAdditionalSelectionsAsync(
        string editedJson,
        string trustedJson,
        CancellationToken cancellationToken)
    {
        var edited = JsonNode.Parse(editedJson)?.AsObject()
            ?? throw new InvalidOperationException("Edited revision is invalid.");
        var trusted = JsonNode.Parse(trustedJson)?.AsObject()
            ?? throw new InvalidOperationException("Trusted revision is invalid.");
        LocalVendorCandidate? additionalVendor = null;
        var selectedVendor = edited["selectedLocalVendorReference"]?.GetValue<string?>();
        if (!string.IsNullOrWhiteSpace(selectedVendor) &&
            !(trusted["vendorCandidates"]?.AsArray() ?? []).Any(candidate =>
                candidate?["localVendorReference"]?.GetValue<string?>() == selectedVendor))
        {
            additionalVendor = await catalogSearch.ResolveVendorAsync(
                selectedVendor,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    "Selected local Vendor is not active in the current catalog projection.");
        }

        var additionalItems = new Dictionary<string, LocalItemCandidate>(StringComparer.Ordinal);
        var trustedLines = (trusted["sourceLines"]?.AsArray() ?? [])
            .OfType<JsonObject>()
            .ToDictionary(
                line => line["sourceLineId"]?.GetValue<string>()
                    ?? throw new InvalidOperationException("Trusted source line identity is missing."),
                StringComparer.Ordinal);
        foreach (var editedLine in (edited["sourceLines"]?.AsArray() ?? []).OfType<JsonObject>())
        {
            var sourceLineId = editedLine["sourceLineId"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Edited source line identity is missing.");
            if (!trustedLines.TryGetValue(sourceLineId, out var trustedLine))
            {
                throw new InvalidOperationException("Edited source line is not in the trusted revision.");
            }
            var selectedItem = editedLine["selectedLocalItemReference"]?.GetValue<string?>();
            if (string.IsNullOrWhiteSpace(selectedItem) ||
                (trustedLine["localCandidates"]?.AsArray() ?? []).Any(candidate =>
                    candidate?["localItemReference"]?.GetValue<string?>() == selectedItem))
            {
                continue;
            }
            var description = trustedLine["descriptionEvidence"]?["normalizedValue"]
                ?.GetValue<string?>() ?? string.Empty;
            var vendorItemCode = trustedLine["vendorItemCodeEvidence"]?["normalizedValue"]
                ?.GetValue<string?>();
            var candidate = await catalogSearch.ResolveItemAsync(
                selectedItem,
                new LocalMatchQuery(
                    description,
                    vendorItemCode,
                    null,
                    null,
                    null,
                    ExtractStrength(description),
                    ExtractDosageForm(description),
                    null,
                    1),
                cancellationToken)
                ?? throw new InvalidOperationException(
                    "Selected local Item is not active in the current catalog projection.");
            if (candidate.HardMismatches.Count > 0)
            {
                throw new InvalidOperationException(
                    "Selected local Item has a blocking pharmaceutical mismatch.");
            }
            additionalItems.Add(sourceLineId, candidate);
        }
        return new ValidatedSelections(additionalVendor, additionalItems);
    }

    public async Task ConfirmRevisionAsync(
        Guid deviceId,
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        var revision = await store.GetRevisionAsync(revisionId, cancellationToken)
            ?? throw new InvalidOperationException("Revision does not exist.");
        var job = await RequireOwnedJobAsync(deviceId, revision.JobId, cancellationToken);
        if (job.CurrentRevisionId != revisionId ||
            job.State != InvoiceJobState.AwaitingUserReview)
        {
            throw new InvalidOperationException("Only the current review revision can be confirmed.");
        }
        ReviewRevisionGuard.EnsureConfirmable(revision.Json);
        var now = timeProvider.GetUtcNow();
        if (!await store.ConfirmRevisionAsync(revisionId, deviceId, now, cancellationToken))
        {
            throw new InvalidOperationException("Revision was already confirmed or changed.");
        }
        await RequireTransitionAsync(
            job.JobId,
            InvoiceJobState.AwaitingUserReview,
            InvoiceJobState.Confirmed,
            cancellationToken,
            revisionId);
        await store.AppendAuditAsync(
            new AuditRecord(
                Guid.NewGuid(),
                "DEVICE",
                deviceId.ToString("D"),
                "REVISION_CONFIRMED_READ_ONLY",
                revisionId.ToString("D"),
                "SUCCESS_NO_GENIUS_WRITE",
                job.JobId,
                now),
            cancellationToken);
    }

    private async Task FinalizePageAsync(
        InvoiceJob job,
        int page,
        IReadOnlyList<UploadChunk> chunks,
        CancellationToken cancellationToken)
    {
        var ordered = chunks.OrderBy(chunk => chunk.ChunkIndex).ToArray();
        if (ordered.Select(chunk => chunk.ChunkIndex).Where((value, index) => value != index).Any())
        {
            return;
        }
        var total = ordered.Sum(chunk => chunk.Length);
        if (total > MaximumPageBytes)
        {
            throw new InvalidOperationException("Assembled page exceeds the 20 MiB limit.");
        }

        await using var stream = new MemoryStream((int)total);
        foreach (var chunk in ordered)
        {
            var bytes = await objectStore.ReadAsync(chunk.ObjectReference, cancellationToken);
            await stream.WriteAsync(bytes, cancellationToken);
        }
        var pageBytes = stream.ToArray();
        var pageHash = Convert.ToHexStringLower(SHA256.HashData(pageBytes));
        if (!FixedEquals(pageHash, ordered[0].PageSha256))
        {
            throw new InvalidOperationException("Assembled page hash does not match the declared hash.");
        }

        var inspection = await fileInspector.InspectAsync(
            pageBytes,
            ordered[0].MimeType,
            cancellationToken);
        var objectReference = await objectStore.WriteAsync(
            "pages",
            job.JobId,
            $"page-{page:D3}",
            pageBytes,
            cancellationToken);
        await store.SavePageAsync(
            new DocumentPage(
                job.JobId,
                page,
                inspection.MimeType,
                pageHash,
                objectReference,
                pageBytes.Length,
                timeProvider.GetUtcNow()),
            cancellationToken);

        foreach (var chunk in ordered)
        {
            await objectStore.DeleteAsync(chunk.ObjectReference, cancellationToken);
        }
        await store.DeleteChunksAsync(job.JobId, page, cancellationToken);

        var pages = await store.GetPagesAsync(job.JobId, cancellationToken);
        if (pages.Count == job.ExpectedPageCount)
        {
            _ = await store.TransitionJobAsync(
                job.JobId,
                InvoiceJobState.Captured,
                InvoiceJobState.LocallyValidated,
                timeProvider.GetUtcNow(),
                null,
                null,
                cancellationToken);
        }
    }

    private async Task<UploadPageStatus> BuildPageStatusAsync(
        Guid jobId,
        int page,
        CancellationToken cancellationToken)
    {
        var completed = (await store.GetPagesAsync(jobId, cancellationToken))
            .FirstOrDefault(candidate => candidate.Page == page);
        if (completed is not null)
        {
            return new UploadPageStatus(page, true, 1, [0], completed.Sha256, completed.MimeType);
        }
        var chunks = await store.GetChunksAsync(jobId, page, cancellationToken);
        return new UploadPageStatus(
            page,
            false,
            chunks.FirstOrDefault()?.ChunkCount ?? 0,
            chunks.Select(chunk => chunk.ChunkIndex).Order().ToArray(),
            chunks.FirstOrDefault()?.PageSha256,
            chunks.FirstOrDefault()?.MimeType);
    }

    private async Task<InvoiceRevisionRecord> BuildRevisionAsync(
        InvoiceJob job,
        string ocrJson,
        CancellationToken cancellationToken)
    {
        var ocr = JsonNode.Parse(ocrJson)?.AsObject()
            ?? throw new WorkflowException("OCR_SCHEMA_INVALID", "OCR result is not a JSON object.");
        var supplier = ocr["supplier"]?["normalizedValue"]?.GetValue<string?>() ?? string.Empty;
        var vendorCandidates = await catalogSearch.SearchVendorsAsync(
            supplier,
            10,
            cancellationToken);
        var sourceLines = ocr["sourceLines"]?.AsArray()
            ?? throw new WorkflowException("OCR_SCHEMA_INVALID", "OCR source lines are missing.");
        var reviewLines = new JsonArray();
        var postingSequence = 1;
        foreach (var node in sourceLines)
        {
            var line = node?.AsObject()
                ?? throw new WorkflowException("OCR_SCHEMA_INVALID", "OCR source line is invalid.");
            var description = Normalized(line, "description");
            var vendorItemCode = Normalized(line, "vendorItemCode");
            var strength = ExtractStrength(description);
            var dosageForm = ExtractDosageForm(description);
            var localCandidates = await catalogSearch.SearchItemsAsync(
                new LocalMatchQuery(
                    description ?? string.Empty,
                    vendorItemCode,
                    null,
                    null,
                    null,
                    strength,
                    dosageForm,
                    null,
                    10),
                cancellationToken);
            var canonicalCandidates = await saasClient.SearchCanonicalAsync(
                description ?? string.Empty,
                vendorItemCode,
                null,
                strength,
                dosageForm,
                null,
                cancellationToken);
            var postingLineId = Guid.NewGuid();
            reviewLines.Add(new JsonObject
            {
                ["sourceLineId"] = line["sourceLineId"]?.DeepClone(),
                ["sequence"] = line["sequence"]?.DeepClone(),
                ["descriptionEvidence"] = line["description"]?.DeepClone(),
                ["vendorItemCodeEvidence"] = line["vendorItemCode"]?.DeepClone(),
                ["selectedLocalItemReference"] = null,
                ["requiresManualItemConfirmation"] = true,
                ["localCandidates"] = JsonSerializer.SerializeToNode(
                    localCandidates,
                    ContractJsonOptions),
                ["canonicalCandidates"] = JsonSerializer.SerializeToNode(
                    canonicalCandidates,
                    ContractJsonOptions),
                ["postingLines"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["postingLineId"] = postingLineId.ToString("D"),
                        ["splitIndex"] = 1,
                        ["postingSequence"] = postingSequence++,
                        ["quantity"] = Normalized(line, "quantity"),
                        ["bonus"] = "0",
                        ["expiryDate"] = Normalized(line, "expiryDate"),
                        ["batch"] = Normalized(line, "batch"),
                        ["commercialValues"] = BuildCommercialValues(line),
                        ["originalOcrCommercialValues"] = new JsonObject
                        {
                            ["purchaseUnitPrice"] = line["purchaseUnitPrice"]?.DeepClone(),
                            ["discount1Percentage"] = line["discount1Percentage"]?.DeepClone(),
                            ["discount2Percentage"] = line["discount2Percentage"]?.DeepClone(),
                            ["sellingUnitPrice"] = line["sellingUnitPrice"]?.DeepClone()
                        }
                    }
                }
            });
        }

        var now = timeProvider.GetUtcNow();
        var revisionId = Guid.NewGuid();
        var review = new JsonObject
        {
            ["schemaVersion"] = "1.0",
            ["jobId"] = job.JobId.ToString("D"),
            ["revisionId"] = revisionId.ToString("D"),
            ["revisionNumber"] = 1,
            ["sourceRevisionId"] = null,
            ["status"] = "AWAITING_USER_REVIEW",
            ["vendorEvidence"] = ocr["supplier"]?.DeepClone(),
            ["selectedLocalVendorReference"] = null,
            ["requiresManualVendorConfirmation"] = true,
            ["vendorCandidates"] = JsonSerializer.SerializeToNode(
                vendorCandidates,
                ContractJsonOptions),
            ["sourceLines"] = reviewLines,
            ["qualityFlags"] = ocr["qualityFlags"]?.DeepClone(),
            ["ocrEvidence"] = ocr.DeepClone(),
            ["createdByDeviceId"] = job.DeviceId.ToString("D"),
            ["createdAt"] = now.ToString("O"),
            ["revisionReason"] = "OCR_AND_MATCHING_RESULT",
            ["geniusWritePerformed"] = false
        };
        return new InvoiceRevisionRecord(
            revisionId,
            job.JobId,
            1,
            "AWAITING_USER_REVIEW",
            review.ToJsonString(),
            job.DeviceId,
            now,
            null);
    }

    private async Task<InvoiceJob> RequireOwnedJobAsync(
        Guid deviceId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await store.GetJobAsync(jobId, cancellationToken)
            ?? throw new InvalidOperationException("Invoice job does not exist.");
        if (job.DeviceId != deviceId)
        {
            throw new UnauthorizedAccessException("Invoice job belongs to another device.");
        }
        return job;
    }

    private async Task RequireTransitionAsync(
        Guid jobId,
        InvoiceJobState expected,
        InvoiceJobState next,
        CancellationToken cancellationToken,
        Guid? revisionId = null)
    {
        if (!await store.TransitionJobAsync(
                jobId,
                expected,
                next,
                timeProvider.GetUtcNow(),
                null,
                revisionId,
                cancellationToken))
        {
            throw new WorkflowException(
                "STATE_CONFLICT",
                $"Job state changed while moving from {expected} to {next}.");
        }
    }

    private static JsonObject BuildCommercialValues(JsonObject line) => new()
    {
        ["currency"] = "EGP",
        ["purchaseUnit"] = Normalized(line, "unit") ?? "BOX",
        ["purchaseUnitPrice"] = Normalized(line, "purchaseUnitPrice") ?? "0",
        ["purchasePriceTaxTreatment"] = "EXPLICIT",
        ["discounts"] = new JsonArray
        {
            new JsonObject
            {
                ["sequence"] = 1,
                ["kind"] = "PERCENTAGE",
                ["percentage"] = Normalized(line, "discount1Percentage") ?? "0",
                ["applicationBasis"] = "PURCHASE_UNIT_PRICE",
                ["affectsPurchaseUnitPrice"] = true
            },
            new JsonObject
            {
                ["sequence"] = 2,
                ["kind"] = "PERCENTAGE",
                ["percentage"] = Normalized(line, "discount2Percentage") ?? "0",
                ["applicationBasis"] = "REMAINING_LINE_SUBTOTAL",
                ["affectsPurchaseUnitPrice"] = false
            }
        },
        ["sellingUnit"] = "BOX",
        ["sellingUnitPrice"] = Normalized(line, "sellingUnitPrice") ?? "0",
        ["sellingPriceTaxTreatment"] = "INCLUSIVE",
        ["sellingPriceScope"] = "NEW_STOCK_ONLY",
        ["existingStockPriceBehavior"] = "PRESERVE",
        ["unsupportedScopeBehavior"] = "BLOCK_COMMIT"
    };

    private static string? Normalized(JsonObject line, string field) =>
        line[field]?["normalizedValue"]?.GetValue<string?>();

    private static string? ExtractStrength(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var tokens = description.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            if (decimal.TryParse(tokens[index], NumberStyles.Number, CultureInfo.InvariantCulture, out _) &&
                tokens[index + 1] is "mg" or "MG" or "ml" or "ML" or "مجم" or "مل")
            {
                return $"{tokens[index]} {tokens[index + 1]}";
            }
        }
        return null;
    }

    private static string? ExtractDosageForm(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        if (description.Contains("CAPS", StringComparison.OrdinalIgnoreCase)) return "CAPSULE";
        if (description.Contains("TAB", StringComparison.OrdinalIgnoreCase)) return "TABLET";
        if (description.Contains("SYRUP", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("شراب", StringComparison.Ordinal)) return "SYRUP";
        return null;
    }

    private static string ComputeLogicalDocumentSha256(IReadOnlyList<DocumentPage> pages)
    {
        var lines = pages
            .OrderBy(page => page.Page)
            .Select(page => $"{page.Page}:{page.Sha256}");
        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n")));
    }

    private sealed record ValidatedSelections(
        LocalVendorCandidate? Vendor,
        IReadOnlyDictionary<string, LocalItemCandidate> Items);

    private static void RequireSha256(string value, string parameter)
    {
        if (value.Length != 64 || value.Any(character =>
                !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
        {
            throw new ArgumentException("SHA-256 must be 64 lowercase hexadecimal characters.", parameter);
        }
    }

    private static bool FixedEquals(string first, string second)
    {
        var firstBytes = Encoding.ASCII.GetBytes(first);
        var secondBytes = Encoding.ASCII.GetBytes(second);
        return firstBytes.Length == secondBytes.Length &&
            CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes);
    }
}

public sealed class WorkflowException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
