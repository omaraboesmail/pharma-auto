# Write Assumptions and Side-Effect Owners

Status: **Accepted Phase 0 register; all write capabilities are disabled**

This register is the authoritative inventory for reverse-engineered Genius behavior. `REGISTERED_NEEDS_GOLDEN` means the uncertainty is explicit and safely gated; it does not mean the behavior is correct.

## 1. Registration Rule

Every Genius write rule, read-back formula, trigger expectation, data invariant and concurrency claim must cite a `WA-*` entry. An implementation or Golden capture that discovers an unlisted object or behavior stops, creates a new entry, assigns an owner and updates the fingerprint definition before proceeding. There is no catch-all “assumed compatible” state.

## 2. Write-Assumption Register

| ID | Registered assumption or unknown | Required evidence | Primary owner | Current state and gate |
|---|---|---|---|---|
| WA-001 | `pur_trans_h` identity, Vendor bill-number fallback, detail count, totals and security metadata reproduce e-plus behavior. Use of `pth_notice` for correlation may be unsafe. | Credit/cash/partial Golden diffs, duplicate cases, exact read-back and notice-field investigation. | DB Integration Owner | `REGISTERED_NEEDS_GOLDEN`; purchase writes disabled. |
| WA-002 | One ordered Posting Line maps to one `pur_trans_d` row with correct units, quantity, bonus, expiry, batch and commercial snapshots. | Mandatory line/split/unit Golden matrix and detail reconciliation. | DB Integration Owner | `REGISTERED_NEEDS_GOLDEN`; detail writes disabled. |
| WA-003 | Expiry normalization and existing/new `c_id` selection can match e-plus without merging distinct lots or prices. | Same/different expiry, batch and price scenarios with old-stock proof. | DB Integration Owner | `REGISTERED_NEEDS_GOLDEN`; class resolution is read-only. |
| WA-004 | `Item_Class` and `Item_Class_Store` row-at-a-time changes plus their triggers yield the required overall/per-store quantity. | Before/after rows, trigger audit, rollback and concurrency scenarios. | DB Integration Owner | `REGISTERED_NEEDS_GOLDEN`; stock writes disabled. |
| WA-005 | A new selling price can be isolated to newly received stock without changing existing class/store stock or silently changing `Item_Catalog.itm_def_sell_price`. | Changed-price class split, subsequent sales selection, tax and catalog/store scope diffs. | DB Integration Owner | `REGISTERED_NEEDS_GOLDEN`; unsupported isolation returns `CommitRejected`. |
| WA-006 | Discount 1 maps to the proven line-price path; Discount 2 translation, header interaction and rounding are not yet certified. | Discount 1 only, Discount 2 only, combined discounts, unit conversion and decimal-scale evidence. | Pharmacy Accounting SME | `REGISTERED_NEEDS_GOLDEN`; financial translation disabled. |
| WA-007 | Credit, cash and partial payment generate different financial documents and Vendor balance effects. `total_bill` alone is insufficient. | Scenario-specific diffs across all financial tables and `v_VenTrans`, approved by accounting SME. | Pharmacy Accounting SME | `REGISTERED_NEEDS_GOLDEN`; payment writes disabled. |
| WA-008 | Bonus, header discount, other expense and source-tax fields affect detail/header/financial state using legacy formulas. | One isolated scenario per variation plus combined-case reconciliation. | Pharmacy Accounting SME | `REGISTERED_NEEDS_GOLDEN`; variations disabled. |
| WA-009 | New Item identity, code fallback, reversed-name bytes, units/conversions and `Item_Vendor` linkage can be reproduced without corrupting names or creating duplicates. Exact unit-table dependencies remain to be discovered. | New Item and conversion Golden scenarios, byte-for-byte read-back and duplicate tests. | DB Integration Owner | `REGISTERED_NEEDS_GOLDEN`; master-item writes disabled. |
| WA-010 | Final duplicate checks and consistent SQL locks prevent collision with e-plus and another Connector job. `sp_getapplock` protects only Connector peers. | Concurrent manual/Connector scenarios, lock timeout and identity-allocation evidence. | DB Integration Owner | `REGISTERED_NEEDS_GOLDEN`; no write retry after lock acquisition ambiguity. |
| WA-011 | A connection loss near commit can be classified only by read-only reconciliation and durable Sidecar evidence. | Connection-loss/power-loss fault suite and duplicate-proof recovery drill. | DB Integration Owner | `REGISTERED_NEEDS_GOLDEN`; `CommitUnknown` never auto-retries. |
| WA-012 | Required Genius audit/security fields and least-privilege grants can identify the integration without granting schema or unrelated-data access. | e-plus comparison, object-level grant test, audit/read-back and credential-rotation test. | Security Reviewer | `REGISTERED_NEEDS_GOLDEN`; privileged SQL accounts prohibited. |
| WA-013 | Purchase returns require a distinct profile; treating them as negative purchases would be unsafe. | Dedicated return matrix before the feature is proposed. | Product Owner | `REGISTERED_OUT_OF_SCOPE`; return writes unavailable. |
| WA-014 | Runtime settings used by financial/stock behavior, including `Sys_setting.pur_extra_disc_update_stock`, must match the certified baseline; schema equality alone is insufficient. | Named data-invariant capture and Golden comparison for every referenced setting. | DB Integration Owner | `REGISTERED_NEEDS_GOLDEN`; invariant drift blocks writes. |
| WA-015 | `close_stock` on `Item_Class_Store` may snapshot all active-store stock into `ICS_Month_Close` and update `Sys_setting.last_month_close_dt` when a month boundary is pending. A Connector receipt must never become the accidental month-close initiator. | Trigger hash, named-invariant preflight, within-month and pending-month-close Golden scenarios, all-stock diff and operational runbook. | DB Integration Owner | `REGISTERED_NEEDS_GOLDEN`; pending month close blocks Connector writes. |
| WA-016 | `delete_duplicate_records` on `F_Transaction_Header` may create an archive table, archive matching rows and delete existing financial rows for a legacy notes/type condition. Purchase-generated financial rows must be proven unable to enter the destructive branch unexpectedly. | Require archive table in fingerprint, trigger hash, isolated financial scenarios, notes/type proof and deletion-focused all-table reconciliation. | Pharmacy Accounting SME | `REGISTERED_NEEDS_GOLDEN`; financial writes disabled. |
| WA-017 | Vendor and stock triggers write audit rows using `master.dbo.sysprocesses` program/host values, and `VUPDATEDATE` self-updates Vendor metadata. Audit rows and identity text are required side effects, not ignorable noise. | Trigger-specific read-back, least-privilege access test and exact `watch_qty_chng`/`vendor_credit_chng` reconciliation. | Security Reviewer | `REGISTERED_NEEDS_GOLDEN`; affected writes disabled. |

## 3. Critical Object and Side-Effect Ownership

| Object or surface | Possible side effect or dependency | Investigation owner | Required co-review |
|---|---|---|---|
| `dbo.pur_trans_h` | identity, Vendor invoice number, totals, payment, tax, audit metadata | DB Integration Owner | Pharmacy Accounting SME for monetary fields |
| `dbo.pur_trans_d` | ordered lines, quantities, unit snapshots, discounts, tax, purchase/selling snapshots | DB Integration Owner | Pharmacy Accounting SME |
| `dbo.Item_Class` | lot/class identity, expiry/batch, aggregate quantity and selling price | DB Integration Owner | Pharmacy Accounting SME for pricing |
| `dbo.Item_Class_Store` | per-store quantity/price and trigger-driven effects | DB Integration Owner | DB Integration Second Reviewer |
| Triggers attached to critical tables | hidden history, stock or audit effects; row-at-a-time behavior | DB Integration Owner | DB Integration Second Reviewer |
| `dbo.Item_Catalog` | master identity, raw name bytes, units and optional catalog-default price | DB Integration Owner | Product Owner; Pharmacy Accounting SME for price |
| `dbo.Item_Vendor` | Vendor linkage, code, price/discount/min-expiry metadata | DB Integration Owner | Product Owner |
| `dbo.Vendor` | Vendor identity and balance-related dependency; direct mutation is not assumed | Pharmacy Accounting SME | DB Integration Owner |
| `dbo.F_Auto_Doc_h` / `dbo.F_Auto_Doc_d` | generated financial document and ledger lines | Pharmacy Accounting SME | DB Integration Owner |
| `dbo.F_Transaction_Header` / `dbo.F_Transaction_Bills` | financial transaction/payment/bill linkage | Pharmacy Accounting SME | DB Integration Owner |
| `dbo.F_Transaction_Header_SaveDeleteRecords` | archive target used by the legacy duplicate-delete trigger; its absence can cause runtime DDL | Pharmacy Accounting SME | Security Reviewer |
| `dbo.v_VenTrans` | Vendor balance formula used for independent reconciliation | Pharmacy Accounting SME | DB Integration Second Reviewer |
| `dbo.Sys_setting` named invariants | runtime behavior switches; read-only dependency | DB Integration Owner | Security Reviewer for drift handling |
| `dbo.ICS_Month_Close` | whole-stock month-close snapshot and per-class correction/delete target | DB Integration Owner | Pharmacy Accounting SME |
| `dbo.Store` | active-store filter used by stock triggers | DB Integration Owner | Product Owner |
| `dbo.watch_qty_chng` | stock quantity/expiry audit rows created by `watch_stock` | DB Integration Owner | Security Reviewer |
| `dbo.vendor_credit_chng` | Vendor credit audit rows created by `watch_vendor_credit_chnge` | Pharmacy Accounting SME | Security Reviewer |
| `master.dbo.sysprocesses` | read-only program/host source used by legacy audit triggers | Security Reviewer | DB Integration Owner |
| Sidecar commit journal | idempotency, correlation and `CommitUnknown` recovery state | DB Integration Owner | Security Reviewer |
| Any changed or referenced unlisted object | unknown side effect or dependency | DB Integration Owner immediately | Relevant domain owner before classification |

## 4. Exit-Gate Enforcement

Phase 0 closes the documentation gap by registering every currently known write uncertainty and assigning every critical side-effect surface to a role. This does **not** close the Golden evidence gaps. Any new assumption discovered after Phase 0 must be registered before code or evidence depending on it can be accepted.
