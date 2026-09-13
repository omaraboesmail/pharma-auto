using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PharmaAuto.Connector.Application;
using PharmaAuto.Connector.Domain;
using PharmaAuto.Connector.Infrastructure;

namespace PharmaAuto.Connector.LocalApi.Tests;

public sealed class ChunkUploadRecoveryTests : IAsyncLifetime
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        "pharma-auto-chunk-recovery-tests",
        Guid.NewGuid().ToString("N"));
    private readonly TrackingObjectStore objectStore = new();
    private readonly AcceptingFileInspector fileInspector = new();
    private SqliteSidecarStore store = null!;
    private InvoiceWorkflowService workflow = null!;
    private DateTimeOffset now;
    private Guid deviceId;
    private Guid jobId;

    public async Task InitializeAsync()
    {
        store = new SqliteSidecarStore(Path.Combine(testDirectory, "sidecar.db"));
        await store.InitializeAsync(CancellationToken.None);
        deviceId = Guid.NewGuid();
        jobId = Guid.NewGuid();
        now = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
        await store.SaveDeviceAsync(
            new DeviceRegistration(
                deviceId,
                "Chunk recovery test device",
                [1, 2, 3],
                now,
                null,
                now),
            CancellationToken.None);
        await CreateJobAsync(expectedPageCount: 1);
        workflow = new InvoiceWorkflowService(
            store,
            objectStore,
            fileInspector,
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
    public async Task ReplayOfLastStoredChunk_RecoversCrashBeforeFinalization()
    {
        var content = "last chunk persisted before connector crash"u8.ToArray();
        var hash = Sha256(content);
        var persistedReference = await objectStore.WriteAsync(
            "chunks",
            jobId,
            "page-001-chunk-0000",
            content,
            CancellationToken.None);
        var initialSave = await store.SaveChunkAsync(
            new UploadChunk(
                jobId,
                1,
                0,
                1,
                hash,
                hash,
                "image/png",
                persistedReference,
                content.Length,
                now),
            CancellationToken.None);
        Assert.Equal(UploadChunkSaveDisposition.Stored, initialSave.Disposition);
        Assert.Empty(await store.GetPagesAsync(jobId, CancellationToken.None));

        var status = await workflow.UploadChunkAsync(
            deviceId,
            jobId,
            page: 1,
            chunkIndex: 0,
            chunkCount: 1,
            hash,
            hash,
            "image/png",
            content,
            CancellationToken.None);

        Assert.True(status.Complete);
        Assert.Equal(hash, status.Sha256);
        Assert.Empty(await store.GetChunksAsync(jobId, 1, CancellationToken.None));
        Assert.Single(await store.GetPagesAsync(jobId, CancellationToken.None));
        var job = await store.GetJobAsync(jobId, CancellationToken.None);
        Assert.NotNull(job);
        Assert.Equal(InvoiceJobState.LocallyValidated, job.State);
        Assert.Equal(1, objectStore.Count);
    }

    [Fact]
    public async Task StateTransitionBetweenWorkflowCheckAndChunkSave_RejectsInsert()
    {
        var content = "chunk racing with job transition"u8.ToArray();
        var hash = Sha256(content);
        objectStore.AfterWriteAsync = async category =>
        {
            if (category == "chunks")
            {
                Assert.True(await store.TransitionJobAsync(
                    jobId,
                    InvoiceJobState.Captured,
                    InvoiceJobState.LocallyValidated,
                    now.AddMinutes(1),
                    null,
                    null,
                    CancellationToken.None));
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.UploadChunkAsync(
                deviceId,
                jobId,
                page: 1,
                chunkIndex: 0,
                chunkCount: 2,
                hash,
                new string('a', 64),
                "image/jpeg",
                content,
                CancellationToken.None));

        Assert.Equal("Uploads are accepted only while a job is Captured.", exception.Message);
        Assert.Empty(await store.GetChunksAsync(jobId, 1, CancellationToken.None));
        Assert.Equal(0, objectStore.Count);
    }

    [Fact]
    public async Task CompletedPageBetweenWorkflowCheckAndChunkSave_ReturnsStoredPage()
    {
        Assert.True(await store.TransitionJobAsync(
            jobId,
            InvoiceJobState.Captured,
            InvoiceJobState.Rejected,
            now,
            null,
            null,
            CancellationToken.None));
        jobId = Guid.NewGuid();
        await CreateJobAsync(expectedPageCount: 2);
        var completedHash = new string('b', 64);
        objectStore.AfterWriteAsync = async category =>
        {
            if (category == "chunks")
            {
                _ = await store.FinalizePageUploadAsync(
                    new DocumentPage(
                        jobId,
                        1,
                        "image/png",
                        completedHash,
                        "pages/already-complete.pao",
                        100,
                        now.AddMinutes(1)),
                    CancellationToken.None);
            }
        };
        var content = "chunk racing with page finalization"u8.ToArray();
        var chunkHash = Sha256(content);

        var status = await workflow.UploadChunkAsync(
            deviceId,
            jobId,
            page: 1,
            chunkIndex: 0,
            chunkCount: 2,
            chunkHash,
            completedHash,
            "image/png",
            content,
            CancellationToken.None);

        Assert.True(status.Complete);
        Assert.Equal(completedHash, status.Sha256);
        Assert.Empty(await store.GetChunksAsync(jobId, 1, CancellationToken.None));
        Assert.Single(await store.GetPagesAsync(jobId, CancellationToken.None));
        var job = await store.GetJobAsync(jobId, CancellationToken.None);
        Assert.NotNull(job);
        Assert.Equal(InvoiceJobState.Captured, job.State);
        Assert.Equal(0, objectStore.Count);
    }

    [Fact]
    public async Task CompletedPageWithDifferentMetadataBetweenCheckAndSave_RejectsReplay()
    {
        var completedHash = new string('c', 64);
        objectStore.AfterWriteAsync = async category =>
        {
            if (category == "chunks")
            {
                _ = await store.FinalizePageUploadAsync(
                    new DocumentPage(
                        jobId,
                        1,
                        "image/png",
                        completedHash,
                        "pages/already-complete.pao",
                        100,
                        now.AddMinutes(1)),
                    CancellationToken.None);
            }
        };
        var content = "conflicting chunk after page finalization"u8.ToArray();
        var chunkHash = Sha256(content);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.UploadChunkAsync(
                deviceId,
                jobId,
                page: 1,
                chunkIndex: 0,
                chunkCount: 1,
                chunkHash,
                new string('d', 64),
                "image/jpeg",
                content,
                CancellationToken.None));

        Assert.Equal("Page number was replayed with different content.", exception.Message);
        Assert.Empty(await store.GetChunksAsync(jobId, 1, CancellationToken.None));
        var page = Assert.Single(await store.GetPagesAsync(jobId, CancellationToken.None));
        Assert.Equal(completedHash, page.Sha256);
        Assert.Equal(0, objectStore.Count);
    }

    [Fact]
    public async Task SaveChunk_RejectsBytesBeyondPageLimitInsideTransaction()
    {
        const long chunkLength = 4L * 1024 * 1024;
        var pageHash = new string('e', 64);
        for (var index = 0; index < 5; index++)
        {
            var result = await store.SaveChunkAsync(
                new UploadChunk(
                    jobId,
                    1,
                    index,
                    6,
                    TestHash(index + 1),
                    pageHash,
                    "image/jpeg",
                    $"chunks/{index}",
                    chunkLength,
                    now),
                CancellationToken.None);
            Assert.Equal(UploadChunkSaveDisposition.Stored, result.Disposition);
        }

        var replayAtLimit = await store.SaveChunkAsync(
            new UploadChunk(
                jobId,
                1,
                0,
                6,
                TestHash(1),
                pageHash,
                "image/jpeg",
                "chunks/0",
                chunkLength,
                now),
            CancellationToken.None);
        Assert.Equal(UploadChunkSaveDisposition.Replay, replayAtLimit.Disposition);

        var rejected = await store.SaveChunkAsync(
            new UploadChunk(
                jobId,
                1,
                5,
                6,
                TestHash(6),
                pageHash,
                "image/jpeg",
                "chunks/5",
                1,
                now),
            CancellationToken.None);

        Assert.Equal(UploadChunkSaveDisposition.PageSizeExceeded, rejected.Disposition);
        var persisted = await store.GetChunksAsync(jobId, 1, CancellationToken.None);
        Assert.Equal(5, persisted.Count);
        Assert.Equal(20L * 1024 * 1024, persisted.Sum(chunk => chunk.Length));
    }

    [Fact]
    public async Task ConcurrentReplaysOfLastStoredChunk_FinalizeOnceWithoutOrphans()
    {
        var content = "last chunk replayed concurrently"u8.ToArray();
        var hash = Sha256(content);
        var persistedReference = await objectStore.WriteAsync(
            "chunks",
            jobId,
            "page-001-chunk-0000",
            content,
            CancellationToken.None);
        var initialSave = await store.SaveChunkAsync(
            new UploadChunk(
                jobId,
                1,
                0,
                1,
                hash,
                hash,
                "image/png",
                persistedReference,
                content.Length,
                now),
            CancellationToken.None);
        Assert.Equal(UploadChunkSaveDisposition.Stored, initialSave.Disposition);
        var releaseInspection = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var inspectionCount = 0;
        fileInspector.BeforeInspectAsync = () =>
        {
            if (Interlocked.Increment(ref inspectionCount) == 2)
            {
                releaseInspection.TrySetResult();
            }
            return releaseInspection.Task.WaitAsync(TimeSpan.FromSeconds(10));
        };

        var statuses = await Task.WhenAll(
            Enumerable.Range(0, 2).Select(_ => workflow.UploadChunkAsync(
                deviceId,
                jobId,
                page: 1,
                chunkIndex: 0,
                chunkCount: 1,
                hash,
                hash,
                "image/png",
                content,
                CancellationToken.None)));

        Assert.All(statuses, status => Assert.True(status.Complete));
        Assert.Equal(2, inspectionCount);
        Assert.Empty(await store.GetChunksAsync(jobId, 1, CancellationToken.None));
        Assert.Single(await store.GetPagesAsync(jobId, CancellationToken.None));
        var job = await store.GetJobAsync(jobId, CancellationToken.None);
        Assert.NotNull(job);
        Assert.Equal(InvoiceJobState.LocallyValidated, job.State);
        Assert.Equal(1, objectStore.Count);
    }

    private async Task CreateJobAsync(int expectedPageCount)
    {
        await store.CreateJobAsync(
            new InvoiceJob(
                jobId,
                deviceId,
                InvoiceJobState.Captured,
                expectedPageCount,
                0,
                null,
                null,
                now,
                now),
            CancellationToken.None);
    }

    private static string Sha256(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    private static string TestHash(int value) => value.ToString("x64");

    private sealed class TrackingObjectStore : IDocumentObjectStore
    {
        private readonly ConcurrentDictionary<string, byte[]> objects = new();
        private int sequence;

        public Func<string, Task>? AfterWriteAsync { get; set; }

        public int Count => objects.Count;

        public async Task<string> WriteAsync(
            string category,
            Guid objectJobId,
            string objectName,
            ReadOnlyMemory<byte> plaintext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reference =
                $"{category}/{objectJobId:D}/{objectName}-{Interlocked.Increment(ref sequence)}";
            Assert.True(objects.TryAdd(reference, plaintext.ToArray()));
            if (AfterWriteAsync is not null)
            {
                await AfterWriteAsync(category);
            }
            return reference;
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

    private sealed class AcceptingFileInspector : IFileSafetyInspector
    {
        public Func<Task>? BeforeInspectAsync { get; set; }

        public async Task<FileInspection> InspectAsync(
            ReadOnlyMemory<byte> content,
            string claimedMimeType,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (BeforeInspectAsync is not null)
            {
                await BeforeInspectAsync();
            }
            return new FileInspection(claimedMimeType, content.Length, 1, 1, []);
        }
    }

    private sealed class UnusedSaasClient : ISaasClient
    {
        public Task<string> GetEntitlementAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The SaaS client is not used during chunk upload.");

        public Task<SaasOcrResponse> ProcessOcrAsync(
            Guid ocrJobId,
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
