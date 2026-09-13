using Npgsql;
using PharmaAuto.Saas.Application;
using PharmaAuto.Saas.Domain;
using PharmaAuto.Saas.Infrastructure;

namespace PharmaAuto.Saas.Application.Tests;

[Collection("PostgreSQL integration")]
public sealed class PostgresSaasStoreIntegrationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

    [PostgresFact]
    public async Task Migrations_FreshPathAndReplayProduceTheHardenedSchema()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();

        await database.ApplyMigrationAsync("001-phase-1.sql");
        await database.ApplyMigrationAsync("002-phase-1-hardening.sql");
        await database.ApplyMigrationAsync("002-phase-1-hardening.sql");

        Assert.True(await database.ScalarAsync<bool>("""
            SELECT attribute.attnotnull
            FROM pg_attribute AS attribute
            WHERE attribute.attrelid = 'ocr_jobs'::regclass
              AND attribute.attname = 'attempt_id';
            """));
        Assert.Equal(1, await database.ScalarAsync<int>("""
            SELECT count(*)::integer
            FROM pg_constraint
            WHERE conrelid = 'ocr_jobs'::regclass
              AND conname = 'ck_ocr_jobs_state_payload';
            """));
        Assert.Equal(1, await database.ScalarAsync<int>("""
            SELECT count(*)::integer
            FROM pg_constraint
            WHERE conrelid = 'subscription_periods'::regclass
              AND contype = 'x';
            """));
        Assert.Contains(
            "canonical_product_search_vector",
            await database.ScalarAsync<string>("""
                SELECT pg_get_expr(definition.adbin, definition.adrelid)
                FROM pg_attribute AS attribute
                JOIN pg_attrdef AS definition
                  ON definition.adrelid = attribute.attrelid
                 AND definition.adnum = attribute.attnum
                WHERE attribute.attrelid = 'canonical_products'::regclass
                  AND attribute.attname = 'search_vector';
                """),
            StringComparison.Ordinal);

        var tenantId = Guid.NewGuid();
        var connectorId = Guid.NewGuid();
        await database.SeedTenantAsync(tenantId, connectorId, 10, Now);
        var entitlementId = await database.ScalarAsync<Guid>(
            "SELECT entitlement_id FROM subscription_periods WHERE tenant_id = @tenant_id;",
            new NpgsqlParameter("tenant_id", tenantId));
        var reservationId = Guid.NewGuid();
        var reservedJobId = Guid.NewGuid();
        await database.ExecuteAsync(
            """
            SELECT set_config('app.tenant_id', CAST(@tenant_id AS text), false);
            INSERT INTO quota_reservations
                (reservation_id, tenant_id, entitlement_id, job_id, page_count, reserved_at)
            VALUES
                (@reservation_id, @tenant_id, @entitlement_id, @reserved_job_id, 1, @now);
            """,
            new NpgsqlParameter("tenant_id", tenantId),
            new NpgsqlParameter("entitlement_id", entitlementId),
            new NpgsqlParameter("reservation_id", reservationId),
            new NpgsqlParameter("reserved_job_id", reservedJobId),
            new NpgsqlParameter("now", Now));
        var mismatch = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
            """
            SELECT set_config('app.tenant_id', CAST(@tenant_id AS text), false);
            INSERT INTO ocr_jobs
                (job_id, tenant_id, connector_id, page_count, source_sha256, state,
                 reservation_id, attempt_id, created_at, updated_at)
            VALUES
                (@reserved_job_id, @tenant_id, @connector_id, 2, repeat('a', 64), 'PROCESSING',
                 @reservation_id, @attempt_id, @now, @now);
            """,
            new NpgsqlParameter("reserved_job_id", reservedJobId),
            new NpgsqlParameter("tenant_id", tenantId),
            new NpgsqlParameter("connector_id", connectorId),
            new NpgsqlParameter("reservation_id", reservationId),
            new NpgsqlParameter("attempt_id", Guid.NewGuid()),
            new NpgsqlParameter("now", Now)));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, mismatch.SqlState);
    }

    [PostgresFact]
    public async Task Migration002_UpgradesAndReconcilesTheLegacySchema()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await database.ApplyMigrationAsync("001-phase-1.sql");
        await database.ExecuteAsync(LegacyDowngradeSql);

        var tenantId = Guid.NewGuid();
        var connectorId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var entitlementId = Guid.NewGuid();
        var completedJobId = Guid.NewGuid();
        var failedJobId = Guid.NewGuid();
        var processingJobId = Guid.NewGuid();
        var orphanJobId = Guid.NewGuid();
        var completedReservationId = Guid.NewGuid();
        var failedReservationId = Guid.NewGuid();
        var processingReservationId = Guid.NewGuid();
        var orphanReservationId = Guid.NewGuid();

        await database.ExecuteAsync(
            LegacySeedSql,
            new NpgsqlParameter("tenant_id", tenantId),
            new NpgsqlParameter("connector_id", connectorId),
            new NpgsqlParameter("subscription_id", subscriptionId),
            new NpgsqlParameter("entitlement_id", entitlementId),
            new NpgsqlParameter("completed_job_id", completedJobId),
            new NpgsqlParameter("failed_job_id", failedJobId),
            new NpgsqlParameter("processing_job_id", processingJobId),
            new NpgsqlParameter("orphan_job_id", orphanJobId),
            new NpgsqlParameter("completed_reservation_id", completedReservationId),
            new NpgsqlParameter("failed_reservation_id", failedReservationId),
            new NpgsqlParameter("processing_reservation_id", processingReservationId),
            new NpgsqlParameter("orphan_reservation_id", orphanReservationId),
            new NpgsqlParameter("now", Now));

        await database.ApplyMigrationAsync("002-phase-1-hardening.sql");
        await database.ApplyMigrationAsync("002-phase-1-hardening.sql");

        Assert.Equal(3, await database.ScalarAsync<int>("""
            SELECT count(*)::integer FROM ocr_jobs WHERE attempt_id = job_id;
            """));
        Assert.Equal("OCR_LEGACY_FAILURE", await database.ScalarAsync<string>("""
            SELECT failure_code FROM ocr_jobs WHERE state = 'FAILED';
            """));
        Assert.Null(await database.ScalarNullableAsync<string>("""
            SELECT failure_code FROM ocr_jobs WHERE state = 'PROCESSING';
            """));
        Assert.Equal(0, await database.ScalarAsync<int>(
            "SELECT count(*)::integer FROM quota_reservations WHERE job_id = @job_id;",
            new NpgsqlParameter("job_id", orphanJobId)));
        Assert.Equal(1, await database.ScalarAsync<int>("""
            SELECT pages_reserved FROM subscription_periods;
            """));
        Assert.Equal(2, await database.ScalarAsync<int>("""
            SELECT pages_settled FROM subscription_periods;
            """));
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT settled_at IS NOT NULL FROM quota_reservations WHERE job_id = @job_id;",
            new NpgsqlParameter("job_id", completedJobId)));
        Assert.True(await database.ScalarAsync<bool>(
            "SELECT released_at IS NOT NULL FROM quota_reservations WHERE job_id = @job_id;",
            new NpgsqlParameter("job_id", failedJobId)));
        var mismatchedJob = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(
            """
            SELECT set_config('app.tenant_id', CAST(@tenant_id AS text), false);
            INSERT INTO ocr_jobs
                (job_id, tenant_id, connector_id, page_count, source_sha256, state,
                 reservation_id, attempt_id, created_at, updated_at)
            VALUES
                (@job_id, @tenant_id, @connector_id, 2, repeat('d', 64), 'PROCESSING',
                 @reservation_id, @attempt_id, @now, @now);
            """,
            new NpgsqlParameter("job_id", Guid.NewGuid()),
            new NpgsqlParameter("tenant_id", tenantId),
            new NpgsqlParameter("connector_id", connectorId),
            new NpgsqlParameter("reservation_id", completedReservationId),
            new NpgsqlParameter("attempt_id", Guid.NewGuid()),
            new NpgsqlParameter("now", Now)));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, mismatchedJob.SqlState);
    }

    [PostgresFact]
    public async Task ForcedRls_HidesAndProtectsOtherTenantRows()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await database.ApplyAllAsync();
        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();
        await database.SeedTenantAsync(firstTenant, Guid.NewGuid(), 10, Now);
        await database.SeedTenantAsync(secondTenant, Guid.NewGuid(), 10, Now);
        var role = "pa_rls_" + Guid.NewGuid().ToString("N");
        var roleCreated = false;

        try
        {
            await database.ExecuteAsync($$"""
                CREATE ROLE "{{role}}" NOLOGIN NOSUPERUSER NOBYPASSRLS;
                GRANT USAGE ON SCHEMA "{{database.Schema}}" TO "{{role}}";
                GRANT SELECT, INSERT, UPDATE, DELETE
                    ON connector_registrations, subscriptions, subscription_periods,
                       quota_reservations, ocr_jobs, audit_events
                    TO "{{role}}";
                """);
            roleCreated = true;

            await using var connection = await database.OpenAsync();
            await using (var assumeRole = new NpgsqlCommand($$"""
                SET ROLE "{{role}}";
                SELECT set_config('app.tenant_id', @tenant_id, false);
                """, connection))
            {
                assumeRole.Parameters.AddWithValue("tenant_id", firstTenant.ToString("D"));
                await assumeRole.ExecuteNonQueryAsync();
            }
            await using (var count = new NpgsqlCommand(
                "SELECT count(*)::integer FROM connector_registrations;",
                connection))
            {
                Assert.Equal(1, (int)(await count.ExecuteScalarAsync())!);
            }
            await using (var forceRls = new NpgsqlCommand("""
                SELECT relforcerowsecurity
                FROM pg_class
                WHERE oid = 'connector_registrations'::regclass;
                """, connection))
            {
                Assert.True((bool)(await forceRls.ExecuteScalarAsync())!);
            }
            await using (var tenants = new NpgsqlCommand(
                "SELECT count(*)::integer FROM tenants;",
                connection))
            {
                var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                    tenants.ExecuteScalarAsync());
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
            }

            await using var update = new NpgsqlCommand("""
                UPDATE connector_registrations
                SET display_name = 'cross-tenant mutation'
                WHERE tenant_id = @other_tenant;
                """, connection);
            update.Parameters.AddWithValue("other_tenant", secondTenant);
            Assert.Equal(0, await update.ExecuteNonQueryAsync());
            await using var reset = new NpgsqlCommand("RESET ROLE;", connection);
            await reset.ExecuteNonQueryAsync();
        }
        finally
        {
            if (roleCreated)
            {
                await database.ExecuteAsync($$"""
                    DROP OWNED BY "{{role}}";
                    DROP ROLE IF EXISTS "{{role}}";
                    """);
            }
        }
    }

    [PostgresFact]
    public async Task AtomicQuotaAndAttemptLease_HoldUnderConcurrencyAndRecovery()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await database.ApplyAllAsync();
        var tenantId = Guid.NewGuid();
        var connectorId = Guid.NewGuid();
        await database.SeedTenantAsync(tenantId, connectorId, 5, Now);
        var store = new PostgresSaasStore(database.ConnectionString);
        var firstJobId = Guid.NewGuid();
        var secondJobId = Guid.NewGuid();
        var source = new string('a', 64);

        var starts = await Task.WhenAll(
            CaptureStartAsync(store, tenantId, connectorId, firstJobId, source),
            CaptureStartAsync(store, tenantId, connectorId, secondJobId, source));
        var successful = Assert.Single(starts, result => result.Attempt is not null);
        _ = Assert.Single(starts, result => result.Error is QuotaExceededException);
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT count(*)::integer FROM ocr_jobs WHERE tenant_id = @tenant_id;",
            new NpgsqlParameter("tenant_id", tenantId)));
        Assert.Equal(4, await database.ScalarAsync<int>(
            "SELECT pages_reserved FROM subscription_periods WHERE tenant_id = @tenant_id;",
            new NpgsqlParameter("tenant_id", tenantId)));

        var initial = successful.Attempt!;
        var recoveredAt = Now.AddMinutes(16);
        var recovered = await store.StartOcrJobAsync(
            tenantId,
            connectorId,
            initial.Job.JobId,
            4,
            source,
            recoveredAt,
            recoveredAt.AddMinutes(-15),
            CancellationToken.None);
        Assert.NotEqual(initial.AttemptId, recovered.AttemptId);

        var staleCompletion = initial.Job with
        {
            State = OcrJobState.Completed,
            ResultJson = "{\"schemaVersion\":\"1.0\"}",
            ProviderModel = "stale-model",
            UpdatedAt = recoveredAt
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CompleteOcrJobAsync(
            staleCompletion,
            initial.AttemptId,
            Audit(tenantId, connectorId, initial.Job.JobId, recoveredAt),
            CancellationToken.None));

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
            Audit(tenantId, connectorId, recovered.Job.JobId, recoveredAt),
            CancellationToken.None);

        Assert.Equal(0, await database.ScalarAsync<int>(
            "SELECT pages_reserved FROM subscription_periods WHERE tenant_id = @tenant_id;",
            new NpgsqlParameter("tenant_id", tenantId)));
        Assert.Equal(4, await database.ScalarAsync<int>(
            "SELECT pages_settled FROM subscription_periods WHERE tenant_id = @tenant_id;",
            new NpgsqlParameter("tenant_id", tenantId)));
        Assert.Equal(1, await database.ScalarAsync<int>(
            "SELECT count(*)::integer FROM audit_events WHERE tenant_id = @tenant_id;",
            new NpgsqlParameter("tenant_id", tenantId)));
    }

    private static async Task<StartResult> CaptureStartAsync(
        PostgresSaasStore store,
        Guid tenantId,
        Guid connectorId,
        Guid jobId,
        string source)
    {
        try
        {
            return new(await store.StartOcrJobAsync(
                tenantId,
                connectorId,
                jobId,
                4,
                source,
                Now,
                Now.AddMinutes(-15),
                CancellationToken.None), null);
        }
        catch (Exception exception)
        {
            return new(null, exception);
        }
    }

    private static AuditEvent Audit(
        Guid tenantId,
        Guid connectorId,
        Guid jobId,
        DateTimeOffset occurredAt) =>
        new(
            Guid.NewGuid(),
            tenantId,
            "CONNECTOR",
            connectorId.ToString("D"),
            "OCR_SETTLED",
            jobId.ToString("D"),
            "SUCCESS",
            jobId,
            occurredAt);

    private sealed record StartResult(OcrProcessingAttempt? Attempt, Exception? Error);

    private const string LegacyDowngradeSql = """
        DO $downgrade$
        DECLARE
            constraint_record record;
        BEGIN
            FOR constraint_record IN
                SELECT constraint_name.conname, table_name.relname
                FROM pg_constraint AS constraint_name
                JOIN pg_class AS table_name ON table_name.oid = constraint_name.conrelid
                WHERE constraint_name.connamespace = current_schema()::regnamespace
                  AND (
                      constraint_name.conname = 'ck_ocr_jobs_state_payload'
                      OR constraint_name.contype = 'x'
                      OR constraint_name.contype = 'f'
                         AND array_length(constraint_name.conkey, 1) > 1
                      OR constraint_name.contype = 'u'
                         AND (
                             table_name.relname = 'subscriptions'
                             AND position('subscription_id' IN pg_get_constraintdef(constraint_name.oid)) > 0
                             OR table_name.relname = 'subscription_periods'
                             AND position('entitlement_id' IN pg_get_constraintdef(constraint_name.oid)) > 0
                             OR table_name.relname = 'quota_reservations'
                             AND position('reservation_id' IN pg_get_constraintdef(constraint_name.oid)) > 0
                         )
                  )
            LOOP
                EXECUTE format(
                    'ALTER TABLE %I DROP CONSTRAINT %I',
                    constraint_record.relname,
                    constraint_record.conname);
            END LOOP;

            FOR constraint_record IN
                SELECT constraint_name.conname
                FROM pg_constraint AS constraint_name
                WHERE constraint_name.conrelid = 'subscription_periods'::regclass
                  AND constraint_name.contype = 'c'
                  AND position('pages_reserved' IN pg_get_constraintdef(constraint_name.oid)) > 0
                  AND position('pages_settled' IN pg_get_constraintdef(constraint_name.oid)) > 0
            LOOP
                EXECUTE format(
                    'ALTER TABLE subscription_periods DROP CONSTRAINT %I',
                    constraint_record.conname);
            END LOOP;
        END;
        $downgrade$;

        ALTER TABLE ocr_jobs DROP COLUMN attempt_id;
        DROP INDEX ix_canonical_products_search;
        ALTER TABLE canonical_products DROP COLUMN search_vector;
        ALTER TABLE canonical_products
            ADD COLUMN search_vector tsvector GENERATED ALWAYS AS (
                to_tsvector('simple'::regconfig, display_name)
            ) STORED;
        CREATE INDEX ix_canonical_products_search
            ON canonical_products USING gin(search_vector);
        """;

    private const string LegacySeedSql = """
        SELECT set_config('app.tenant_id', CAST(@tenant_id AS text), false);
        INSERT INTO tenants (tenant_id, display_name, created_at)
        VALUES (@tenant_id, 'Legacy tenant', @now);
        INSERT INTO connector_registrations
            (connector_id, tenant_id, display_name, activated_at)
        VALUES (@connector_id, @tenant_id, 'Legacy connector', @now);
        INSERT INTO subscriptions
            (subscription_id, tenant_id, status, valid_from, valid_until,
             offline_review_allowed, created_at, updated_at)
        VALUES
            (@subscription_id, @tenant_id, 'ACTIVE', @now - interval '1 day',
             @now + interval '1 day', true, @now, @now);
        INSERT INTO subscription_periods
            (entitlement_id, subscription_id, tenant_id, period_start, period_end,
             page_limit, pages_reserved, pages_settled, updated_at)
        VALUES
            (@entitlement_id, @subscription_id, @tenant_id, @now - interval '1 day',
             @now + interval '1 day', 10, 9, 0, @now);

        INSERT INTO quota_reservations
            (reservation_id, tenant_id, entitlement_id, job_id, page_count, reserved_at)
        VALUES
            (@completed_reservation_id, @tenant_id, @entitlement_id, @completed_job_id, 2, @now),
            (@failed_reservation_id, @tenant_id, @entitlement_id, @failed_job_id, 1, @now),
            (@processing_reservation_id, @tenant_id, @entitlement_id, @processing_job_id, 1, @now),
            (@orphan_reservation_id, @tenant_id, @entitlement_id, @orphan_job_id, 3, @now);

        INSERT INTO ocr_jobs
            (job_id, tenant_id, connector_id, page_count, source_sha256, state,
             reservation_id, result_json, provider_model, failure_code, created_at, updated_at)
        VALUES
            (@completed_job_id, @tenant_id, @connector_id, 2, repeat('a', 64), 'COMPLETED',
             @completed_reservation_id, '{"schemaVersion":"1.0"}'::jsonb, 'legacy-model', NULL,
             @now, @now),
            (@failed_job_id, @tenant_id, @connector_id, 1, repeat('b', 64), 'FAILED',
             @failed_reservation_id, NULL, NULL, NULL, @now, @now),
            (@processing_job_id, @tenant_id, @connector_id, 1, repeat('c', 64), 'PROCESSING',
             @processing_reservation_id, NULL, NULL, 'OLD_FAILURE', @now, @now);
        """;
}

[CollectionDefinition("PostgreSQL integration", DisableParallelization = true)]
public sealed class PostgresIntegrationCollection
{
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public const string ConnectionStringEnvironmentVariable =
        "PHARMA_AUTO_POSTGRES_TEST_CONNECTION_STRING";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)))
        {
            Skip = $"Set {ConnectionStringEnvironmentVariable} to a disposable PostgreSQL 18 database.";
        }
    }
}

internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string administrativeConnectionString;

    private PostgresTestDatabase(
        string administrativeConnectionString,
        string connectionString,
        string schema)
    {
        this.administrativeConnectionString = administrativeConnectionString;
        ConnectionString = connectionString;
        Schema = schema;
    }

    public string ConnectionString { get; }

    public string Schema { get; }

    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        var administrativeConnectionString = Environment.GetEnvironmentVariable(
            PostgresFactAttribute.ConnectionStringEnvironmentVariable)
            ?? throw new InvalidOperationException("The PostgreSQL test connection is not configured.");
        var schema = "pa_it_" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(administrativeConnectionString)
        {
            SearchPath = $"{schema},public"
        };
        await using (var connection = new NpgsqlConnection(administrativeConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\";", connection);
            await command.ExecuteNonQueryAsync();
        }
        return new PostgresTestDatabase(
            administrativeConnectionString,
            builder.ConnectionString,
            schema);
    }

    public async Task ApplyAllAsync()
    {
        await ApplyMigrationAsync("001-phase-1.sql");
        await ApplyMigrationAsync("002-phase-1-hardening.sql");
    }

    public async Task ApplyMigrationAsync(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "DatabaseMigrations", name);
        await ExecuteAsync(await File.ReadAllTextAsync(path));
    }

    public async Task SeedTenantAsync(
        Guid tenantId,
        Guid connectorId,
        int pageLimit,
        DateTimeOffset now)
    {
        await ExecuteAsync(
            """
            SELECT set_config('app.tenant_id', CAST(@tenant_id AS text), false);
            INSERT INTO tenants (tenant_id, display_name, created_at)
            VALUES (@tenant_id, 'Integration tenant', @now);
            INSERT INTO connector_registrations
                (connector_id, tenant_id, display_name, activated_at)
            VALUES (@connector_id, @tenant_id, 'Integration connector', @now);
            INSERT INTO subscriptions
                (subscription_id, tenant_id, status, valid_from, valid_until,
                 offline_review_allowed, created_at, updated_at)
            VALUES
                (@subscription_id, @tenant_id, 'ACTIVE', @now - interval '1 day',
                 @now + interval '1 day', true, @now, @now);
            INSERT INTO subscription_periods
                (entitlement_id, subscription_id, tenant_id, period_start, period_end,
                 page_limit, pages_reserved, pages_settled, updated_at)
            VALUES
                (@entitlement_id, @subscription_id, @tenant_id, @now - interval '1 day',
                 @now + interval '1 day', @page_limit, 0, 0, @now);
            """,
            new NpgsqlParameter("tenant_id", tenantId),
            new NpgsqlParameter("connector_id", connectorId),
            new NpgsqlParameter("subscription_id", Guid.NewGuid()),
            new NpgsqlParameter("entitlement_id", Guid.NewGuid()),
            new NpgsqlParameter("page_limit", pageLimit),
            new NpgsqlParameter("now", now));
    }

    public async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task ExecuteAsync(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return (T)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The PostgreSQL assertion returned null."));
    }

    public async Task<T?> ScalarNullableAsync<T>(
        string sql,
        params NpgsqlParameter[] parameters)
        where T : class
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (T)value;
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(administrativeConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP SCHEMA IF EXISTS \"{Schema}\" CASCADE;",
            connection);
        await command.ExecuteNonQueryAsync();
    }
}
