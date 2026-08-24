using PharmaAuto.Saas.Application;
using PharmaAuto.Saas.Domain;
using PharmaAuto.Saas.Infrastructure;

namespace PharmaAuto.Saas.Application.Tests;

public sealed class OcrOrchestratorTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ConnectorId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset Now =
        new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Process_CompletesAndSettlesExactlyOnceAcrossReplay()
    {
        var store = CreateStore();
        var provider = new ScriptedProvider([ProviderSuccess()]);
        var orchestrator = new OcrOrchestrator(store, provider, new FixedTimeProvider(Now));
        var document = CreateDocument(Guid.NewGuid(), "invoice-one");

        var first = await orchestrator.ProcessAsync(
            TenantId,
            ConnectorId,
            document,
            CancellationToken.None);
        var replay = await orchestrator.ProcessAsync(
            TenantId,
            ConnectorId,
            document,
            CancellationToken.None);
        var entitlement = await store.GetEntitlementAsync(
            TenantId,
            ConnectorId,
            Now,
            CancellationToken.None);

        Assert.Equal(OcrJobState.Completed, first.State);
        Assert.Equal(first, replay);
        Assert.Equal(1, provider.CallCount);
        Assert.NotNull(entitlement);
        Assert.Equal(0, entitlement.PagesReserved);
        Assert.Equal(1, entitlement.PagesSettled);
    }

    [Fact]
    public async Task Process_ReactivatesReleasedReservationAndChargesSuccessfulRetry()
    {
        var store = CreateStore();
        var provider = new ScriptedProvider(
        [
            new OcrProviderException("PROVIDER_TIMEOUT", "Synthetic first-attempt timeout."),
            ProviderSuccess()
        ]);
        var orchestrator = new OcrOrchestrator(store, provider, new FixedTimeProvider(Now));
        var document = CreateDocument(Guid.NewGuid(), "invoice-two");

        await Assert.ThrowsAsync<OcrProviderException>(() => orchestrator.ProcessAsync(
            TenantId,
            ConnectorId,
            document,
            CancellationToken.None));
        var afterFailure = await store.GetEntitlementAsync(
            TenantId,
            ConnectorId,
            Now,
            CancellationToken.None);
        var completed = await orchestrator.ProcessAsync(
            TenantId,
            ConnectorId,
            document,
            CancellationToken.None);
        var afterRetry = await store.GetEntitlementAsync(
            TenantId,
            ConnectorId,
            Now,
            CancellationToken.None);

        Assert.NotNull(afterFailure);
        Assert.Equal(0, afterFailure.PagesReserved);
        Assert.Equal(0, afterFailure.PagesSettled);
        Assert.Equal(OcrJobState.Completed, completed.State);
        Assert.NotNull(afterRetry);
        Assert.Equal(0, afterRetry.PagesReserved);
        Assert.Equal(1, afterRetry.PagesSettled);
        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task Process_ReleasesQuotaForUnexpectedProviderFailures()
    {
        var store = CreateStore();
        var provider = new ScriptedProvider(
            [new InvalidOperationException("Synthetic malformed provider projection.")]);
        var orchestrator = new OcrOrchestrator(store, provider, new FixedTimeProvider(Now));
        var document = CreateDocument(Guid.NewGuid(), "invoice-three");

        var exception = await Assert.ThrowsAsync<OcrProviderException>(() =>
            orchestrator.ProcessAsync(
                TenantId,
                ConnectorId,
                document,
                CancellationToken.None));
        var entitlement = await store.GetEntitlementAsync(
            TenantId,
            ConnectorId,
            Now,
            CancellationToken.None);
        var failed = await store.GetOcrJobAsync(
            TenantId,
            document.JobId,
            CancellationToken.None);

        Assert.Equal("OCR_PROVIDER_UNEXPECTED", exception.Code);
        Assert.NotNull(entitlement);
        Assert.Equal(0, entitlement.PagesReserved);
        Assert.Equal(0, entitlement.PagesSettled);
        Assert.NotNull(failed);
        Assert.Equal(OcrJobState.Failed, failed.State);
    }

    [Fact]
    public async Task Process_RejectsJobIdReplayWithDifferentSource()
    {
        var store = CreateStore();
        var provider = new ScriptedProvider([ProviderSuccess()]);
        var orchestrator = new OcrOrchestrator(store, provider, new FixedTimeProvider(Now));
        var jobId = Guid.NewGuid();
        var first = CreateDocument(jobId, "invoice-four-a");
        var changed = CreateDocument(jobId, "invoice-four-b");
        await orchestrator.ProcessAsync(
            TenantId,
            ConnectorId,
            first,
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.ProcessAsync(
            TenantId,
            ConnectorId,
            changed,
            CancellationToken.None));
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task Process_RejectsConcurrentDuplicateWhileAttemptLeaseIsActive()
    {
        var store = CreateStore();
        var provider = new BlockingProvider();
        var orchestrator = new OcrOrchestrator(store, provider, new FixedTimeProvider(Now));
        var document = CreateDocument(Guid.NewGuid(), "invoice-five");

        var first = orchestrator.ProcessAsync(
            TenantId,
            ConnectorId,
            document,
            CancellationToken.None);
        await provider.Started.WaitAsync(TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.ProcessAsync(
                TenantId,
                ConnectorId,
                document,
                CancellationToken.None));

        Assert.Contains("active attempt", exception.Message, StringComparison.OrdinalIgnoreCase);
        provider.Release();
        var completed = await first;
        var entitlement = await store.GetEntitlementAsync(
            TenantId,
            ConnectorId,
            Now,
            CancellationToken.None);
        Assert.Equal(OcrJobState.Completed, completed.State);
        Assert.Equal(1, provider.CallCount);
        Assert.NotNull(entitlement);
        Assert.Equal(0, entitlement.PagesReserved);
        Assert.Equal(1, entitlement.PagesSettled);
    }

    [Fact]
    public async Task StaleAttempt_CannotFinalizeARecoveredAttempt()
    {
        var store = CreateStore();
        var document = CreateDocument(Guid.NewGuid(), "invoice-six");
        var first = await store.StartOcrJobAsync(
            TenantId,
            ConnectorId,
            document.JobId,
            document.Pages.Count,
            document.SourceSha256,
            Now,
            Now.AddMinutes(-15),
            CancellationToken.None);
        var recoveredAt = Now.AddMinutes(16);
        var recovered = await store.StartOcrJobAsync(
            TenantId,
            ConnectorId,
            document.JobId,
            document.Pages.Count,
            document.SourceSha256,
            recoveredAt,
            recoveredAt.AddMinutes(-15),
            CancellationToken.None);
        var staleCompletion = first.Job with
        {
            State = OcrJobState.Completed,
            ResultJson = "{\"schemaVersion\":\"1.0\"}",
            ProviderModel = "stale-model",
            UpdatedAt = recoveredAt
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CompleteOcrJobAsync(
            staleCompletion,
            first.AttemptId,
            CreateAudit(document.JobId, "OCR_SETTLED", "SUCCESS", recoveredAt),
            CancellationToken.None));

        var duringRecovery = await store.GetEntitlementAsync(
            TenantId,
            ConnectorId,
            recoveredAt,
            CancellationToken.None);
        Assert.NotEqual(first.AttemptId, recovered.AttemptId);
        Assert.NotNull(duringRecovery);
        Assert.Equal(1, duringRecovery.PagesReserved);
        Assert.Equal(0, duringRecovery.PagesSettled);

        var completed = recovered.Job with
        {
            State = OcrJobState.Completed,
            ResultJson = "{\"schemaVersion\":\"1.0\"}",
            ProviderModel = "recovered-model",
            UpdatedAt = recoveredAt
        };
        _ = await store.CompleteOcrJobAsync(
            completed,
            recovered.AttemptId,
            CreateAudit(document.JobId, "OCR_SETTLED", "SUCCESS", recoveredAt),
            CancellationToken.None);
        var afterCompletion = await store.GetEntitlementAsync(
            TenantId,
            ConnectorId,
            recoveredAt,
            CancellationToken.None);
        Assert.NotNull(afterCompletion);
        Assert.Equal(0, afterCompletion.PagesReserved);
        Assert.Equal(1, afterCompletion.PagesSettled);

        var replay = await store.StartOcrJobAsync(
            TenantId,
            ConnectorId,
            document.JobId,
            document.Pages.Count,
            document.SourceSha256,
            recoveredAt.AddMinutes(1),
            recoveredAt.AddMinutes(-14),
            CancellationToken.None);
        Assert.Equal(recovered.AttemptId, replay.AttemptId);
        Assert.Equal(OcrJobState.Completed, replay.Job.State);
        Assert.Equal(completed.ResultJson, replay.Job.ResultJson);
    }

    [Fact]
    public async Task Process_RejectsRawPdfBeforeReservingQuota()
    {
        var store = CreateStore();
        var provider = new ScriptedProvider([ProviderSuccess()]);
        var orchestrator = new OcrOrchestrator(store, provider, new FixedTimeProvider(Now));
        var bytes = "%PDF-1.7 synthetic"u8.ToArray();
        var page = new OcrDocumentPage(
            1,
            "application/pdf",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            bytes);
        var document = new OcrDocument(
            Guid.NewGuid(),
            OcrOrchestrator.ComputeLogicalDocumentSha256([page]),
            [page]);

        await Assert.ThrowsAsync<ArgumentException>(() => orchestrator.ProcessAsync(
            TenantId,
            ConnectorId,
            document,
            CancellationToken.None));
        var entitlement = await store.GetEntitlementAsync(
            TenantId,
            ConnectorId,
            Now,
            CancellationToken.None);

        Assert.Equal(0, provider.CallCount);
        Assert.NotNull(entitlement);
        Assert.Equal(0, entitlement.PagesReserved);
        Assert.Equal(0, entitlement.PagesSettled);
    }

    private static InMemorySaasStore CreateStore() => new(
        new InMemorySaasSeed(
            new ConnectorRegistration(
                ConnectorId,
                TenantId,
                "Test connector",
                null,
                false),
            new SubscriptionEntitlement(
                Guid.NewGuid(),
                TenantId,
                ConnectorId,
                SubscriptionStatus.Active,
                Now.AddDays(-1),
                Now.AddDays(1),
                Now.AddDays(-1),
                Now.AddDays(1),
                10,
                0,
                0,
                true),
            []));

    private static OcrDocument CreateDocument(Guid jobId, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var page = new OcrDocumentPage(
            1,
            "image/png",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),
            bytes);
        return new OcrDocument(
            jobId,
            OcrOrchestrator.ComputeLogicalDocumentSha256([page]),
            [page]);
    }

    private static OcrProviderResult ProviderSuccess() => new(
        "synthetic-model",
        "{\"schemaVersion\":\"1.0\"}",
        10,
        5,
        Now);

    private static AuditEvent CreateAudit(
        Guid jobId,
        string action,
        string result,
        DateTimeOffset occurredAt) =>
        new(
            Guid.NewGuid(),
            TenantId,
            "CONNECTOR",
            ConnectorId.ToString("D"),
            action,
            jobId.ToString("D"),
            result,
            jobId,
            occurredAt);

    private sealed class ScriptedProvider(IReadOnlyList<object> outcomes) : IOcrProvider
    {
        private int nextOutcome;

        public string ProviderName => "Scripted";

        public int CallCount { get; private set; }

        public Task<OcrProviderResult> ExtractAsync(
            OcrDocument document,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = document;
            var outcome = outcomes[Math.Min(nextOutcome++, outcomes.Count - 1)];
            CallCount++;
            return outcome switch
            {
                OcrProviderResult result => Task.FromResult(result),
                Exception exception => Task.FromException<OcrProviderResult>(exception),
                _ => throw new InvalidOperationException("Unknown scripted OCR outcome.")
            };
        }
    }

    private sealed class BlockingProvider : IOcrProvider
    {
        private readonly TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string ProviderName => "Blocking";

        public int CallCount { get; private set; }

        public Task Started => started.Task;

        public void Release() => released.TrySetResult();

        public async Task<OcrProviderResult> ExtractAsync(
            OcrDocument document,
            CancellationToken cancellationToken)
        {
            _ = document;
            CallCount++;
            started.TrySetResult();
            await released.Task.WaitAsync(cancellationToken);
            return ProviderSuccess();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
