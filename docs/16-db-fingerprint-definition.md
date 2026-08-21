# DB Fingerprint Definition

Status: **Accepted Phase 0 definition; all Genius writes remain disabled**

Definition: `DBFP-1` for `EPLUS_GENIUS_DB539_PROFILE_1`.

## 1. Safety Boundary

The fingerprint answers one question: does the connected database expose the exact metadata surface certified for this profile? It does not prove financial behavior and does not authorize a write. Golden Scenario certification, fault testing and reconciliation remain separate gates.

The machine-readable definition is [fingerprint-definition.v1.json](../local-connector/profiles/EPLUS_GENIUS_DB539_PROFILE_1/fingerprint-definition.v1.json). It validates against [db-fingerprint-definition.v1.schema.json](../contracts/schemas/db-fingerprint-definition.v1.schema.json).

## 2. Captured Surfaces

For every critical table, the Connector captures:

- schema and object name;
- columns in ordinal order, including SQL type, length, precision, scale, nullability, collation, identity and computed flags;
- primary keys and indexes, including column order, uniqueness, included columns and filter definition;
- attached triggers, enabled state, trigger type and normalized definition hash;
- referenced reconciliation views/functions and their normalized definition hashes;
- named runtime invariants that can change trigger or financial behavior without a schema change;
- database internal version, SQL Server product version, compatibility level, collation and profile version.

Adding a table, view, function or trigger to adapter or reconciliation code requires adding it to the definition before review. Runtime discovery never silently expands the allowed write surface.

## 3. Canonicalization and Hash

`PHARMA_AUTO_DBFP_CANONICAL_JSON_V1` is deterministic:

1. strings use their exact SQL metadata text; no case folding, trimming or Unicode repair;
2. module definitions normalize only CRLF/CR line endings to LF and remove no other character;
3. metadata arrays sort by schema, object kind, object name and ordinal/index-column order;
4. JSON object properties sort by Unicode code point, output is minified UTF-8 without BOM, and integers use base-10 representation;
5. SHA-256 is calculated over those exact bytes and emitted as lowercase hexadecimal.

The approved capture and its hash are immutable evidence. Recreating a hash from a different database is a comparison, not approval.

## 4. Comparison Rules

Any missing critical object, column/type/nullability/identity change, key/index drift, trigger drift, reconciliation-module drift, collation mismatch or unexpected dependency disables all Genius writes. Read-only diagnostics remain available. There is no bypass flag; a changed database requires a new reviewed profile or an explicit update to this profile with new evidence.

Named data invariants are evaluated separately from the immutable metadata hash. `Sys_setting.pur_extra_disc_update_stock` must match the certified value. If `DATEDIFF(month, last_month_close_dt, GETDATE()) > 0`, Connector purchase writes remain blocked so its first stock update cannot accidentally initiate the legacy `close_stock` whole-stock snapshot. `Store.activated` values used by that trigger are captured with the preflight evidence. These rules require Golden certification before any write-era implementation.

`dbo.sysdiagrams` is the only current exclusion. SQL Server tooling created it after restore; it is not a Genius business object or adapter dependency. It is excluded by exact schema/name only. Backup, temporary or customer-created tables are not automatically ignored if a certified module references them.

## 5. Reference Observation

On 2026-08-21, read-only queries against the restored `Genius_Legacy` clone reported:

- SQL Server `10.50.4000.0`;
- compatibility level `80`;
- collation `SQL_Latin1_General_CP1256_CI_AS`;
- 219 `is_ms_shipped = 0` tables, or 218 after the exact `dbo.sysdiagrams` exclusion.
- `pur_extra_disc_update_stock = 1`, `last_month_close_dt = 2026-08-01 00:03:43` with zero pending month boundaries on the observation date, and two stores classified with `activated = 1`.

This observation reconciles the earlier 218-table count. It is **not** a certified write baseline and must never be represented as production approval.

Read-only dependency inspection also identified `ICS_Month_Close`, `Store`, `watch_qty_chng`, `vendor_credit_chng` and `F_Transaction_Header_SaveDeleteRecords` as trigger-related critical objects. They are fingerprinted even when a scenario is not expected to change them.

## 6. Definition Acceptance

Phase 0 accepts the fingerprint format, critical object inventory, mismatch behavior and ownership. A concrete database fingerprint becomes write-certified only after the complete Golden matrix is captured on a disposable clone and approved by the DB Integration Owner plus the required second reviewer.
