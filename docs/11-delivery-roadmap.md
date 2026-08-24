# Delivery Roadmap

## Phase 0 — Evidence and Contracts

Status: **Complete (2026-08-21); closure audit recorded in [Phase 0 Closure](20-phase-0-closure.md).**

Deliverables:

- approved system docs وADRs.
- sanitized test dataset.
- DB fingerprint definition.
- Golden Scenario capture procedure.
- versioned domain/OCR contracts.
- threat model.

Exit gate: لا توجد write assumptions غير مسجلة،وكل critical table side effect له investigation owner.

## Phase 1 — Read-Only Vertical Slice

Status: **Implementation baseline recorded 2026-08-21 and updated 2026-08-24; acceptance pending. Evidence and open limitations are recorded in [Phase 1 Implementation Record](21-phase-1-closure.md).**

- Connector installer/service/control UI.
- Android pairing/upload.
- local catalog extraction،byte reversal إلى raw labels،integrity flags وBiDi-safe display.
- SaaS tenant/subscription/quota.
- Gemini OCR structured result.
- Vendor/Product matching.
- Android review،expiry splitting وper-Posting-Line purchase/discount/selling-price editor.
- لا DB writes.

Acceptance still open:

- API-28+ physical/emulator end-to-end and visual/accessibility acceptance،مع قرار صريح لمسار PAX API 27.
- controlled live Gemini staging run على synthetic data وقياس field accuracy،schema rejection،latency،cost وquota idempotency.
- independent pairing/replay،tenant isolation،file-limit،retention-recovery and local workflow tests tied to a commit/CI evidence record.
- implementation and independent verification of the accepted local human identity/step-up and offline write-grant boundaries in ADR-012 and ADR-013 before any Phase 2 mutation implementation.

Exit gate: OCR/matching metrics معروفة وworkflow قابل للاستخدام ومختبر على target device دون data corruption risk. Implementation existence أوstatic validation وحدهما لا يغلقان هذا gate.

## Phase 2 — Master Item Creation

Genius mutation status: **Blocked until Phase 1 acceptance and completion of the ADR-012/ADR-013 implementation،test and approval gates. Accepting either design decision does not enable writes.**

### Phase 2A — Golden Evidence First

- implement and independently verify per-person Operator/Catalog Manager/Supervisor identity،role provisioning،session revocation،step-up and actor audit per ADR-012.
- implement and independently verify separate `GENIUS_MASTER_ITEM_CREATE` grant،trusted-time and revocation behavior per ADR-013.
- capture manual e-plus New Item،duplicate and unit-conversion scenarios on a disposable isolated Clone using the accepted Golden procedure.
- obtain byte-for-byte read-back،all-table diff،DB Integration second review and Product/Accounting/Security approval where applicable.
- **No Connector Genius mutation command،endpoint or runtime writer exists in Phase 2A.**

### Phase 2B — Clone-Only Writer

- implement New Item wizard،permissions and duplicate checks only after the matching Phase 2A evidence bundle is `PASS`.
- implement Master Item Adapter and `Item_Vendor` linkage inside a disposable Clone profile only.
- enforce DBFP-1،human step-up،capability-specific grant،read-back verification،audit and fault tests.
- keep production/pilot activation absent؛there is no bypass or unsigned lab evidence promoted to a live profile.

Exit gate: every enabled New Item/unit-conversion scenario has an approved Golden bundle،the Clone-only writer matches it،authorization/revocation/fault tests pass،and no production Genius write surface exists.

## Phase 3 — Direct DB Commit Lab

- e-plus before/after capture لكل scenario.
- implement profile داخل Clone فقط.
- stock/class/financial writes.
- certified mapping وimpact rules للـ purchase،discount وselling-price edits.
- reconciliation engine.
- fault injection.

Exit gate: كل mandatory Golden Scenario يطابق business state،صفر partial/duplicate commits في fault suite.

## Phase 4 — Supervised Pilot

- pharmacy واحدة.
- Supervisor يبدأ كل Commit.
- automatic stop عند warning.
- daily reconciliation review.
- rapid rollback للـ Connector binary؛ business correction عبر e-plus.

Exit gate: pilot criteria في testing document.

## Phase 5 — Controlled Production

- staged tenants.
- policy-based Operator commit للسيناريوهات منخفضة المخاطر.
- Supervisor للـ New Items،duplicates،missing expiry والـ financial variations.
- monitoring/SLA/support process.

## Phase 6 — Optimization

لا تبدأ إلا بالبيانات:

- template-specific OCR improvements.
- local mapping automation.
- queue/worker scaling.
- additional ERP profile.
- return invoice profile.

## Workstreams

| Workstream | Depends on |
|---|---|
| Android UX | domain contracts،pairing contract |
| Local Connector core | job model،security identity |
| Genius Adapter | Golden e-plus evidence |
| SaaS OCR | subscription/quota + OCR schema |
| Admin | SaaS RBAC/audit contract |
| Operations | installer،metrics،runbooks |

## Explicit Non-Goals During MVP

- vector DB cluster.
- Kubernetes.
- multiple OCR providers abstraction قبل نجاح provider واحد.
- unattended New Item creation.
- cross-tenant auto-confirm.
- direct DB support لschemas غير مثبتة.

إضافة هذه العناصر مبكرًا تزيد surface area ولا تعالج أكبر خطر: correctness داخل Genius.
