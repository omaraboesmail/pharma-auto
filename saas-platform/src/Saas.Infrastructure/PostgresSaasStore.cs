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

    public async Task<OcrProcessingAttempt> StartOcrJobAsync(
        Guid tenantId,
        Guid connectorId,
        Guid jobId,
        int pageCount,
        string sourceSha256,
        DateTimeOffset now,
        DateTimeOffset staleBefore,
        CancellationToken cancellationToken)
    {
        if (pageCount is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(pageCount));
        }
        if (sourceSha256.Length != 64)
        {
            throw new ArgumentException("A SHA-256 source binding is required.", nameof(sourceSha256));
        }

        await using var connection = await OpenTenantAsync(tenantId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        await using (var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));",
            connection,
            transaction))
        {
            lockCommand.Parameters.AddWithValue("lock_key", $"{tenantId:D}:{jobId:D}");
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string existingSql = """
            SELECT job.job_id, job.tenant_id, job.connector_id, job.page_count,
                   job.source_sha256, job.state, job.reservation_id,
                   job.result_json::text, job.provider_model, job.failure_code,
                   job.created_at, job.updated_at, job.attempt_id,
                   reservation.entitlement_id, reservation.page_count,
                   reservation.settled_at, reservation.released_at
            FROM ocr_jobs AS job
            JOIN quota_reservations AS reservation
              ON reservation.reservation_id = job.reservation_id
             AND reservation.tenant_id = job.tenant_id
             AND reservation.job_id = job.job_id
            WHERE job.tenant_id = @tenant_id AND job.job_id = @job_id
            FOR UPDATE OF job, reservation;
            """;
        OcrJob? existingJob = null;
        Guid existingAttemptId = Guid.Empty;
        DateTimeOffset? existingSettledAt = null;
        DateTimeOffset? existingReleasedAt = null;
        await using (var existingCommand = new NpgsqlCommand(existingSql, connection, transaction))
        {
            existingCommand.Parameters.AddWithValue("tenant_id", tenantId);
            existingCommand.Parameters.AddWithValue("job_id", jobId);
            await using var reader = await existingCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                existingJob = ReadOcrJob(reader);
                existingAttemptId = reader.GetGuid(12);
                if (reader.GetInt32(14) != existingJob.PageCount)
                {
                    throw new InvalidOperationException(
                        "OCR job and quota reservation page counts do not agree.");
                }
                existingSettledAt = reader.IsDBNull(15)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(15);
                existingReleasedAt = reader.IsDBNull(16)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(16);
            }
        }

        if (existingJob is not null)
        {
            EnsureSameOcrJob(
                existingJob,
                tenantId,
                connectorId,
                pageCount,
                sourceSha256);
            if (existingJob.State == OcrJobState.Completed)
            {
                if (existingSettledAt is null || existingReleasedAt is not null)
                {
                    throw new InvalidOperationException(
                        "Completed OCR job and quota settlement do not agree.");
                }
                await transaction.CommitAsync(cancellationToken);
                return new OcrProcessingAttempt(existingJob, existingAttemptId);
            }

            if (existingJob.State is OcrJobState.Processing or OcrJobState.Reserved)
            {
                if (existingSettledAt is not null || existingReleasedAt is not null)
                {
                    throw new InvalidOperationException(
                        "Active OCR job and quota reservation do not agree.");
                }
                if (existingJob.State == OcrJobState.Processing &&
                    existingJob.UpdatedAt > staleBefore)
                {
                    throw new InvalidOperationException(
                        "The OCR job is already being processed by an active attempt.");
                }

                var recoveredAttemptId = Guid.NewGuid();
                var recoveredJob = existingJob with
                {
                    State = OcrJobState.Processing,
                    ResultJson = null,
                    ProviderModel = null,
                    FailureCode = null,
                    UpdatedAt = now
                };
                await UpdateProcessingJobAsync(
                    connection,
                    transaction,
                    recoveredJob,
                    recoveredAttemptId,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new OcrProcessingAttempt(recoveredJob, recoveredAttemptId);
            }

            if (existingJob.State != OcrJobState.Failed ||
                existingSettledAt is not null ||
                existingReleasedAt is null)
            {
                throw new InvalidOperationException(
                    "Failed OCR job and released quota reservation do not agree.");
            }
        }

        const string periodSql = """
            SELECT p.entitlement_id, p.page_limit, p.pages_reserved, p.pages_settled
            FROM connector_registrations c
            JOIN subscriptions s ON s.tenant_id = c.tenant_id
            JOIN subscription_periods p
              ON p.subscription_id = s.subscription_id
             AND p.tenant_id = s.tenant_id
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

        var reservationId = existingJob?.ReservationId ?? Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        OcrJob processingJob;
        if (existingJob is null)
        {
            const string insertReservationSql = """
                INSERT INTO quota_reservations
                    (reservation_id, tenant_id, entitlement_id, job_id, page_count, reserved_at)
                VALUES
                    (@reservation_id, @tenant_id, @entitlement_id, @job_id, @page_count, @reserved_at);
                """;
            await using (var insertReservation = new NpgsqlCommand(
                insertReservationSql,
                connection,
                transaction))
            {
                AddReservationParameters(
                    insertReservation,
                    reservationId,
                    tenantId,
                    entitlementId,
                    jobId,
                    pageCount,
                    now);
                if (await insertReservation.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    throw new InvalidOperationException("Quota reservation was not created.");
                }
            }

            processingJob = new OcrJob(
                jobId,
                tenantId,
                connectorId,
                pageCount,
                sourceSha256,
                OcrJobState.Processing,
                reservationId,
                null,
                null,
                null,
                now,
                now);
            await InsertOcrJobAsync(
                connection,
                transaction,
                processingJob,
                attemptId,
                cancellationToken);
        }
        else
        {
            const string reactivateReservationSql = """
                UPDATE quota_reservations
                SET entitlement_id = @entitlement_id,
                    reserved_at = @reserved_at,
                    settled_at = NULL,
                    released_at = NULL
                WHERE reservation_id = @reservation_id
                  AND tenant_id = @tenant_id
                  AND job_id = @job_id
                  AND released_at IS NOT NULL
                  AND settled_at IS NULL;
                """;
            await using (var reactivateReservation = new NpgsqlCommand(
                reactivateReservationSql,
                connection,
                transaction))
            {
                AddReservationParameters(
                    reactivateReservation,
                    reservationId,
                    tenantId,
                    entitlementId,
                    jobId,
                    pageCount,
                    now);
                if (await reactivateReservation.ExecuteNonQueryAsync(cancellationToken) != 1)
                {
                    throw new InvalidOperationException("Released quota reservation changed during retry.");
                }
            }

            processingJob = existingJob with
            {
                State = OcrJobState.Processing,
                ResultJson = null,
                ProviderModel = null,
                FailureCode = null,
                UpdatedAt = now
            };
            await UpdateProcessingJobAsync(
                connection,
                transaction,
                processingJob,
                attemptId,
                cancellationToken);
        }

        const string incrementPeriodSql = """
            UPDATE subscription_periods
            SET pages_reserved = pages_reserved + @page_count,
                updated_at = @reserved_at
            WHERE entitlement_id = @entitlement_id
              AND tenant_id = @tenant_id;
            """;
        await using (var incrementPeriod = new NpgsqlCommand(
            incrementPeriodSql,
            connection,
            transaction))
        {
            incrementPeriod.Parameters.AddWithValue("page_count", pageCount);
            incrementPeriod.Parameters.AddWithValue("reserved_at", now);
            incrementPeriod.Parameters.AddWithValue("entitlement_id", entitlementId);
            incrementPeriod.Parameters.AddWithValue("tenant_id", tenantId);
            if (await incrementPeriod.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Subscription period changed during reservation.");
            }
        }
        await transaction.CommitAsync(cancellationToken);
        return new OcrProcessingAttempt(processingJob, attemptId);
    }

    public Task<OcrJob> CompleteOcrJobAsync(
        OcrJob job,
        Guid attemptId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken) =>
        FinalizeOcrJobAsync(job, attemptId, auditEvent, settle: true, cancellationToken);

    public Task<OcrJob> FailOcrJobAsync(
        OcrJob job,
        Guid attemptId,
        AuditEvent auditEvent,
        CancellationToken cancellationToken) =>
        FinalizeOcrJobAsync(job, attemptId, auditEvent, settle: false, cancellationToken);

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

    public async Task<IReadOnlyList<CanonicalProductSearchHit>> SearchCanonicalProductsAsync(
        Guid tenantId,
        CanonicalSearchQuery query,
        float[]? embedding,
        string? embeddingVersion,
        CancellationToken cancellationToken)
    {
        var vectorOrder = embedding is null
            ? "0.0::double precision"
            : "CASE WHEN embedding_version = @embedding_version AND embedding IS NOT NULL " +
              "THEN 1.0 - (embedding <=> CAST(@embedding AS vector)) ELSE 0.0 END";
        var vectorPredicate = embedding is null
            ? "FALSE"
            : "(embedding_version = @embedding_version AND embedding IS NOT NULL " +
              "AND 1.0 - (embedding <=> CAST(@embedding AS vector)) >= @semantic_threshold)";
        var sql = $$"""
            SELECT canonical_product_id, display_name, aliases, identifiers,
                   active_ingredient, strength, dosage_form, pack, manufacturer,
                   embedding_version, {{vectorOrder}} AS semantic_score
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
            command.Parameters.AddWithValue(
                "semantic_threshold",
                CanonicalSearchPolicy.SemanticThreshold);
        }

        var products = new List<CanonicalProductSearchHit>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(new CanonicalProductSearchHit(
                new CanonicalProduct(
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
                    null),
                reader.GetDouble(10)));
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
        AddAuditParameters(command, auditEvent);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertOcrJobAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OcrJob job,
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO ocr_jobs
                (job_id, tenant_id, connector_id, page_count, source_sha256, state,
                 reservation_id, result_json, provider_model, failure_code,
                 created_at, updated_at, attempt_id)
            VALUES
                (@job_id, @tenant_id, @connector_id, @page_count, @source_sha256, 'PROCESSING',
                 @reservation_id, NULL, NULL, NULL, @created_at, @updated_at, @attempt_id);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddOcrIdentityParameters(command, job);
        command.Parameters.AddWithValue("created_at", job.CreatedAt);
        command.Parameters.AddWithValue("updated_at", job.UpdatedAt);
        command.Parameters.AddWithValue("attempt_id", attemptId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("OCR processing job was not created.");
        }
    }

    private static async Task UpdateProcessingJobAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OcrJob job,
        Guid attemptId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE ocr_jobs
            SET state = 'PROCESSING',
                result_json = NULL,
                provider_model = NULL,
                failure_code = NULL,
                updated_at = @updated_at,
                attempt_id = @attempt_id
            WHERE job_id = @job_id
              AND tenant_id = @tenant_id
              AND connector_id = @connector_id
              AND page_count = @page_count
              AND source_sha256 = @source_sha256
              AND reservation_id = @reservation_id
              AND state <> 'COMPLETED';
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddOcrIdentityParameters(command, job);
        command.Parameters.AddWithValue("updated_at", job.UpdatedAt);
        command.Parameters.AddWithValue("attempt_id", attemptId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("OCR job changed before processing could start.");
        }
    }

    private static void AddOcrIdentityParameters(NpgsqlCommand command, OcrJob job)
    {
        command.Parameters.AddWithValue("job_id", job.JobId);
        command.Parameters.AddWithValue("tenant_id", job.TenantId);
        command.Parameters.AddWithValue("connector_id", job.ConnectorId);
        command.Parameters.AddWithValue("page_count", job.PageCount);
        command.Parameters.AddWithValue("source_sha256", job.SourceSha256);
        command.Parameters.AddWithValue("reservation_id", job.ReservationId);
    }

    private static void AddReservationParameters(
        NpgsqlCommand command,
        Guid reservationId,
        Guid tenantId,
        Guid entitlementId,
        Guid jobId,
        int pageCount,
        DateTimeOffset reservedAt)
    {
        command.Parameters.AddWithValue("reservation_id", reservationId);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("entitlement_id", entitlementId);
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("page_count", pageCount);
        command.Parameters.AddWithValue("reserved_at", reservedAt);
    }

    private async Task<OcrJob> FinalizeOcrJobAsync(
        OcrJob job,
        Guid attemptId,
        AuditEvent auditEvent,
        bool settle,
        CancellationToken cancellationToken)
    {
        if (settle &&
            (job.State != OcrJobState.Completed || string.IsNullOrWhiteSpace(job.ResultJson)))
        {
            throw new ArgumentException("A completed OCR job must include a result.", nameof(job));
        }
        if (!settle &&
            (job.State != OcrJobState.Failed || string.IsNullOrWhiteSpace(job.FailureCode)))
        {
            throw new ArgumentException("A failed OCR job must include a failure code.", nameof(job));
        }
        EnsureMatchingAudit(job, auditEvent, settle);

        await using var connection = await OpenTenantAsync(job.TenantId, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        const string selectSql = """
            SELECT job.job_id, job.tenant_id, job.connector_id, job.page_count,
                   job.source_sha256, job.state, job.reservation_id,
                   job.result_json::text, job.provider_model, job.failure_code,
                   job.created_at, job.updated_at, job.attempt_id,
                   reservation.entitlement_id, reservation.page_count,
                   reservation.settled_at, reservation.released_at
            FROM ocr_jobs AS job
            JOIN quota_reservations AS reservation
              ON reservation.reservation_id = job.reservation_id
             AND reservation.tenant_id = job.tenant_id
             AND reservation.job_id = job.job_id
            WHERE job.tenant_id = @tenant_id AND job.job_id = @job_id
            FOR UPDATE OF job, reservation;
            """;
        OcrJob current;
        Guid currentAttemptId;
        Guid entitlementId;
        int reservedPageCount;
        DateTimeOffset? settledAt;
        DateTimeOffset? releasedAt;
        await using (var selectCommand = new NpgsqlCommand(selectSql, connection, transaction))
        {
            selectCommand.Parameters.AddWithValue("tenant_id", job.TenantId);
            selectCommand.Parameters.AddWithValue("job_id", job.JobId);
            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("OCR job and reservation binding does not exist.");
            }
            current = ReadOcrJob(reader);
            currentAttemptId = reader.GetGuid(12);
            entitlementId = reader.GetGuid(13);
            reservedPageCount = reader.GetInt32(14);
            settledAt = reader.IsDBNull(15)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(15);
            releasedAt = reader.IsDBNull(16)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(16);
        }

        EnsureSameOcrJob(current, job);
        if (currentAttemptId != attemptId)
        {
            throw new InvalidOperationException(
                "The OCR result belongs to a superseded processing attempt.");
        }
        if (current.State == OcrJobState.Completed)
        {
            if (settledAt is null || releasedAt is not null)
            {
                throw new InvalidOperationException(
                    "Completed OCR job and quota settlement do not agree.");
            }
            await transaction.CommitAsync(cancellationToken);
            return current;
        }
        if (!settle && current.State == OcrJobState.Failed)
        {
            if (settledAt is not null || releasedAt is null)
            {
                throw new InvalidOperationException(
                    "Failed OCR job and quota release do not agree.");
            }
            await transaction.CommitAsync(cancellationToken);
            return current;
        }
        if (current.State != OcrJobState.Processing)
        {
            throw new InvalidOperationException(
                "Only the active OCR processing attempt can be finalized.");
        }
        if (settledAt is not null || releasedAt is not null)
        {
            throw new InvalidOperationException(
                "OCR job state and quota reservation finalization do not agree.");
        }

        const string updateJobSql = """
            UPDATE ocr_jobs
            SET state = @state,
                result_json = @result_json,
                provider_model = @provider_model,
                failure_code = @failure_code,
                updated_at = @updated_at
            WHERE tenant_id = @tenant_id
              AND job_id = @job_id
              AND connector_id = @connector_id
              AND page_count = @page_count
              AND source_sha256 = @source_sha256
              AND reservation_id = @reservation_id
              AND attempt_id = @attempt_id
              AND state = 'PROCESSING';
            """;
        await using (var updateJob = new NpgsqlCommand(updateJobSql, connection, transaction))
        {
            updateJob.Parameters.AddWithValue("state", ToDatabaseState(job.State));
            updateJob.Parameters.Add(
                new NpgsqlParameter("result_json", NpgsqlDbType.Jsonb)
                {
                    Value = (object?)job.ResultJson ?? DBNull.Value
                });
            updateJob.Parameters.AddWithValue(
                "provider_model",
                (object?)job.ProviderModel ?? DBNull.Value);
            updateJob.Parameters.AddWithValue(
                "failure_code",
                (object?)job.FailureCode ?? DBNull.Value);
            updateJob.Parameters.AddWithValue("updated_at", job.UpdatedAt);
            updateJob.Parameters.AddWithValue("tenant_id", job.TenantId);
            updateJob.Parameters.AddWithValue("job_id", job.JobId);
            updateJob.Parameters.AddWithValue("connector_id", job.ConnectorId);
            updateJob.Parameters.AddWithValue("page_count", job.PageCount);
            updateJob.Parameters.AddWithValue("source_sha256", job.SourceSha256);
            updateJob.Parameters.AddWithValue("reservation_id", job.ReservationId);
            updateJob.Parameters.AddWithValue("attempt_id", attemptId);
            if (await updateJob.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("OCR job changed during finalization.");
            }
        }

        var reservationColumn = settle ? "settled_at" : "released_at";
        var settledIncrement = settle
            ? ", pages_settled = pages_settled + @page_count"
            : string.Empty;
        var finalizeReservationSql = $$"""
            UPDATE quota_reservations
            SET {{reservationColumn}} = @updated_at
            WHERE reservation_id = @reservation_id
              AND tenant_id = @tenant_id
              AND job_id = @job_id
              AND settled_at IS NULL
              AND released_at IS NULL;
            """;
        await using (var finalizeReservation = new NpgsqlCommand(
            finalizeReservationSql,
            connection,
            transaction))
        {
            finalizeReservation.Parameters.AddWithValue("updated_at", job.UpdatedAt);
            finalizeReservation.Parameters.AddWithValue("reservation_id", job.ReservationId);
            finalizeReservation.Parameters.AddWithValue("tenant_id", job.TenantId);
            finalizeReservation.Parameters.AddWithValue("job_id", job.JobId);
            if (await finalizeReservation.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Quota reservation changed during finalization.");
            }
        }

        var finalizePeriodSql = $$"""
            UPDATE subscription_periods
            SET pages_reserved = pages_reserved - @page_count
                {{settledIncrement}},
                updated_at = @updated_at
            WHERE entitlement_id = @entitlement_id
              AND tenant_id = @tenant_id
              AND pages_reserved >= @page_count;
            """;
        await using (var finalizePeriod = new NpgsqlCommand(
            finalizePeriodSql,
            connection,
            transaction))
        {
            finalizePeriod.Parameters.AddWithValue("updated_at", job.UpdatedAt);
            finalizePeriod.Parameters.AddWithValue("tenant_id", job.TenantId);
            finalizePeriod.Parameters.AddWithValue("page_count", reservedPageCount);
            finalizePeriod.Parameters.AddWithValue("entitlement_id", entitlementId);
            if (await finalizePeriod.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("Subscription period changed during finalization.");
            }
        }

        const string auditSql = """
            INSERT INTO audit_events
                (event_id, tenant_id, actor_type, actor_reference, action, target_reference,
                 result, correlation_id, occurred_at)
            VALUES
                (@event_id, @tenant_id, @actor_type, @actor_reference, @action, @target_reference,
                 @result, @correlation_id, @occurred_at);
            """;
        await using (var insertAudit = new NpgsqlCommand(auditSql, connection, transaction))
        {
            AddAuditParameters(insertAudit, auditEvent);
            await insertAudit.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return job;
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

    private static void EnsureSameOcrJob(OcrJob current, OcrJob requested)
    {
        if (current.JobId != requested.JobId ||
            current.TenantId != requested.TenantId ||
            current.ConnectorId != requested.ConnectorId ||
            current.PageCount != requested.PageCount ||
            !string.Equals(
                current.SourceSha256,
                requested.SourceSha256,
                StringComparison.Ordinal) ||
            current.ReservationId != requested.ReservationId)
        {
            throw new InvalidOperationException(
                "OCR job identity or immutable source binding does not match.");
        }
    }

    private static void EnsureSameOcrJob(
        OcrJob existing,
        Guid tenantId,
        Guid connectorId,
        int pageCount,
        string sourceSha256)
    {
        if (existing.TenantId != tenantId ||
            existing.ConnectorId != connectorId ||
            existing.PageCount != pageCount ||
            !string.Equals(existing.SourceSha256, sourceSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The OCR job ID was replayed with a different connector or source document.");
        }
    }

    private static void AddAuditParameters(NpgsqlCommand command, AuditEvent auditEvent)
    {
        command.Parameters.AddWithValue("event_id", auditEvent.EventId);
        command.Parameters.AddWithValue("tenant_id", auditEvent.TenantId);
        command.Parameters.AddWithValue("actor_type", auditEvent.ActorType);
        command.Parameters.AddWithValue("actor_reference", auditEvent.ActorReference);
        command.Parameters.AddWithValue("action", auditEvent.Action);
        command.Parameters.AddWithValue("target_reference", auditEvent.TargetReference);
        command.Parameters.AddWithValue("result", auditEvent.Result);
        command.Parameters.AddWithValue("correlation_id", auditEvent.CorrelationId);
        command.Parameters.AddWithValue("occurred_at", auditEvent.OccurredAt);
    }

    private static void EnsureMatchingAudit(
        OcrJob job,
        AuditEvent auditEvent,
        bool settle)
    {
        if (auditEvent.TenantId != job.TenantId ||
            auditEvent.CorrelationId != job.JobId ||
            !string.Equals(auditEvent.ActorType, "CONNECTOR", StringComparison.Ordinal) ||
            !string.Equals(
                auditEvent.ActorReference,
                job.ConnectorId.ToString("D"),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                auditEvent.Action,
                settle ? "OCR_SETTLED" : "OCR_RELEASED",
                StringComparison.Ordinal) ||
            !string.Equals(
                auditEvent.TargetReference,
                job.JobId.ToString("D"),
                StringComparison.OrdinalIgnoreCase) ||
            auditEvent.OccurredAt != job.UpdatedAt ||
            !string.Equals(
                auditEvent.Result,
                settle ? "SUCCESS" : job.FailureCode,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("OCR audit identity does not match the job.", nameof(auditEvent));
        }
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
