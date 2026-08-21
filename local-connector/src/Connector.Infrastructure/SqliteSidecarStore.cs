using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PharmaAuto.Connector.Application;
using PharmaAuto.Connector.Domain;

namespace PharmaAuto.Connector.Infrastructure;

public sealed class SqliteSidecarStore(string databasePath) : ISidecarStore
{
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(databasePath),
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
        Pooling = true,
        ForeignKeys = true,
        DefaultTimeout = 15
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SavePairingSessionAsync(
        PairingSession session,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO pairing_sessions(session_id, secret_hash, expires_at, created_at, consumed_at)
            VALUES($session_id, $secret_hash, $expires_at, $created_at, NULL);
            """;
        await ExecuteAsync(sql, cancellationToken, command =>
        {
            command.Parameters.AddWithValue("$session_id", session.SessionId.ToString("D"));
            command.Parameters.AddWithValue("$secret_hash", session.SecretHash);
            command.Parameters.AddWithValue("$expires_at", Format(session.ExpiresAt));
            command.Parameters.AddWithValue("$created_at", Format(session.CreatedAt));
        });
    }

    public async Task<bool> ConsumePairingSessionAsync(
        Guid sessionId,
        ReadOnlyMemory<byte> secretHash,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE pairing_sessions
            SET consumed_at = $consumed_at
            WHERE session_id = $session_id
              AND secret_hash = $secret_hash
              AND consumed_at IS NULL
              AND expires_at > $consumed_at;
            """;
        return await ExecuteAsync(sql, cancellationToken, command =>
        {
            command.Parameters.AddWithValue("$consumed_at", Format(consumedAt));
            command.Parameters.AddWithValue("$session_id", sessionId.ToString("D"));
            command.Parameters.AddWithValue("$secret_hash", secretHash.ToArray());
        }) == 1;
    }

    public async Task SaveDeviceAsync(
        DeviceRegistration device,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO devices(
                device_id, display_name, public_key_spki, paired_at, revoked_at, last_seen_at)
            VALUES(
                $device_id, $display_name, $public_key_spki, $paired_at, NULL, $last_seen_at);
            """;
        await ExecuteAsync(sql, cancellationToken, command =>
        {
            command.Parameters.AddWithValue("$device_id", device.DeviceId.ToString("D"));
            command.Parameters.AddWithValue("$display_name", device.DisplayName);
            command.Parameters.AddWithValue("$public_key_spki", device.PublicKeySubjectPublicKeyInfo);
            command.Parameters.AddWithValue("$paired_at", Format(device.PairedAt));
            command.Parameters.AddWithValue("$last_seen_at", Format(device.LastSeenAt ?? device.PairedAt));
        });
    }

    public async Task<DeviceRegistration?> GetDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT device_id, display_name, public_key_spki, paired_at, revoked_at, last_seen_at
            FROM devices
            WHERE device_id = $device_id;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$device_id", deviceId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDevice(reader) : null;
    }

    public async Task<IReadOnlyList<DeviceRegistration>> ListDevicesAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT device_id, display_name, public_key_spki, paired_at, revoked_at, last_seen_at
            FROM devices
            ORDER BY paired_at DESC;
            """;
        var devices = new List<DeviceRegistration>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            devices.Add(ReadDevice(reader));
        }
        return devices;
    }

    public async Task<bool> RevokeDeviceAsync(
        Guid deviceId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE devices
            SET revoked_at = $revoked_at
            WHERE device_id = $device_id AND revoked_at IS NULL;
            """;
        return await ExecuteAsync(sql, cancellationToken, command =>
        {
            command.Parameters.AddWithValue("$revoked_at", Format(revokedAt));
            command.Parameters.AddWithValue("$device_id", deviceId.ToString("D"));
        }) == 1;
    }

    public Task TouchDeviceAsync(
        Guid deviceId,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken) =>
        ExecuteNoResultAsync(
            "UPDATE devices SET last_seen_at = $seen_at WHERE device_id = $device_id;",
            cancellationToken,
            command =>
            {
                command.Parameters.AddWithValue("$seen_at", Format(seenAt));
                command.Parameters.AddWithValue("$device_id", deviceId.ToString("D"));
            });

    public Task SaveChallengeAsync(
        AccessChallenge challenge,
        CancellationToken cancellationToken) =>
        ExecuteNoResultAsync(
            """
            INSERT INTO access_challenges(
                challenge_id, device_id, nonce, expires_at, consumed_at)
            VALUES(
                $challenge_id, $device_id, $nonce, $expires_at, NULL);
            """,
            cancellationToken,
            command =>
            {
                command.Parameters.AddWithValue("$challenge_id", challenge.ChallengeId.ToString("D"));
                command.Parameters.AddWithValue("$device_id", challenge.DeviceId.ToString("D"));
                command.Parameters.AddWithValue("$nonce", challenge.Nonce);
                command.Parameters.AddWithValue("$expires_at", Format(challenge.ExpiresAt));
            });

    public async Task<AccessChallenge?> ConsumeChallengeAsync(
        Guid challengeId,
        Guid deviceId,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var select = connection.CreateCommand();
        select.Transaction = (SqliteTransaction)transaction;
        select.CommandText = """
            SELECT challenge_id, device_id, nonce, expires_at, consumed_at
            FROM access_challenges
            WHERE challenge_id = $challenge_id
              AND device_id = $device_id
              AND consumed_at IS NULL
              AND expires_at > $consumed_at;
            """;
        select.Parameters.AddWithValue("$challenge_id", challengeId.ToString("D"));
        select.Parameters.AddWithValue("$device_id", deviceId.ToString("D"));
        select.Parameters.AddWithValue("$consumed_at", Format(consumedAt));
        AccessChallenge? challenge;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            challenge = await reader.ReadAsync(cancellationToken)
                ? new AccessChallenge(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    ParseDate(reader.GetString(3)),
                    null)
                : null;
        }
        if (challenge is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        await using var update = connection.CreateCommand();
        update.Transaction = (SqliteTransaction)transaction;
        update.CommandText = """
            UPDATE access_challenges
            SET consumed_at = $consumed_at
            WHERE challenge_id = $challenge_id AND consumed_at IS NULL;
            """;
        update.Parameters.AddWithValue("$consumed_at", Format(consumedAt));
        update.Parameters.AddWithValue("$challenge_id", challengeId.ToString("D"));
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        await transaction.CommitAsync(cancellationToken);
        return challenge with { ConsumedAt = consumedAt };
    }

    public Task CreateJobAsync(InvoiceJob job, CancellationToken cancellationToken) =>
        ExecuteNoResultAsync(
            """
            INSERT INTO invoice_jobs(
                job_id, device_id, state, expected_page_count, uploaded_page_count,
                current_revision_id, failure_code, created_at, updated_at)
            VALUES(
                $job_id, $device_id, $state, $expected_page_count, 0,
                NULL, NULL, $created_at, $updated_at);
            """,
            cancellationToken,
            command => AddJobParameters(command, job));

    public async Task<InvoiceJob?> GetJobAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT job_id, device_id, state, expected_page_count, uploaded_page_count,
                   current_revision_id, failure_code, created_at, updated_at
            FROM invoice_jobs
            WHERE job_id = $job_id;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$job_id", jobId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
    }

    public Task<IReadOnlyList<InvoiceJob>> ListJobsAsync(
        int limit,
        CancellationToken cancellationToken) =>
        ReadJobsAsync(
            """
            SELECT job_id, device_id, state, expected_page_count, uploaded_page_count,
                   current_revision_id, failure_code, created_at, updated_at
            FROM invoice_jobs
            ORDER BY created_at DESC
            LIMIT $limit;
            """,
            limit,
            null,
            cancellationToken);

    public Task<IReadOnlyList<InvoiceJob>> ListJobsByStateAsync(
        IReadOnlyCollection<InvoiceJobState> states,
        int limit,
        CancellationToken cancellationToken)
    {
        if (states.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<InvoiceJob>>([]);
        }
        var parameterNames = states.Select((_, index) => $"$state_{index}").ToArray();
        var sql = $$"""
            SELECT job_id, device_id, state, expected_page_count, uploaded_page_count,
                   current_revision_id, failure_code, created_at, updated_at
            FROM invoice_jobs
            WHERE state IN ({{string.Join(",", parameterNames)}})
            ORDER BY updated_at
            LIMIT $limit;
            """;
        return ReadJobsAsync(sql, limit, states, cancellationToken);
    }

    public async Task<bool> TransitionJobAsync(
        Guid jobId,
        InvoiceJobState expected,
        InvoiceJobState next,
        DateTimeOffset changedAt,
        string? failureCode,
        Guid? revisionId,
        CancellationToken cancellationToken)
    {
        InvoiceJobTransitions.EnsureAllowed(expected, next);
        const string sql = """
            UPDATE invoice_jobs
            SET state = $next,
                failure_code = $failure_code,
                current_revision_id = COALESCE($revision_id, current_revision_id),
                updated_at = $changed_at
            WHERE job_id = $job_id AND state = $expected;
            """;
        return await ExecuteAsync(sql, cancellationToken, command =>
        {
            command.Parameters.AddWithValue("$next", State(next));
            command.Parameters.AddWithValue("$failure_code", (object?)failureCode ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$revision_id",
                revisionId?.ToString("D") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$changed_at", Format(changedAt));
            command.Parameters.AddWithValue("$job_id", jobId.ToString("D"));
            command.Parameters.AddWithValue("$expected", State(expected));
        }) == 1;
    }

    public Task SaveChunkAsync(UploadChunk chunk, CancellationToken cancellationToken) =>
        ExecuteNoResultAsync(
            """
            INSERT INTO upload_chunks(
                job_id, page, chunk_index, chunk_count, chunk_sha256, page_sha256,
                mime_type, object_reference, length, uploaded_at)
            VALUES(
                $job_id, $page, $chunk_index, $chunk_count, $chunk_sha256, $page_sha256,
                $mime_type, $object_reference, $length, $uploaded_at)
            ON CONFLICT(job_id, page, chunk_index) DO NOTHING;
            """,
            cancellationToken,
            command =>
            {
                command.Parameters.AddWithValue("$job_id", chunk.JobId.ToString("D"));
                command.Parameters.AddWithValue("$page", chunk.Page);
                command.Parameters.AddWithValue("$chunk_index", chunk.ChunkIndex);
                command.Parameters.AddWithValue("$chunk_count", chunk.ChunkCount);
                command.Parameters.AddWithValue("$chunk_sha256", chunk.ChunkSha256);
                command.Parameters.AddWithValue("$page_sha256", chunk.PageSha256);
                command.Parameters.AddWithValue("$mime_type", chunk.MimeType);
                command.Parameters.AddWithValue("$object_reference", chunk.ObjectReference);
                command.Parameters.AddWithValue("$length", chunk.Length);
                command.Parameters.AddWithValue("$uploaded_at", Format(chunk.UploadedAt));
            });

    public async Task<IReadOnlyList<UploadChunk>> GetChunksAsync(
        Guid jobId,
        int page,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT job_id, page, chunk_index, chunk_count, chunk_sha256, page_sha256,
                   mime_type, object_reference, length, uploaded_at
            FROM upload_chunks
            WHERE job_id = $job_id AND page = $page
            ORDER BY chunk_index;
            """;
        var chunks = new List<UploadChunk>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$job_id", jobId.ToString("D"));
        command.Parameters.AddWithValue("$page", page);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            chunks.Add(new UploadChunk(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetInt64(8),
                ParseDate(reader.GetString(9))));
        }
        return chunks;
    }

    public Task DeleteChunksAsync(Guid jobId, int page, CancellationToken cancellationToken) =>
        ExecuteNoResultAsync(
            "DELETE FROM upload_chunks WHERE job_id = $job_id AND page = $page;",
            cancellationToken,
            command =>
            {
                command.Parameters.AddWithValue("$job_id", jobId.ToString("D"));
                command.Parameters.AddWithValue("$page", page);
            });

    public async Task SavePageAsync(DocumentPage page, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO document_pages(
                job_id, page, mime_type, sha256, object_reference, length, uploaded_at)
            VALUES(
                $job_id, $page, $mime_type, $sha256, $object_reference, $length, $uploaded_at)
            ON CONFLICT(job_id, page) DO UPDATE SET
                object_reference = excluded.object_reference,
                uploaded_at = excluded.uploaded_at
            WHERE document_pages.sha256 = excluded.sha256
              AND document_pages.mime_type = excluded.mime_type;
            """;
        AddPageParameters(command, page);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException("Page number was replayed with different content.");
        }
        await using var update = connection.CreateCommand();
        update.Transaction = (SqliteTransaction)transaction;
        update.CommandText = """
            UPDATE invoice_jobs
            SET uploaded_page_count = (
                    SELECT COUNT(*) FROM document_pages WHERE job_id = $job_id
                ),
                updated_at = $uploaded_at
            WHERE job_id = $job_id;
            """;
        update.Parameters.AddWithValue("$job_id", page.JobId.ToString("D"));
        update.Parameters.AddWithValue("$uploaded_at", Format(page.UploadedAt));
        await update.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentPage>> GetPagesAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT job_id, page, mime_type, sha256, object_reference, length, uploaded_at
            FROM document_pages
            WHERE job_id = $job_id
            ORDER BY page;
            """;
        var pages = new List<DocumentPage>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$job_id", jobId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pages.Add(new DocumentPage(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt64(5),
                ParseDate(reader.GetString(6))));
        }
        return pages;
    }

    public Task SaveRevisionAsync(
        InvoiceRevisionRecord revision,
        CancellationToken cancellationToken) =>
        ExecuteNoResultAsync(
            """
            INSERT INTO invoice_revisions(
                revision_id, job_id, revision_number, status, json, created_by_device_id,
                created_at, confirmed_at)
            VALUES(
                $revision_id, $job_id, $revision_number, $status, $json,
                $created_by_device_id, $created_at, $confirmed_at);
            """,
            cancellationToken,
            command => AddRevisionParameters(command, revision));

    public async Task<InvoiceRevisionRecord?> GetRevisionAsync(
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT revision_id, job_id, revision_number, status, json, created_by_device_id,
                   created_at, confirmed_at
            FROM invoice_revisions
            WHERE revision_id = $revision_id;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$revision_id", revisionId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRevision(reader) : null;
    }

    public async Task<bool> ConfirmRevisionAsync(
        Guid revisionId,
        Guid deviceId,
        DateTimeOffset confirmedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE invoice_revisions
            SET status = 'CONFIRMED', confirmed_at = $confirmed_at
            WHERE revision_id = $revision_id
              AND created_by_device_id = $device_id
              AND status = 'AWAITING_USER_REVIEW'
              AND confirmed_at IS NULL;
            """;
        return await ExecuteAsync(sql, cancellationToken, command =>
        {
            command.Parameters.AddWithValue("$confirmed_at", Format(confirmedAt));
            command.Parameters.AddWithValue("$revision_id", revisionId.ToString("D"));
            command.Parameters.AddWithValue("$device_id", deviceId.ToString("D"));
        }) == 1;
    }

    public async Task ReplaceCatalogAsync(
        IAsyncEnumerable<LocalCatalogItem> items,
        IAsyncEnumerable<LocalVendor> vendors,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = (SqliteTransaction)transaction;
            clear.CommandText = """
                DELETE FROM catalog_identifiers;
                DELETE FROM catalog_items;
                DELETE FROM catalog_vendors;
                DELETE FROM catalog_fts;
                """;
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        await foreach (var item in items.WithCancellation(cancellationToken))
        {
            await InsertCatalogItemAsync(connection, (SqliteTransaction)transaction, item, cancellationToken);
        }
        await foreach (var vendor in vendors.WithCancellation(cancellationToken))
        {
            await InsertVendorAsync(connection, (SqliteTransaction)transaction, vendor, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogSearchHit>> SearchItemsAsync(
        LocalMatchQuery query,
        CancellationToken cancellationToken)
    {
        var exactValues = new[]
        {
            query.Barcode,
            query.VendorItemCode,
            query.ItemCode
        }.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var normalizedDescription = NormalizeSearch(query.Description);
        var fts = BuildFtsQuery(normalizedDescription);
        var exactParameters = exactValues.Length == 0
            ? "SELECT NULL AS local_item_reference, 99 AS priority, '' AS reason WHERE 0"
            : string.Join(
                " UNION ALL ",
                exactValues.Select((_, index) =>
                    $"SELECT local_item_reference, CASE kind WHEN 'VENDOR_CODE' THEN 2 ELSE 1 END AS priority, " +
                    $"CASE kind WHEN 'VENDOR_CODE' THEN 'VENDOR_ITEM_CODE' ELSE 'EXACT_IDENTIFIER' END AS reason " +
                    $"FROM catalog_identifiers WHERE normalized_value = $exact_{index}"));
        var sql = $$"""
            WITH hits AS (
                {{exactParameters}}
                UNION ALL
                SELECT local_item_reference, 3, 'EXACT_NORMALIZED_NAME'
                FROM catalog_items
                WHERE normalized_label = $description AND $description <> ''
                UNION ALL
                SELECT local_item_reference, 4, 'WEAK_RAW_LABEL'
                FROM catalog_fts
                WHERE catalog_fts MATCH $fts
                LIMIT 250
            ), ranked AS (
                SELECT local_item_reference, MIN(priority) AS priority,
                       group_concat(DISTINCT reason) AS reasons
                FROM hits
                GROUP BY local_item_reference
            )
            SELECT i.local_item_reference, i.genius_item_id, i.raw_arabic_label,
                   i.raw_english_label, i.display_label, i.raw_arabic_hash,
                   i.raw_english_hash, i.display_direction, i.quality_flags_json,
                   i.item_code, i.secondary_code, i.international_code,
                   i.barcodes_json, i.vendor_codes_json, i.active_ingredient,
                   i.strength, i.dosage_form, i.pack, i.has_expiry, i.active,
                   i.projected_at, ranked.reasons
            FROM ranked
            JOIN catalog_items i USING(local_item_reference)
            WHERE i.active = 1
            ORDER BY ranked.priority, i.display_label
            LIMIT $limit;
            """;
        var results = new List<CatalogSearchHit>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var index = 0; index < exactValues.Length; index++)
        {
            command.Parameters.AddWithValue($"$exact_{index}", exactValues[index]);
        }
        command.Parameters.AddWithValue("$description", normalizedDescription);
        command.Parameters.AddWithValue("$fts", fts);
        command.Parameters.AddWithValue("$limit", Math.Max(query.Limit * 3, query.Limit));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var item = ReadCatalogItem(reader);
            var reasons = reader.GetString(21)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (item.QualityFlags.Contains(CatalogQualityFlag.TruncatedOrCorrupt) ||
                item.QualityFlags.Contains(CatalogQualityFlag.MalformedBidi))
            {
                reasons = reasons.Where(reason => reason != "WEAK_RAW_LABEL").ToArray();
                if (reasons.Length == 0)
                {
                    continue;
                }
            }
            results.Add(new CatalogSearchHit(item, reasons));
        }
        return results;
    }

    public async Task<IReadOnlyList<LocalVendor>> SearchVendorsAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT local_vendor_reference, genius_vendor_id, code, display_name, active, projected_at
            FROM catalog_vendors
            WHERE active = 1
              AND (normalized_code = $query OR normalized_name LIKE $like_query ESCAPE '\')
            ORDER BY (normalized_code = $query) DESC, display_name
            LIMIT $limit;
            """;
        var normalized = NormalizeSearch(query);
        var vendors = new List<LocalVendor>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$query", normalized);
        command.Parameters.AddWithValue("$like_query", $"%{EscapeLike(normalized)}%");
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 50));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            vendors.Add(new LocalVendor(
                reader.GetString(0),
                ParseDecimal(reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                ParseDate(reader.GetString(5))));
        }
        return vendors;
    }

    public async Task<LocalCatalogItem?> GetCatalogItemAsync(
        string localItemReference,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT local_item_reference, genius_item_id, raw_arabic_label,
                   raw_english_label, display_label, raw_arabic_hash,
                   raw_english_hash, display_direction, quality_flags_json,
                   item_code, secondary_code, international_code,
                   barcodes_json, vendor_codes_json, active_ingredient,
                   strength, dosage_form, pack, has_expiry, active, projected_at
            FROM catalog_items
            WHERE local_item_reference = $reference AND active = 1;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$reference", localItemReference);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCatalogItem(reader) : null;
    }

    public async Task<LocalVendor?> GetCatalogVendorAsync(
        string localVendorReference,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT local_vendor_reference, genius_vendor_id, code, display_name, active, projected_at
            FROM catalog_vendors
            WHERE local_vendor_reference = $reference AND active = 1;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$reference", localVendorReference);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new LocalVendor(
            reader.GetString(0),
            ParseDecimal(reader.GetString(1)),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetBoolean(4),
            ParseDate(reader.GetString(5)));
    }

    public async Task<CatalogProjectionSummary?> GetCatalogProjectionSummaryAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT item_count, vendor_count, barcode_count, vendor_code_count,
                   untrusted_label_count, identical_language_field_count, completed_at
            FROM catalog_projection_summary
            WHERE singleton_id = 1;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new CatalogProjectionSummary(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            ParseDate(reader.GetString(6)),
            false);
    }

    public Task SaveCatalogProjectionSummaryAsync(
        CatalogProjectionSummary summary,
        CancellationToken cancellationToken) =>
        ExecuteNoResultAsync(
            """
            INSERT INTO catalog_projection_summary(
                singleton_id, item_count, vendor_count, barcode_count, vendor_code_count,
                untrusted_label_count, identical_language_field_count, completed_at)
            VALUES(
                1, $item_count, $vendor_count, $barcode_count, $vendor_code_count,
                $untrusted_label_count, $identical_language_field_count, $completed_at)
            ON CONFLICT(singleton_id) DO UPDATE SET
                item_count = excluded.item_count,
                vendor_count = excluded.vendor_count,
                barcode_count = excluded.barcode_count,
                vendor_code_count = excluded.vendor_code_count,
                untrusted_label_count = excluded.untrusted_label_count,
                identical_language_field_count = excluded.identical_language_field_count,
                completed_at = excluded.completed_at;
            """,
            cancellationToken,
            command =>
            {
                command.Parameters.AddWithValue("$item_count", summary.ItemCount);
                command.Parameters.AddWithValue("$vendor_count", summary.VendorCount);
                command.Parameters.AddWithValue("$barcode_count", summary.BarcodeCount);
                command.Parameters.AddWithValue("$vendor_code_count", summary.VendorCodeCount);
                command.Parameters.AddWithValue("$untrusted_label_count", summary.UntrustedLabelCount);
                command.Parameters.AddWithValue(
                    "$identical_language_field_count",
                    summary.IdenticalLanguageFieldCount);
                command.Parameters.AddWithValue("$completed_at", Format(summary.CompletedAt));
            });

    public Task AppendAuditAsync(AuditRecord record, CancellationToken cancellationToken) =>
        ExecuteNoResultAsync(
            """
            INSERT INTO audit_events(
                event_id, actor_type, actor_reference, action, target_reference, result,
                correlation_id, occurred_at)
            VALUES(
                $event_id, $actor_type, $actor_reference, $action, $target_reference, $result,
                $correlation_id, $occurred_at);
            """,
            cancellationToken,
            command =>
            {
                command.Parameters.AddWithValue("$event_id", record.EventId.ToString("D"));
                command.Parameters.AddWithValue("$actor_type", record.ActorType);
                command.Parameters.AddWithValue("$actor_reference", record.ActorReference);
                command.Parameters.AddWithValue("$action", record.Action);
                command.Parameters.AddWithValue("$target_reference", record.TargetReference);
                command.Parameters.AddWithValue("$result", record.Result);
                command.Parameters.AddWithValue("$correlation_id", record.CorrelationId.ToString("D"));
                command.Parameters.AddWithValue("$occurred_at", Format(record.OccurredAt));
            });

    private async Task InsertCatalogItemAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LocalCatalogItem item,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalog_items(
                local_item_reference, genius_item_id, raw_arabic_label, raw_english_label,
                display_label, normalized_label, raw_arabic_hash, raw_english_hash,
                display_direction, quality_flags_json, item_code, secondary_code,
                international_code, barcodes_json, vendor_codes_json, active_ingredient,
                strength, dosage_form, pack, has_expiry, active, projected_at)
            VALUES(
                $local_item_reference, $genius_item_id, $raw_arabic_label, $raw_english_label,
                $display_label, $normalized_label, $raw_arabic_hash, $raw_english_hash,
                $display_direction, $quality_flags_json, $item_code, $secondary_code,
                $international_code, $barcodes_json, $vendor_codes_json, $active_ingredient,
                $strength, $dosage_form, $pack, $has_expiry, $active, $projected_at);
            """;
        command.Parameters.AddWithValue("$local_item_reference", item.LocalItemReference);
        command.Parameters.AddWithValue("$genius_item_id", FormatDecimal(item.GeniusItemId));
        command.Parameters.AddWithValue("$raw_arabic_label", Db(item.RawArabicLabel));
        command.Parameters.AddWithValue("$raw_english_label", Db(item.RawEnglishLabel));
        command.Parameters.AddWithValue("$display_label", Db(item.DisplayLabel));
        command.Parameters.AddWithValue("$normalized_label", NormalizeSearch(item.DisplayLabel ?? string.Empty));
        command.Parameters.AddWithValue("$raw_arabic_hash", Db(item.RawArabicHash));
        command.Parameters.AddWithValue("$raw_english_hash", Db(item.RawEnglishHash));
        command.Parameters.AddWithValue("$display_direction", item.DisplayDirection.ToString());
        command.Parameters.AddWithValue("$quality_flags_json", JsonSerializer.Serialize(item.QualityFlags));
        command.Parameters.AddWithValue("$item_code", Db(item.Identifiers.ItemCode));
        command.Parameters.AddWithValue("$secondary_code", Db(item.Identifiers.SecondaryCode));
        command.Parameters.AddWithValue("$international_code", Db(item.Identifiers.InternationalCode));
        command.Parameters.AddWithValue("$barcodes_json", JsonSerializer.Serialize(item.Identifiers.Barcodes));
        command.Parameters.AddWithValue(
            "$vendor_codes_json",
            JsonSerializer.Serialize(item.Identifiers.VendorItemCodes));
        command.Parameters.AddWithValue("$active_ingredient", Db(item.ActiveIngredient));
        command.Parameters.AddWithValue("$strength", Db(item.Strength));
        command.Parameters.AddWithValue("$dosage_form", Db(item.DosageForm));
        command.Parameters.AddWithValue("$pack", Db(item.Pack));
        command.Parameters.AddWithValue("$has_expiry", item.HasExpiry);
        command.Parameters.AddWithValue("$active", item.Active);
        command.Parameters.AddWithValue("$projected_at", Format(item.ProjectedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);

        var identifiers = new List<(string Kind, string Value)>();
        AddIdentifier(identifiers, "ITEM_CODE", item.Identifiers.ItemCode);
        AddIdentifier(identifiers, "SECONDARY_CODE", item.Identifiers.SecondaryCode);
        AddIdentifier(identifiers, "INTERNATIONAL_CODE", item.Identifiers.InternationalCode);
        identifiers.AddRange(item.Identifiers.Barcodes.Select(value => ("BARCODE", value)));
        identifiers.AddRange(item.Identifiers.VendorItemCodes.Select(value => ("VENDOR_CODE", value)));
        foreach (var identifier in identifiers.Distinct())
        {
            await using var identifierCommand = connection.CreateCommand();
            identifierCommand.Transaction = transaction;
            identifierCommand.CommandText = """
                INSERT OR IGNORE INTO catalog_identifiers(
                    local_item_reference, kind, normalized_value)
                VALUES($local_item_reference, $kind, $normalized_value);
                """;
            identifierCommand.Parameters.AddWithValue("$local_item_reference", item.LocalItemReference);
            identifierCommand.Parameters.AddWithValue("$kind", identifier.Kind);
            identifierCommand.Parameters.AddWithValue(
                "$normalized_value",
                identifier.Value.Trim().ToUpperInvariant());
            await identifierCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var fts = connection.CreateCommand();
        fts.Transaction = transaction;
        fts.CommandText = """
            INSERT INTO catalog_fts(local_item_reference, searchable)
            VALUES($local_item_reference, $searchable);
            """;
        fts.Parameters.AddWithValue("$local_item_reference", item.LocalItemReference);
        fts.Parameters.AddWithValue(
            "$searchable",
            string.Join(
                ' ',
                item.DisplayLabel,
                item.RawArabicLabel,
                item.RawEnglishLabel,
                item.ActiveIngredient,
                item.Strength,
                item.Identifiers.ItemCode,
                string.Join(' ', item.Identifiers.Barcodes),
                string.Join(' ', item.Identifiers.VendorItemCodes)));
        await fts.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertVendorAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LocalVendor vendor,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO catalog_vendors(
                local_vendor_reference, genius_vendor_id, code, normalized_code,
                display_name, normalized_name, active, projected_at)
            VALUES(
                $local_vendor_reference, $genius_vendor_id, $code, $normalized_code,
                $display_name, $normalized_name, $active, $projected_at);
            """;
        command.Parameters.AddWithValue("$local_vendor_reference", vendor.LocalVendorReference);
        command.Parameters.AddWithValue("$genius_vendor_id", FormatDecimal(vendor.GeniusVendorId));
        command.Parameters.AddWithValue("$code", Db(vendor.Code));
        command.Parameters.AddWithValue("$normalized_code", NormalizeSearch(vendor.Code ?? string.Empty));
        command.Parameters.AddWithValue("$display_name", vendor.DisplayName);
        command.Parameters.AddWithValue("$normalized_name", NormalizeSearch(vendor.DisplayName));
        command.Parameters.AddWithValue("$active", vendor.Active);
        command.Parameters.AddWithValue("$projected_at", Format(vendor.ProjectedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<InvoiceJob>> ReadJobsAsync(
        string sql,
        int limit,
        IReadOnlyCollection<InvoiceJobState>? states,
        CancellationToken cancellationToken)
    {
        var jobs = new List<InvoiceJob>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));
        if (states is not null)
        {
            var index = 0;
            foreach (var state in states)
            {
                command.Parameters.AddWithValue($"$state_{index++}", State(state));
            }
        }
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(ReadJob(reader));
        }
        return jobs;
    }

    private async Task<int> ExecuteAsync(
        string sql,
        CancellationToken cancellationToken,
        Action<SqliteCommand> configure)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        configure(command);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ExecuteNoResultAsync(
        string sql,
        CancellationToken cancellationToken,
        Action<SqliteCommand> configure)
    {
        _ = await ExecuteAsync(sql, cancellationToken, configure);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 15000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static DeviceRegistration ReadDevice(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        (byte[])reader[2],
        ParseDate(reader.GetString(3)),
        reader.IsDBNull(4) ? null : ParseDate(reader.GetString(4)),
        reader.IsDBNull(5) ? null : ParseDate(reader.GetString(5)));

    private static InvoiceJob ReadJob(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        ParseState(reader.GetString(2)),
        reader.GetInt32(3),
        reader.GetInt32(4),
        reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        ParseDate(reader.GetString(7)),
        ParseDate(reader.GetString(8)));

    private static InvoiceRevisionRecord ReadRevision(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        reader.GetInt32(2),
        reader.GetString(3),
        reader.GetString(4),
        Guid.Parse(reader.GetString(5)),
        ParseDate(reader.GetString(6)),
        reader.IsDBNull(7) ? null : ParseDate(reader.GetString(7)));

    private static LocalCatalogItem ReadCatalogItem(SqliteDataReader reader)
    {
        var flags = JsonSerializer.Deserialize<CatalogQualityFlag[]>(reader.GetString(8)) ?? [];
        var barcodes = JsonSerializer.Deserialize<string[]>(reader.GetString(12)) ?? [];
        var vendorCodes = JsonSerializer.Deserialize<string[]>(reader.GetString(13)) ?? [];
        return new LocalCatalogItem(
            reader.GetString(0),
            ParseDecimal(reader.GetString(1)),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            ParseDirection(reader.GetString(7)),
            flags,
            new CatalogIdentifiers(
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                barcodes,
                vendorCodes),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.IsDBNull(16) ? null : reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.GetBoolean(18),
            reader.GetBoolean(19),
            ParseDate(reader.GetString(20)));
    }

    private static void AddJobParameters(SqliteCommand command, InvoiceJob job)
    {
        command.Parameters.AddWithValue("$job_id", job.JobId.ToString("D"));
        command.Parameters.AddWithValue("$device_id", job.DeviceId.ToString("D"));
        command.Parameters.AddWithValue("$state", State(job.State));
        command.Parameters.AddWithValue("$expected_page_count", job.ExpectedPageCount);
        command.Parameters.AddWithValue("$created_at", Format(job.CreatedAt));
        command.Parameters.AddWithValue("$updated_at", Format(job.UpdatedAt));
    }

    private static void AddPageParameters(SqliteCommand command, DocumentPage page)
    {
        command.Parameters.AddWithValue("$job_id", page.JobId.ToString("D"));
        command.Parameters.AddWithValue("$page", page.Page);
        command.Parameters.AddWithValue("$mime_type", page.MimeType);
        command.Parameters.AddWithValue("$sha256", page.Sha256);
        command.Parameters.AddWithValue("$object_reference", page.ObjectReference);
        command.Parameters.AddWithValue("$length", page.Length);
        command.Parameters.AddWithValue("$uploaded_at", Format(page.UploadedAt));
    }

    private static void AddRevisionParameters(SqliteCommand command, InvoiceRevisionRecord revision)
    {
        command.Parameters.AddWithValue("$revision_id", revision.RevisionId.ToString("D"));
        command.Parameters.AddWithValue("$job_id", revision.JobId.ToString("D"));
        command.Parameters.AddWithValue("$revision_number", revision.RevisionNumber);
        command.Parameters.AddWithValue("$status", revision.Status);
        command.Parameters.AddWithValue("$json", revision.Json);
        command.Parameters.AddWithValue(
            "$created_by_device_id",
            revision.CreatedByDeviceId.ToString("D"));
        command.Parameters.AddWithValue("$created_at", Format(revision.CreatedAt));
        command.Parameters.AddWithValue(
            "$confirmed_at",
            revision.ConfirmedAt is null ? DBNull.Value : Format(revision.ConfirmedAt.Value));
    }

    private static void AddIdentifier(List<(string Kind, string Value)> list, string kind, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            list.Add((kind, value));
        }
    }

    private static string BuildFtsQuery(string normalized)
    {
        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 1)
            .Take(16)
            .Select(token => $"\"{token.Replace("\"", "\"\"", StringComparison.Ordinal)}\"*")
            .ToArray();
        return tokens.Length == 0 ? "\"__PHARMA_AUTO_NO_MATCH__\"" : string.Join(" OR ", tokens);
    }

    private static string NormalizeSearch(string value) =>
        string.Join(
            ' ',
            value.Normalize(NormalizationForm.FormKC)
                .Trim()
                .ToUpperInvariant()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O");

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string FormatDecimal(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static decimal ParseDecimal(string value) =>
        decimal.Parse(value, CultureInfo.InvariantCulture);

    private static object Db(string? value) => (object?)value ?? DBNull.Value;

    private static string State(InvoiceJobState state) => state.ToString().ToUpperInvariant();

    private static InvoiceJobState ParseState(string state) =>
        Enum.Parse<InvoiceJobState>(state, ignoreCase: true);

    private static CatalogDisplayDirection ParseDirection(string value) => value switch
    {
        "LeftToRight" => CatalogDisplayDirection.Ltr,
        "RightToLeft" => CatalogDisplayDirection.Rtl,
        _ => Enum.Parse<CatalogDisplayDirection>(value, ignoreCase: true)
    };

    private const string SchemaSql = """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = FULL;
        PRAGMA foreign_keys = ON;

        CREATE TABLE IF NOT EXISTS pairing_sessions(
            session_id TEXT PRIMARY KEY,
            secret_hash BLOB NOT NULL,
            expires_at TEXT NOT NULL,
            created_at TEXT NOT NULL,
            consumed_at TEXT
        );

        CREATE TABLE IF NOT EXISTS devices(
            device_id TEXT PRIMARY KEY,
            display_name TEXT NOT NULL,
            public_key_spki BLOB NOT NULL,
            paired_at TEXT NOT NULL,
            revoked_at TEXT,
            last_seen_at TEXT
        );

        CREATE TABLE IF NOT EXISTS access_challenges(
            challenge_id TEXT PRIMARY KEY,
            device_id TEXT NOT NULL REFERENCES devices(device_id),
            nonce TEXT NOT NULL,
            expires_at TEXT NOT NULL,
            consumed_at TEXT
        );

        CREATE TABLE IF NOT EXISTS invoice_jobs(
            job_id TEXT PRIMARY KEY,
            device_id TEXT NOT NULL REFERENCES devices(device_id),
            state TEXT NOT NULL,
            expected_page_count INTEGER NOT NULL CHECK(expected_page_count BETWEEN 1 AND 100),
            uploaded_page_count INTEGER NOT NULL DEFAULT 0,
            current_revision_id TEXT,
            failure_code TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS upload_chunks(
            job_id TEXT NOT NULL REFERENCES invoice_jobs(job_id),
            page INTEGER NOT NULL,
            chunk_index INTEGER NOT NULL,
            chunk_count INTEGER NOT NULL,
            chunk_sha256 TEXT NOT NULL,
            page_sha256 TEXT NOT NULL,
            mime_type TEXT NOT NULL,
            object_reference TEXT NOT NULL,
            length INTEGER NOT NULL,
            uploaded_at TEXT NOT NULL,
            PRIMARY KEY(job_id, page, chunk_index)
        );

        CREATE TABLE IF NOT EXISTS document_pages(
            job_id TEXT NOT NULL REFERENCES invoice_jobs(job_id),
            page INTEGER NOT NULL,
            mime_type TEXT NOT NULL,
            sha256 TEXT NOT NULL,
            object_reference TEXT NOT NULL,
            length INTEGER NOT NULL,
            uploaded_at TEXT NOT NULL,
            PRIMARY KEY(job_id, page)
        );

        CREATE TABLE IF NOT EXISTS invoice_revisions(
            revision_id TEXT PRIMARY KEY,
            job_id TEXT NOT NULL REFERENCES invoice_jobs(job_id),
            revision_number INTEGER NOT NULL,
            status TEXT NOT NULL,
            json TEXT NOT NULL,
            created_by_device_id TEXT NOT NULL REFERENCES devices(device_id),
            created_at TEXT NOT NULL,
            confirmed_at TEXT,
            UNIQUE(job_id, revision_number)
        );

        CREATE TABLE IF NOT EXISTS catalog_items(
            local_item_reference TEXT PRIMARY KEY,
            genius_item_id TEXT NOT NULL UNIQUE,
            raw_arabic_label TEXT,
            raw_english_label TEXT,
            display_label TEXT,
            normalized_label TEXT NOT NULL,
            raw_arabic_hash TEXT,
            raw_english_hash TEXT,
            display_direction TEXT NOT NULL,
            quality_flags_json TEXT NOT NULL,
            item_code TEXT,
            secondary_code TEXT,
            international_code TEXT,
            barcodes_json TEXT NOT NULL,
            vendor_codes_json TEXT NOT NULL,
            active_ingredient TEXT,
            strength TEXT,
            dosage_form TEXT,
            pack TEXT,
            has_expiry INTEGER NOT NULL,
            active INTEGER NOT NULL,
            projected_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS catalog_identifiers(
            local_item_reference TEXT NOT NULL REFERENCES catalog_items(local_item_reference),
            kind TEXT NOT NULL,
            normalized_value TEXT NOT NULL,
            PRIMARY KEY(local_item_reference, kind, normalized_value)
        );
        CREATE INDEX IF NOT EXISTS ix_catalog_identifiers_value
            ON catalog_identifiers(normalized_value, kind);

        CREATE VIRTUAL TABLE IF NOT EXISTS catalog_fts USING fts5(
            local_item_reference UNINDEXED,
            searchable,
            tokenize = 'unicode61 remove_diacritics 2'
        );

        CREATE TABLE IF NOT EXISTS catalog_vendors(
            local_vendor_reference TEXT PRIMARY KEY,
            genius_vendor_id TEXT NOT NULL UNIQUE,
            code TEXT,
            normalized_code TEXT NOT NULL,
            display_name TEXT NOT NULL,
            normalized_name TEXT NOT NULL,
            active INTEGER NOT NULL,
            projected_at TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_catalog_vendors_code ON catalog_vendors(normalized_code);

        CREATE TABLE IF NOT EXISTS catalog_projection_summary(
            singleton_id INTEGER PRIMARY KEY CHECK(singleton_id = 1),
            item_count INTEGER NOT NULL,
            vendor_count INTEGER NOT NULL,
            barcode_count INTEGER NOT NULL,
            vendor_code_count INTEGER NOT NULL,
            untrusted_label_count INTEGER NOT NULL,
            identical_language_field_count INTEGER NOT NULL,
            completed_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS audit_events(
            event_id TEXT PRIMARY KEY,
            actor_type TEXT NOT NULL,
            actor_reference TEXT NOT NULL,
            action TEXT NOT NULL,
            target_reference TEXT NOT NULL,
            result TEXT NOT NULL,
            correlation_id TEXT NOT NULL,
            occurred_at TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_audit_events_correlation ON audit_events(correlation_id);
        """;
}
