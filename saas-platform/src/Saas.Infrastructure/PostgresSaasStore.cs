using System.Data;
using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PharmaAuto.Saas.Application;
using PharmaAuto.Saas.Domain;

namespace PharmaAuto.Saas.Infrastructure;

public sealed class PostgresSaasStore(string connectionString) : ISaasStore
{
    public async Task<ConnectorRegistration?> GetConnectorAsync(
        Guid tenantId,
        Guid connectorId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT connector_id, tenant_id, display_name, certificate_thumbprint, revoked_at IS NOT NULL
            FROM connector_registrations
            WHERE connector_id = @connector_id AND tenant_id = @tenant_id;
            """;
        await using var connection = await OpenTenantAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("connector_id", connectorId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new ConnectorRegistration(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetBoolean(4));
    }

    public async Task<SubscriptionEntitlement?> GetEntitlementAsync(
        Guid tenantId,
        Guid connectorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.entitlement_id, p.tenant_id, c.connector_id, s.status,
                   s.valid_from, s.valid_until, p.period_start, p.period_end,
                   p.page_limit, p.pages_reserved, p.pages_settled, s.offline_review_allowed
            FROM connector_registrations c
            JOIN subscriptions s ON s.tenant_id = c.tenant_id
            JOIN subscription_periods p ON p.subscription_id = s.subscription_id
            WHERE c.connector_id = @connector_id
              AND c.tenant_id = @tenant_id
              AND c.revoked_at IS NULL
              AND @now >= p.period_start
              AND @now < p.period_end
            ORDER BY p.period_start DESC
            LIMIT 1;
            """;
        await using var connection = await OpenTenantAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("connector_id", connectorId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("now", now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new SubscriptionEntitlement(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            ParseStatus(reader.GetString(3)),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.GetInt32(10),
            reader.GetBoolean(11));
    }

    public async Task<QuotaReservation> ReserveQuotaAsync(
        Guid tenantId,
        Guid connectorId,
        Guid jobId,
        int pageCount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenTenantAsync(tenantId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        const string existingSql = """
            SELECT reservation_id, page_count, reserved_at, settled_at, released_at IS NOT NULL
            FROM quota_reservations
            WHERE tenant_id = @tenant_id AND job_id = @job_id;
            """;
        await using (var existingCommand = new NpgsqlCommand(existingSql, connection, transaction))
        {
            existingCommand.Parameters.AddWithValue("tenant_id", tenantId);
            existingCommand.Parameters.AddWithValue("job_id", jobId);
            await using var reader = await existingCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var existingCount = reader.GetInt32(1);
                if (existingCount != pageCount)
                {
                    throw new InvalidOperationException(
                        "The idempotent OCR reservation was replayed with a different page count.");
                }
                var existing = new QuotaReservation(
                    reader.GetGuid(0),
                    tenantId,
                    jobId,
                    existingCount,
                    reader.GetFieldValue<DateTimeOffset>(2),
                    reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                    reader.GetBoolean(4));
                await reader.DisposeAsync();
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }
        }

        const string periodSql = """
            SELECT p.entitlement_id, p.page_limit, p.pages_reserved, p.pages_settled
            FROM connector_registrations c
            JOIN subscriptions s ON s.tenant_id = c.tenant_id
            JOIN subscription_periods p ON p.subscription_id = s.subscription_id
            WHERE c.connector_id = @connector_id
              AND c.tenant_id = @tenant_id
              AND c.revoked_at IS NULL
              AND s.status = 'ACTIVE'
              AND @now >= s.valid_from AND @now < s.valid_until
              AND @now >= p.period_start AND @now < p.period_end
            FOR UPDATE OF p;
            """;
        Guid entitlementId;
        int pageLimit;
        int reserved;
        int settled;
        await using (var periodCommand = new NpgsqlCommand(periodSql, connection, transaction))
        {
            periodCommand.Parameters.AddWithValue("connector_id", connectorId);
            periodCommand.Parameters.AddWithValue("tenant_id", tenantId);
            periodCommand.Parameters.AddWithValue("now", now);
            await using var reader = await periodCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new EntitlementRejectedException("Subscription entitlement is not active.");
            }
            entitlementId = reader.GetGuid(0);
            pageLimit = reader.GetInt32(1);
            reserved = reader.GetInt32(2);
            settled = reader.GetInt32(3);
        }

        var remaining = pageLimit - reserved - settled;
        if (pageCount > remaining)
        {
            throw new QuotaExceededException(pageCount, remaining);
        }

        var reservation = new QuotaReservation(
            Guid.NewGuid(),
            tenantId,
            jobId,
            pageCount,
            now,
            null,
            false);
        const string insertSql = """
            INSERT INTO quota_reservations
                (reservation_id, tenant_id, entitlement_id, job_id, page_count, reserved_at)
            VALUES
                (@reservation_id, @tenant_id, @entitlement_id, @job_id, @page_count, @reserved_at);

            UPDATE subscription_periods
            SET pages_reserved = pages_reserved + @page_count,
                updated_at = @reserved_at
            WHERE entitlement_id = @entitlement_id;
            """;
        await using (var insertCommand = new NpgsqlCommand(insertSql, connection, transaction))
        {
            insertCommand.Parameters.AddWithValue("reservation_id", reservation.ReservationId);
            insertCommand.Parameters.AddWithValue("tenant_id", tenantId);
            insertCommand.Parameters.AddWithValue("entitlement_id", entitlementId);
            insertCommand.Parameters.AddWithValue("job_id", jobId);
            insertCommand.Parameters.AddWithValue("page_count", pageCount);
            insertCommand.Parameters.AddWithValue("reserved_at", now);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return reservation;
    }

    public Task SettleQuotaAsync(
        Guid tenantId,
        Guid reservationId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        CompleteReservationAsync(tenantId, reservationId, now, settle: true, cancellationToken);

    public Task ReleaseQuotaAsync(
        Guid tenantId,
        Guid reservationId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        CompleteReservationAsync(tenantId, reservationId, now, settle: false, cancellationToken);

    public async Task<OcrJob?> GetOcrJobAsync(
        Guid tenantId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT job_id, tenant_id, connector_id, page_count, source_sha256, state,
                   reservation_id, result_json::text, provider_model, failure_code,
                   created_at, updated_at
            FROM ocr_jobs
            WHERE tenant_id = @tenant_id AND job_id = @job_id;
            """;
        await using var connection = await OpenTenantAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("job_id", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadOcrJob(reader) : null;
    }

    public async Task SaveOcrJobAsync(OcrJob job, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO ocr_jobs
                (job_id, tenant_id, connector_id, page_count, source_sha256, state,
                 reservation_id, result_json, provider_model, failure_code, created_at, updated_at)
            VALUES
                (@job_id, @tenant_id, @connector_id, @page_count, @source_sha256, @state,
                 @reservation_id, @result_json, @provider_model, @failure_code, @created_at, @updated_at)
            ON CONFLICT (job_id) DO UPDATE SET
                state = EXCLUDED.state,
                result_json = EXCLUDED.result_json,
                provider_model = EXCLUDED.provider_model,
                failure_code = EXCLUDED.failure_code,
                updated_at = EXCLUDED.updated_at
            WHERE ocr_jobs.tenant_id = EXCLUDED.tenant_id
              AND ocr_jobs.connector_id = EXCLUDED.connector_id;
            """;
        await using var connection = await OpenTenantAsync(job.TenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("job_id", job.JobId);
        command.Parameters.AddWithValue("tenant_id", job.TenantId);
        command.Parameters.AddWithValue("connector_id", job.ConnectorId);
        command.Parameters.AddWithValue("page_count", job.PageCount);
        command.Parameters.AddWithValue("source_sha256", job.SourceSha256);
        command.Parameters.AddWithValue("state", ToDatabaseState(job.State));
        command.Parameters.AddWithValue("reservation_id", job.ReservationId);
        command.Parameters.Add(
            new NpgsqlParameter("result_json", NpgsqlDbType.Jsonb)
            {
                Value = (object?)job.ResultJson ?? DBNull.Value
            });
        command.Parameters.AddWithValue("provider_model", (object?)job.ProviderModel ?? DBNull.Value);
        command.Parameters.AddWithValue("failure_code", (object?)job.FailureCode ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at", job.CreatedAt);
        command.Parameters.AddWithValue("updated_at", job.UpdatedAt);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            throw new InvalidOperationException("Cross-tenant OCR job collision.");
        }
    }

    public async Task<IReadOnlyList<CanonicalProduct>> SearchCanonicalProductsAsync(
        Guid tenantId,
        CanonicalSearchQuery query,
        float[]? embedding,
        string? embeddingVersion,
        CancellationToken cancellationToken)
    {
        var vectorOrder = embedding is null
            ? "0.0"
            : "CASE WHEN embedding_version = @embedding_version AND embedding IS NOT NULL " +
              "THEN 1.0 - (embedding <=> CAST(@embedding AS vector)) ELSE 0.0 END";
        var vectorPredicate = embedding is null
            ? "FALSE"
            : "(embedding_version = @embedding_version AND embedding IS NOT NULL " +
              "AND 1.0 - (embedding <=> CAST(@embedding AS vector)) >= 0.55)";
        var sql = $$"""
            SELECT canonical_product_id, display_name, aliases, identifiers,
                   active_ingredient, strength, dosage_form, pack, manufacturer,
                   embedding_version
            FROM canonical_products
            WHERE active
              AND (
                    search_vector @@ websearch_to_tsquery('simple', @query)
                    OR display_name ILIKE @like_query
                    OR @identifier = ANY(identifiers)
                    OR {{vectorPredicate}}
                  )
            ORDER BY (@identifier = ANY(identifiers)) DESC,
                     {{vectorOrder}} DESC,
                     ts_rank(search_vector, websearch_to_tsquery('simple', @query)) DESC,
                     display_name
            LIMIT @limit;
            """;
        await using var connection = await OpenTenantAsync(tenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("query", query.Description);
        command.Parameters.AddWithValue("like_query", $"%{EscapeLike(query.Description)}%");
        command.Parameters.AddWithValue("identifier", query.VendorItemCode ?? string.Empty);
        command.Parameters.AddWithValue("limit", Math.Max(query.Limit * 3, query.Limit));
        if (embedding is not null)
        {
            command.Parameters.AddWithValue(
                "embedding",
                "[" + string.Join(
                    ',',
                    embedding.Select(value => value.ToString("R", CultureInfo.InvariantCulture))) + "]");
            command.Parameters.AddWithValue("embedding_version", embeddingVersion!);
        }

        var products = new List<CanonicalProduct>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(new CanonicalProduct(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetFieldValue<string[]>(2),
                reader.GetFieldValue<string[]>(3),
                new PharmaAttributes(
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)),
                reader.GetString(9),
                null));
        }
        _ = tenantId;
        return products;
    }

    public async Task AppendAuditAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO audit_events
                (event_id, tenant_id, actor_type, actor_reference, action, target_reference,
                 result, correlation_id, occurred_at)
            VALUES
                (@event_id, @tenant_id, @actor_type, @actor_reference, @action, @target_reference,
                 @result, @correlation_id, @occurred_at);
            """;
        await using var connection = await OpenTenantAsync(auditEvent.TenantId, cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("event_id", auditEvent.EventId);
        command.Parameters.AddWithValue("tenant_id", auditEvent.TenantId);
        command.Parameters.AddWithValue("actor_type", auditEvent.ActorType);
        command.Parameters.AddWithValue("actor_reference", auditEvent.ActorReference);
        command.Parameters.AddWithValue("action", auditEvent.Action);
        command.Parameters.AddWithValue("target_reference", auditEvent.TargetReference);
        command.Parameters.AddWithValue("result", auditEvent.Result);
        command.Parameters.AddWithValue("correlation_id", auditEvent.CorrelationId);
        command.Parameters.AddWithValue("occurred_at", auditEvent.OccurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task CompleteReservationAsync(
        Guid tenantId,
        Guid reservationId,
        DateTimeOffset now,
        bool settle,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenTenantAsync(tenantId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        const string selectSql = """
            SELECT entitlement_id, page_count, settled_at, released_at
            FROM quota_reservations
            WHERE reservation_id = @reservation_id
            FOR UPDATE;
            """;
        Guid entitlementId;
        int pageCount;
        await using (var selectCommand = new NpgsqlCommand(selectSql, connection, transaction))
        {
            selectCommand.Parameters.AddWithValue("reservation_id", reservationId);
            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) ||
                !reader.IsDBNull(2) ||
                !reader.IsDBNull(3))
            {
                await reader.DisposeAsync();
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            entitlementId = reader.GetGuid(0);
            pageCount = reader.GetInt32(1);
        }

        var reservationColumn = settle ? "settled_at" : "released_at";
        var periodIncrement = settle ? ", pages_settled = pages_settled + @page_count" : string.Empty;
        var sql = $$"""
            UPDATE quota_reservations
            SET {{reservationColumn}} = @now
            WHERE reservation_id = @reservation_id;

            UPDATE subscription_periods
            SET pages_reserved = pages_reserved - @page_count
                {{periodIncrement}},
                updated_at = @now
            WHERE entitlement_id = @entitlement_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("reservation_id", reservationId);
        command.Parameters.AddWithValue("page_count", pageCount);
        command.Parameters.AddWithValue("entitlement_id", entitlementId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task<NpgsqlConnection> OpenTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenant_id, false);",
            connection);
        command.Parameters.AddWithValue("tenant_id", tenantId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static OcrJob ReadOcrJob(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetInt32(3),
            reader.GetString(4),
            ParseJobState(reader.GetString(5)),
            reader.GetGuid(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetFieldValue<DateTimeOffset>(10),
            reader.GetFieldValue<DateTimeOffset>(11));

    private static SubscriptionStatus ParseStatus(string status) => status switch
    {
        "ACTIVE" => SubscriptionStatus.Active,
        "SUSPENDED" => SubscriptionStatus.Suspended,
        "EXPIRED" => SubscriptionStatus.Expired,
        _ => throw new InvalidOperationException($"Unknown subscription status: {status}")
    };

    private static OcrJobState ParseJobState(string state) => state switch
    {
        "RESERVED" => OcrJobState.Reserved,
        "PROCESSING" => OcrJobState.Processing,
        "COMPLETED" => OcrJobState.Completed,
        "FAILED" => OcrJobState.Failed,
        _ => throw new InvalidOperationException($"Unknown OCR job state: {state}")
    };

    private static string ToDatabaseState(OcrJobState state) => state switch
    {
        OcrJobState.Reserved => "RESERVED",
        OcrJobState.Processing => "PROCESSING",
        OcrJobState.Completed => "COMPLETED",
        OcrJobState.Failed => "FAILED",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
