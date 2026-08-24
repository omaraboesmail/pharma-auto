using PharmaAuto.Connector.Application;
using PharmaAuto.Connector.Domain;

namespace PharmaAuto.Connector.LocalApi;

public sealed class SidecarInitializationService(
    ISidecarStore store,
    IInvoiceWorkflowQueue queue,
    TimeProvider timeProvider,
    ILogger<SidecarInitializationService> logger) : BackgroundService
{
    private const int RecoveryBatchSize = 1000;
    private long recoveryHighWatermark;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
        recoveryHighWatermark = await store.GetJobRecoveryHighWatermarkAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        InvoiceJobState[] recoverableStates =
            [
                InvoiceJobState.LocallyValidated,
                InvoiceJobState.OcrReserved,
                InvoiceJobState.OcrProcessing,
                InvoiceJobState.OcrValidated,
                InvoiceJobState.Matching,
                InvoiceJobState.OcrFailed,
                InvoiceJobState.MatchingFailed
            ];
        var recovered = 0;
        var queued = 0;
        var afterStoreSequence = 0L;
        while (afterStoreSequence < recoveryHighWatermark)
        {
            var pending = await store.ListJobsByStatePageAsync(
                recoverableStates,
                afterStoreSequence,
                recoveryHighWatermark,
                RecoveryBatchSize,
                stoppingToken);
            if (pending.Count == 0)
            {
                break;
            }

            foreach (var item in pending)
            {
                var job = item.Job;
                var recoveryState = job.State switch
                {
                    InvoiceJobState.OcrReserved or InvoiceJobState.OcrProcessing =>
                        InvoiceJobState.OcrFailed,
                    InvoiceJobState.OcrValidated or InvoiceJobState.Matching =>
                        InvoiceJobState.MatchingFailed,
                    _ => job.State
                };
                if (recoveryState != job.State)
                {
                    if (!await store.TransitionJobAsync(
                            job.JobId,
                            job.State,
                            recoveryState,
                            timeProvider.GetUtcNow(),
                            "CONNECTOR_RESTART_RECOVERY",
                            null,
                            stoppingToken))
                    {
                        afterStoreSequence = item.StoreSequence;
                        continue;
                    }
                    recovered++;
                }
                await queue.EnqueueAsync(job.JobId, stoppingToken);
                queued++;
                afterStoreSequence = item.StoreSequence;
            }
        }
        logger.LogInformation(
            "Connector Sidecar initialized; {QueuedCount} durable jobs were requeued and " +
            "{RecoveredCount} interrupted states were recovered.",
            queued,
            recovered);
    }
}

public sealed class InvoiceWorkflowWorker(
    IInvoiceWorkflowQueue queue,
    InvoiceWorkflowService workflow,
    ILogger<InvoiceWorkflowWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await workflow.ProcessAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Read-only invoice workflow failed for job {JobId}; no raw content was logged.",
                    jobId);
            }
        }
    }
}

public sealed class DocumentRetentionWorker(
    IDocumentObjectStore objectStore,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<DocumentRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ttlHours = configuration.GetValue("Documents:TtlHours", 72);
        if (ttlHours is < 1 or > 720)
        {
            throw new InvalidOperationException("Document TTL must be between 1 and 720 hours.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var deleted = await objectStore.DeleteExpiredAsync(
                timeProvider.GetUtcNow().AddHours(-ttlHours),
                stoppingToken);
            if (deleted > 0)
            {
                logger.LogInformation("Verified deletion removed {ObjectCount} expired objects.", deleted);
            }
            await Task.Delay(TimeSpan.FromHours(1), timeProvider, stoppingToken);
        }
    }
}
