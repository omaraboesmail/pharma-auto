# Golden Scenario Capture Procedure

Status: **Accepted Phase 0 procedure; no Golden scenario is claimed complete by this document**

This procedure records the business-equivalent write-set produced when an operator enters a controlled synthetic scenario through e-plus. It runs only against a disposable isolated clone. It never authorizes Pharma Auto to write to Genius.

## 1. Required Roles

- **Scenario Operator:** performs the documented action through e-plus only.
- **DB Integration Owner:** owns capture integrity and table-level investigation.
- **DB Integration Second Reviewer:** independently reviews the diff and prevents self-certification.
- **Pharmacy Accounting SME:** approves every financial interpretation.
- **Security Reviewer:** approves evidence redaction and handling when the source clone contains sensitive legacy data.

One person may perform multiple roles in a lab, but the DB Integration Owner and second-review decision must be separate approvals before certification.

## 2. Preconditions

1. Use a disposable SQL Server 2008 R2 clone isolated from production networks. Record the source backup SHA-256 locally; never commit the backup, database files or credentials.
2. Restore the exact seed and prove the database is online, writable only by the lab operator, and contains no prior scenario residue.
3. Capture `DBFP-1` and its named data invariants. A mismatch stops the run; it cannot be waived inside the scenario. Record `last_month_close_dt`, `pur_extra_disc_update_stock` and the relevant `Store.activated` values without changing them.
4. Choose one row from the mandatory matrix in [Testing and Acceptance](09-testing-and-acceptance.md#4-golden-db-scenario-matrix). Use only synthetic identifiers and values.
5. Link every behavior being tested to one or more `WA-*` entries in the [write-assumption register](18-write-assumptions-and-side-effect-owners.md). An unregistered assumption stops the run until the register is updated.
6. Record e-plus version, Windows locale/time zone, scenario inputs, expected business outcome and the role assignments.

## 3. Before Capture

Create an immutable `before/` snapshot covering **every Genius business table**, not only the expected write-set:

- schema/name and row count;
- ordered primary-key values where a primary key exists;
- a canonical row representation that preserves nulls, exact decimal scale, date/time, binary values as Base64 and text bytes without BiDi mutation;
- a SHA-256 per table plus the complete `DBFP-1` record;
- relevant view/function outputs used for financial or stock reconciliation.

Tables without a primary key are exported as a sorted multiset of canonical rows. `CHECKSUM` or row count alone is only a change detector and is never accepted as proof of equality.

## 4. Manual Reference Action

1. Start screen recording or screenshots with the synthetic input visible and record UTC start time.
2. Enter the scenario through the supported e-plus UI. Do not run custom `INSERT`, `UPDATE`, `DELETE`, trigger toggles or repair scripts.
3. Record every e-plus warning, generated identifier, confirmation and visible total.
4. Stop after the intended transaction completes. Do not perform cleanup inside the same clone.

## 5. After Capture and Diff

Capture the same all-table surface into `after/`, then calculate:

- inserted, updated and deleted canonical rows for every changed table;
- header/detail identities and ordering;
- Item/Class/Store quantity changes;
- purchase, discount, selling-price, tax and unit-conversion snapshots;
- Vendor balance and financial-document effects;
- triggers, audit/history rows and any unexpected table change;
- month-close/archive side effects, including `ICS_Month_Close`, `Sys_setting`, `watch_qty_chng`, `vendor_credit_chng` and `F_Transaction_Header_SaveDeleteRecords`;
- the absence of unrelated changes.

Every changed table must appear in the side-effect owner matrix. A changed unlisted table is `UNEXPECTED`, is assigned immediately to the DB Integration Owner, and forces `INCONCLUSIVE` until investigated and registered. A missing expected side effect is also `INCONCLUSIVE` or `FAIL`, never silently accepted.

## 6. Evidence Bundle

Each bundle uses this layout:

```text
GS-<SCENARIO-ID>/
├─ manifest.v1.json
├─ input/synthetic-scenario.json
├─ before/db-fingerprint.json
├─ before/table-summary.json
├─ after/db-fingerprint.json
├─ after/table-summary.json
├─ diff/changed-objects.json
├─ diff/<schema>.<table>.jsonl
├─ reconciliation/checks.json
├─ eplus/redacted-ui-evidence/
└─ approvals/decisions.json
```

`manifest.v1.json` validates against [golden-scenario-manifest.v1.schema.json](../contracts/schemas/golden-scenario-manifest.v1.schema.json). Every evidence file is hashed; the manifest is immutable after approvals. Raw names, credentials, database files and production identifiers remain outside Git. Committed evidence contains only synthetic values or redacted metadata.

## 7. Decision Rules

A scenario is `PASS` only when:

- `DBFP-1` matches before and after;
- every business table was compared;
- all changed objects are registered and owned;
- expected business state and independent reconciliation agree;
- no unrelated row changed;
- the DB Integration Owner, second reviewer and Pharmacy Accounting SME approve when financial effects exist.

Any ambiguity is `INCONCLUSIVE`; it cannot certify a write rule. A mismatch is `FAIL`. A failed or inconclusive result disables that capability and leaves manual e-plus entry as the recovery path.

## 8. Reset

Destroy or re-restore the disposable clone after evidence is secured. Never delete individual business rows to simulate rollback, and never reuse a mutated clone as the next scenario baseline.
