using Microsoft.Data.Sqlite;
using PharmaAuto.Connector.Domain;
using PharmaAuto.Connector.Infrastructure;

namespace PharmaAuto.Connector.LocalApi.Tests;

public sealed class SqliteSidecarStoreTests : IAsyncLifetime
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        "pharma-auto-sidecar-tests",
        Guid.NewGuid().ToString("N"));
    private SqliteSidecarStore store = null!;
    private string databasePath = null!;

    public async Task InitializeAsync()
    {
        databasePath = Path.Combine(testDirectory, "sidecar.db");
        store = new SqliteSidecarStore(databasePath);
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

    [Fact]
    public async Task PublishRevision_RollsBackWhenJobStateChanged()
    {
        var fixture = await CreateJobAsync(InvoiceJobState.Matching);
        var revision = CreateRevision(fixture.JobId, fixture.DeviceId, 1);

        var published = await store.SaveRevisionAndTransitionJobAsync(
            revision,
            InvoiceJobState.AwaitingUserReview,
            InvoiceJobState.AwaitingUserReview,
            null,
            fixture.Now,
            CancellationToken.None);

        Assert.False(published);
        Assert.Null(await store.GetRevisionAsync(revision.RevisionId, CancellationToken.None));
        var job = await store.GetJobAsync(fixture.JobId, CancellationToken.None);
        Assert.NotNull(job);
        Assert.Equal(InvoiceJobState.Matching, job.State);
        Assert.Null(job.CurrentRevisionId);
    }

    [Fact]
    public async Task PublishEditedRevision_RejectsAStaleCurrentRevision()
    {
        var fixture = await CreateJobAsync(InvoiceJobState.AwaitingUserReview);
        var first = CreateRevision(fixture.JobId, fixture.DeviceId, 1);
        Assert.True(await store.SaveRevisionAndTransitionJobAsync(
            first,
            InvoiceJobState.AwaitingUserReview,
            InvoiceJobState.AwaitingUserReview,
            null,
            fixture.Now,
            CancellationToken.None));
        var staleSource = Guid.NewGuid();
        var second = CreateRevision(fixture.JobId, fixture.DeviceId, 2);

        var published = await store.SaveRevisionAndTransitionJobAsync(
            second,
            InvoiceJobState.AwaitingUserReview,
            InvoiceJobState.AwaitingUserReview,
            staleSource,
            fixture.Now.AddMinutes(1),
            CancellationToken.None);

        Assert.False(published);
        Assert.Null(await store.GetRevisionAsync(second.RevisionId, CancellationToken.None));
        var job = await store.GetJobAsync(fixture.JobId, CancellationToken.None);
        Assert.NotNull(job);
        Assert.Equal(first.RevisionId, job.CurrentRevisionId);
    }

    [Fact]
    public async Task PublishEditedRevision_RollsBackJobPointerWhenRevisionInsertFails()
    {
        var fixture = await CreateJobAsync(InvoiceJobState.AwaitingUserReview);
        var first = CreateRevision(fixture.JobId, fixture.DeviceId, 1);
        Assert.True(await store.SaveRevisionAndTransitionJobAsync(
            first,
            InvoiceJobState.AwaitingUserReview,
            InvoiceJobState.AwaitingUserReview,
            null,
            fixture.Now,
            CancellationToken.None));
        var duplicateNumber = CreateRevision(fixture.JobId, fixture.DeviceId, 1);

        await Assert.ThrowsAsync<SqliteException>(() =>
            store.SaveRevisionAndTransitionJobAsync(
                duplicateNumber,
                InvoiceJobState.AwaitingUserReview,
                InvoiceJobState.AwaitingUserReview,
                first.RevisionId,
                fixture.Now.AddMinutes(1),
                CancellationToken.None));

        var job = await store.GetJobAsync(fixture.JobId, CancellationToken.None);
        Assert.NotNull(job);
        Assert.Equal(first.RevisionId, job.CurrentRevisionId);
        Assert.Null(await store.GetRevisionAsync(
            duplicateNumber.RevisionId,
            CancellationToken.None));
    }

    [Fact]
    public async Task PublishRevision_RejectsARevisionCreatedByAnotherDevice()
    {
        var fixture = await CreateJobAsync(InvoiceJobState.Matching);
        var otherDevice = Guid.NewGuid();
        await store.SaveDeviceAsync(
            new DeviceRegistration(
                otherDevice,
                "Other atomic workflow test device",
                [4, 5, 6],
                fixture.Now,
                null,
                fixture.Now),
            CancellationToken.None);
        var revision = CreateRevision(fixture.JobId, otherDevice, 1);

        var published = await store.SaveRevisionAndTransitionJobAsync(
            revision,
            InvoiceJobState.Matching,
            InvoiceJobState.AwaitingUserReview,
            null,
            fixture.Now,
            CancellationToken.None);

        Assert.False(published);
        Assert.Null(await store.GetRevisionAsync(revision.RevisionId, CancellationToken.None));
        var job = await store.GetJobAsync(fixture.JobId, CancellationToken.None);
        Assert.NotNull(job);
        Assert.Equal(InvoiceJobState.Matching, job.State);
        Assert.Null(job.CurrentRevisionId);
    }

    [Fact]
    public async Task ConfirmRevision_IsAtomicAndIdempotent()
    {
        var fixture = await CreateJobAsync(InvoiceJobState.AwaitingUserReview);
        var revision = CreateRevision(fixture.JobId, fixture.DeviceId, 1);
        Assert.True(await store.SaveRevisionAndTransitionJobAsync(
            revision,
            InvoiceJobState.AwaitingUserReview,
            InvoiceJobState.AwaitingUserReview,
            null,
            fixture.Now,
            CancellationToken.None));
        var audit = CreateConfirmationAudit(fixture, revision.RevisionId);

        Assert.True(await store.ConfirmRevisionAndTransitionJobAsync(
            revision.RevisionId,
            fixture.JobId,
            fixture.DeviceId,
            fixture.Now.AddMinutes(1),
            audit,
            CancellationToken.None));
        Assert.True(await store.ConfirmRevisionAndTransitionJobAsync(
            revision.RevisionId,
            fixture.JobId,
            fixture.DeviceId,
            fixture.Now.AddMinutes(2),
            CreateConfirmationAudit(fixture, revision.RevisionId) with
            {
                OccurredAt = fixture.Now.AddMinutes(2)
            },
            CancellationToken.None));

        var job = await store.GetJobAsync(fixture.JobId, CancellationToken.None);
        var confirmed = await store.GetRevisionAsync(revision.RevisionId, CancellationToken.None);
        Assert.NotNull(job);
        Assert.NotNull(confirmed);
        Assert.Equal(InvoiceJobState.Confirmed, job.State);
        Assert.Equal(revision.RevisionId, job.CurrentRevisionId);
        Assert.Equal("CONFIRMED", confirmed.Status);
        Assert.NotNull(confirmed.ConfirmedAt);
        Assert.Equal(1L, await CountAuditEventsAsync(fixture.JobId));
    }

    [Fact]
    public async Task ConfirmRevision_DoesNotAcceptConfirmedStateWithoutItsAudit()
    {
        var fixture = await CreateJobAsync(InvoiceJobState.AwaitingUserReview);
        var revision = CreateRevision(fixture.JobId, fixture.DeviceId, 1);
        Assert.True(await store.SaveRevisionAndTransitionJobAsync(
            revision,
            InvoiceJobState.AwaitingUserReview,
            InvoiceJobState.AwaitingUserReview,
            null,
            fixture.Now,
            CancellationToken.None));
        Assert.True(await store.ConfirmRevisionAndTransitionJobAsync(
            revision.RevisionId,
            fixture.JobId,
            fixture.DeviceId,
            fixture.Now.AddMinutes(1),
            CreateConfirmationAudit(fixture, revision.RevisionId),
            CancellationToken.None));
        await DeleteAuditEventsAsync(fixture.JobId);

        var accepted = await store.ConfirmRevisionAndTransitionJobAsync(
            revision.RevisionId,
            fixture.JobId,
            fixture.DeviceId,
            fixture.Now.AddMinutes(2),
            CreateConfirmationAudit(fixture, revision.RevisionId) with
            {
                OccurredAt = fixture.Now.AddMinutes(2)
            },
            CancellationToken.None);

        Assert.False(accepted);
        Assert.Equal(0L, await CountAuditEventsAsync(fixture.JobId));
    }

    private async Task<TestFixture> CreateJobAsync(InvoiceJobState state)
    {
        var now = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        var deviceId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        await store.SaveDeviceAsync(
            new DeviceRegistration(
                deviceId,
                "Atomic workflow test device",
                [1, 2, 3],
                now,
                null,
                now),
            CancellationToken.None);
        await store.CreateJobAsync(
            new InvoiceJob(jobId, deviceId, state, 1, 0, null, null, now, now),
            CancellationToken.None);
        return new TestFixture(jobId, deviceId, now);
    }

    private static InvoiceRevisionRecord CreateRevision(Guid jobId, Guid deviceId, int number) =>
        new(
            Guid.NewGuid(),
            jobId,
            number,
            "AWAITING_USER_REVIEW",
            "{}",
            deviceId,
            new DateTimeOffset(2026, 8, 23, 12, number, 0, TimeSpan.Zero),
            null);

    private static AuditRecord CreateConfirmationAudit(
        TestFixture fixture,
        Guid revisionId) =>
        new(
            Guid.NewGuid(),
            "DEVICE",
            fixture.DeviceId.ToString("D"),
            "REVISION_CONFIRMED_READ_ONLY",
            revisionId.ToString("D"),
            "SUCCESS_NO_GENIUS_WRITE",
            fixture.JobId,
            fixture.Now.AddMinutes(1));

    private async Task<long> CountAuditEventsAsync(Guid jobId)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM audit_events WHERE correlation_id = $correlation_id;";
        command.Parameters.AddWithValue("$correlation_id", jobId.ToString("D"));
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task DeleteAuditEventsAsync(Guid jobId)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM audit_events WHERE correlation_id = $correlation_id;";
        command.Parameters.AddWithValue("$correlation_id", jobId.ToString("D"));
        await command.ExecuteNonQueryAsync();
    }

    private sealed record TestFixture(Guid JobId, Guid DeviceId, DateTimeOffset Now);
}
