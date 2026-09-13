using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PharmaAuto.Connector.Application;
using PharmaAuto.Connector.Domain;
using PharmaAuto.Connector.Infrastructure;

namespace PharmaAuto.Connector.LocalApi.Tests;

public sealed class ChunkUploadConcurrencyTests : IAsyncLifetime
{
    private const int ConcurrentRequestCount = 16;
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        "pharma-auto-chunk-concurrency-tests",
        Guid.NewGuid().ToString("N"));
    private readonly TrackingObjectStore objectStore = new();
    private SqliteSidecarStore store = null!;
    private InvoiceWorkflowService workflow = null!;
    private Guid deviceId;
    private Guid jobId;

    public async Task InitializeAsync()
    {
        store = new SqliteSidecarStore(Path.Combine(testDirectory, "sidecar.db"));
        await store.InitializeAsync(CancellationToken.None);
        deviceId = Guid.NewGuid();
        jobId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 24, 14, 0, 0, TimeSpan.Zero);
        await store.SaveDeviceAsync(
            new DeviceRegistration(
                deviceId,
                "Concurrent upload test device",
                [1, 2, 3],
                now,
                null,
                now),
            CancellationToken.None);
        await store.CreateJobAsync(
            new InvoiceJob(
                jobId,
                deviceId,
                InvoiceJobState.Captured,
                1,
                0,
                null,
                null,
                now,
                now),
            CancellationToken.None);
        workflow = new InvoiceWorkflowService(
            store,
            objectStore,
            new UnusedFileInspector(),
            new UnusedSaasClient(),
            new CatalogSearchService(store),
            new InvoiceWorkflowQueue(),
            TimeProvider.System);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ConcurrentIdenticalUploads_AreSuccessfulIdempotentReplays()
    {
        var content = "identical resumable chunk"u8.ToArray();
        var chunkHash = Sha256(content);
        var pageHash = new string('a', 64);

        var outcomes = await UploadConcurrentlyAsync(
            Enumerable.Repeat(
                new UploadRequest(content, chunkHash, pageHash, "image/png"),
                ConcurrentRequestCount));

        Assert.All(outcomes, outcome =>
        {
            Assert.Null(outcome.Error);
            Assert.NotNull(outcome.Status);
            Assert.False(outcome.Status.Complete);
            Assert.Equal([0], outcome.Status.ReceivedChunks);
        });
        var persisted = await store.GetChunksAsync(jobId, 1, CancellationToken.None);
        Assert.Single(persisted);
        Assert.Equal(chunkHash, persisted[0].ChunkSha256);
        Assert.Equal(1, objectStore.Count);
    }

    [Fact]
    public async Task ConcurrentDifferentContentAtSameIndex_PersistsOneValueAndRejectsTheOther()
    {
        var firstContent = "first concurrent chunk"u8.ToArray();
        var secondContent = "second concurrent chunk"u8.ToArray();
        var firstHash = Sha256(firstContent);
        var secondHash = Sha256(secondContent);
        var pageHash = new string('b', 64);
        var requests = Enumerable.Range(0, ConcurrentRequestCount)
            .Select(index => index % 2 == 0
                ? new UploadRequest(firstContent, firstHash, pageHash, "image/jpeg")
                : new UploadRequest(secondContent, secondHash, pageHash, "image/jpeg"));

        var outcomes = await UploadConcurrentlyAsync(requests);

        Assert.Equal(ConcurrentRequestCount / 2, outcomes.Count(outcome => outcome.Error is null));
        var conflicts = outcomes.Where(outcome => outcome.Error is not null).ToArray();
        Assert.Equal(ConcurrentRequestCount / 2, conflicts.Length);
        Assert.All(conflicts, outcome =>
        {
            var error = Assert.IsType<InvalidOperationException>(outcome.Error);
            Assert.Equal(
                "A resumable chunk index was replayed with different content.",
                error.Message);
        });
        var persisted = await store.GetChunksAsync(jobId, 1, CancellationToken.None);
        Assert.Single(persisted);
        Assert.Contains(persisted[0].ChunkSha256, new[] { firstHash, secondHash });
        Assert.Equal(1, objectStore.Count);
    }

    [Fact]
    public async Task ConcurrentDifferentMetadataAtSameIndex_PersistsOneValueAndRejectsTheOther()
    {
        var content = "same chunk with conflicting page metadata"u8.ToArray();
        var chunkHash = Sha256(content);
        var firstPageHash = new string('c', 64);
        var secondPageHash = new string('d', 64);
        var requests = Enumerable.Range(0, ConcurrentRequestCount)
            .Select(index => new UploadRequest(
                content,
                chunkHash,
                index % 2 == 0 ? firstPageHash : secondPageHash,
                "image/png"));

        var outcomes = await UploadConcurrentlyAsync(requests);

        Assert.Equal(ConcurrentRequestCount / 2, outcomes.Count(outcome => outcome.Error is null));
        var conflicts = outcomes.Where(outcome => outcome.Error is not null).ToArray();
        Assert.Equal(ConcurrentRequestCount / 2, conflicts.Length);
        Assert.All(conflicts, outcome =>
        {
            var error = Assert.IsType<InvalidOperationException>(outcome.Error);
            Assert.Equal(
                "A resumable chunk index was replayed with different content.",
                error.Message);
        });
        var persisted = await store.GetChunksAsync(jobId, 1, CancellationToken.None);
        Assert.Single(persisted);
        Assert.Contains(persisted[0].PageSha256, new[] { firstPageHash, secondPageHash });
        Assert.Equal(1, objectStore.Count);
    }

    private async Task<IReadOnlyList<UploadOutcome>> UploadConcurrentlyAsync(
        IEnumerable<UploadRequest> requests)
    {
        var start = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = requests
            .Select(request => Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    var status = await workflow.UploadChunkAsync(
                        deviceId,
                        jobId,
                        page: 1,
                        chunkIndex: 0,
                        chunkCount: 2,
                        request.ChunkSha256,
                        request.PageSha256,
                        request.MimeType,
                        request.Content,
                        CancellationToken.None);
                    return new UploadOutcome(status, null);
                }
                catch (Exception exception)
                {
                    return new UploadOutcome(null, exception);
                }
            }))
            .ToArray();
        start.SetResult(true);
        return await Task.WhenAll(tasks);
    }

    private static string Sha256(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    private sealed record UploadRequest(
        byte[] Content,
        string ChunkSha256,
        string PageSha256,
        string MimeType);

    private sealed record UploadOutcome(UploadPageStatus? Status, Exception? Error);

    private sealed class TrackingObjectStore : IDocumentObjectStore
    {
        private readonly ConcurrentDictionary<string, byte[]> objects = new();
        private int sequence;

        public int Count => objects.Count;

        public Task<string> WriteAsync(
            string category,
            Guid jobId,
            string objectName,
            ReadOnlyMemory<byte> plaintext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reference = $"{category}/{jobId:D}/{objectName}-{Interlocked.Increment(ref sequence)}";
            Assert.True(objects.TryAdd(reference, plaintext.ToArray()));
            return Task.FromResult(reference);
        }

        public Task<byte[]> ReadAsync(
            string objectReference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(objects[objectReference]);
        }

        public Task DeleteAsync(
            string objectReference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            objects.TryRemove(objectReference, out _);
            return Task.CompletedTask;
        }

        public Task<int> DeleteExpiredAsync(
            DateTimeOffset olderThan,
            CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    private sealed class UnusedFileInspector : IFileSafetyInspector
    {
        public Task<FileInspection> InspectAsync(
            ReadOnlyMemory<byte> content,
            string claimedMimeType,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Incomplete test chunks must not be inspected.");
    }

    private sealed class UnusedSaasClient : ISaasClient
    {
        public Task<string> GetEntitlementAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The SaaS client is not used during chunk upload.");

        public Task<SaasOcrResponse> ProcessOcrAsync(
            Guid jobId,
            string sourceSha256,
            IReadOnlyList<(DocumentPage Metadata, byte[] Content)> pages,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The SaaS client is not used during chunk upload.");

        public Task<IReadOnlyList<SaasCanonicalCandidate>> SearchCanonicalAsync(
            string description,
            string? vendorItemCode,
            string? activeIngredient,
            string? strength,
            string? dosageForm,
            string? pack,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The SaaS client is not used during chunk upload.");
    }
}
