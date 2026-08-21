using PharmaAuto.Connector.Application;
using PharmaAuto.Connector.Domain;

namespace PharmaAuto.Connector.LocalApi;

public sealed class SidecarInitializationService(
    ISidecarStore store,
    IInvoiceWorkflowQueue queue,
    ILogger<SidecarInitializationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
        var pending = await store.ListJobsByStateAsync(
            [InvoiceJobState.LocallyValidated],
            1000,
            cancellationToken);
        foreach (var job in pending)
        {
            await queue.EnqueueAsync(job.JobId, cancellationToken);
        }
        logger.LogInformation(
            "Connector Sidecar initialized; {PendingCount} pre-OCR jobs were requeued.",
            pending.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
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
