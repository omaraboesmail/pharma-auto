using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using PharmaAuto.Connector.Application;
using PharmaAuto.Connector.Domain;
using PharmaAuto.Connector.Infrastructure;

namespace PharmaAuto.Connector.LocalApi.Tests;

public sealed class SidecarInitializationServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        "pharma-auto-sidecar-recovery-tests",
        Guid.NewGuid().ToString("N"));
    private SqliteSidecarStore store = null!;

    public async Task InitializeAsync()
    {
        store = new SqliteSidecarStore(Path.Combine(testDirectory, "sidecar.db"));
        await store.InitializeAsync(CancellationToken.None);
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

    [Theory]
    [InlineData(InvoiceJobState.OcrReserved, InvoiceJobState.OcrFailed)]
    [InlineData(InvoiceJobState.OcrProcessing, InvoiceJobState.OcrFailed)]
    [InlineData(InvoiceJobState.OcrValidated, InvoiceJobState.MatchingFailed)]
    [InlineData(InvoiceJobState.Matching, InvoiceJobState.MatchingFailed)]
    public async Task Start_RecoversInterruptedStateBeforeRequeueing(
        InvoiceJobState interruptedState,
        InvoiceJobState expectedRecoveredState)
    {
        var jobId = await CreateJobAsync(interruptedState);
        var queue = new CapturingQueue();
        var service = new SidecarInitializationService(
            store,
            queue,
            new FixedTimeProvider(Now),
            NullLogger<SidecarInitializationService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await queue.WaitForCountAsync(1, TimeSpan.FromSeconds(10));

        var recovered = await store.GetJobAsync(jobId, CancellationToken.None);
        Assert.NotNull(recovered);
        Assert.Equal(expectedRecoveredState, recovered.State);
        Assert.Equal("CONNECTOR_RESTART_RECOVERY", recovered.FailureCode);
        Assert.Equal(Now, recovered.UpdatedAt);
        Assert.Equal([jobId], queue.JobIds);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Start_RequeuesEveryJobBeyondSingleStorePageWithoutDuplicates()
    {
        const int jobCount = 1005;
        var deviceId = await CreateDeviceAsync();
        var expected = new HashSet<Guid>();
        for (var index = 0; index < jobCount; index++)
        {
            expected.Add(await CreateJobAsync(InvoiceJobState.LocallyValidated, deviceId));
        }
        var queue = new CapturingQueue();
        var service = new SidecarInitializationService(
            store,
            queue,
            new FixedTimeProvider(Now),
            NullLogger<SidecarInitializationService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await queue.WaitForCountAsync(jobCount, TimeSpan.FromSeconds(30));

        Assert.Equal(jobCount, queue.JobIds.Count);
        Assert.Equal(expected, queue.JobIds.ToHashSet());
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Start_DoesNotRetryPreviouslyFailedJobsWithoutExplicitSubmission()
    {
        _ = await CreateJobAsync(InvoiceJobState.OcrFailed);
        _ = await CreateJobAsync(InvoiceJobState.MatchingFailed);
        var durablePendingJobId = await CreateJobAsync(InvoiceJobState.LocallyValidated);
        var queue = new CapturingQueue();
        var service = new SidecarInitializationService(
            store,
            queue,
            new FixedTimeProvider(Now),
            NullLogger<SidecarInitializationService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await queue.WaitForJobAsync(durablePendingJobId, TimeSpan.FromSeconds(10));

        Assert.Equal([durablePendingJobId], queue.JobIds);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Start_ReturnsWhileRecoveryQueueIsBackpressured()
    {
        var deviceId = await CreateDeviceAsync();
        _ = await CreateJobAsync(InvoiceJobState.LocallyValidated, deviceId);
        _ = await CreateJobAsync(InvoiceJobState.LocallyValidated, deviceId);
        var queue = new BackpressuredQueue();
        var service = new SidecarInitializationService(
            store,
            queue,
            new FixedTimeProvider(Now),
            NullLogger<SidecarInitializationService>.Instance);

        await service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await queue.FirstEnqueue.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
    }

    private async Task<Guid> CreateDeviceAsync()
    {
        var deviceId = Guid.NewGuid();
        await store.SaveDeviceAsync(
            new DeviceRegistration(
                deviceId,
                "Restart recovery test device",
                [1, 2, 3],
                Now.AddMinutes(-5),
                null,
                Now.AddMinutes(-5)),
            CancellationToken.None);
        return deviceId;
    }

    private async Task<Guid> CreateJobAsync(
        InvoiceJobState state,
        Guid? existingDeviceId = null)
    {
        var deviceId = existingDeviceId ?? await CreateDeviceAsync();
        var jobId = Guid.NewGuid();
        await store.CreateJobAsync(
            new InvoiceJob(
                jobId,
                deviceId,
                state,
                1,
                1,
                null,
                null,
                Now.AddMinutes(-5),
                Now.AddMinutes(-5)),
            CancellationToken.None);
        return jobId;
    }

    private sealed class CapturingQueue : IInvoiceWorkflowQueue
    {
        private readonly ConcurrentQueue<Guid> jobIds = new();
        private readonly SemaphoreSlim enqueueSignal = new(0);

        public IReadOnlyList<Guid> JobIds => jobIds.ToArray();

        public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            jobIds.Enqueue(jobId);
            enqueueSignal.Release();
            return ValueTask.CompletedTask;
        }

        public async Task WaitForCountAsync(int count, TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            for (var index = 0; index < count; index++)
            {
                await enqueueSignal.WaitAsync(cancellation.Token);
            }
        }

        public async Task WaitForJobAsync(Guid jobId, TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            while (!jobIds.Contains(jobId))
            {
                await enqueueSignal.WaitAsync(cancellation.Token);
            }
        }

        public async IAsyncEnumerable<Guid> ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            foreach (var jobId in jobIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return jobId;
                await Task.Yield();
            }
        }
    }

    private sealed class BackpressuredQueue : IInvoiceWorkflowQueue
    {
        private readonly Channel<Guid> channel = Channel.CreateBounded<Guid>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        private readonly TaskCompletionSource firstEnqueue = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstEnqueue => firstEnqueue.Task;

        public async ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken)
        {
            await channel.Writer.WriteAsync(jobId, cancellationToken);
            firstEnqueue.TrySetResult();
        }

        public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
            channel.Reader.ReadAllAsync(cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
