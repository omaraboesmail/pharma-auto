# Delivery Roadmap

## Phase 0 — Evidence and Contracts

Deliverables:

- approved system docs وADRs.
- sanitized test dataset.
- DB fingerprint definition.
- Golden Scenario capture procedure.
- versioned domain/OCR contracts.
- threat model.

Exit gate: لا توجد write assumptions غير مسجلة،وكل critical table side effect له investigation owner.

## Phase 1 — Read-Only Vertical Slice

- Connector installer/service/control UI.
- Android pairing/upload.
- local catalog extraction،byte reversal إلى raw labels،integrity flags وBiDi-safe display.
- SaaS tenant/subscription/quota.
- Gemini OCR structured result.
- Vendor/Product matching.
- Android review وexpiry splitting.
- لا DB writes.

Exit gate: OCR/matching metrics معروفة وworkflow قابل للاستخدام دون data corruption risk.

## Phase 2 — Master Item Creation

- New Item wizard.
- permissions وduplicate checks.
- Master Item Adapter.
- `Item_Vendor` linkage.
- read-back verification وaudit.

Exit gate: Golden New Item scenarios وunit conversion tests كاملة.

## Phase 3 — Direct DB Commit Lab

- e-plus before/after capture لكل scenario.
- implement profile داخل Clone فقط.
- stock/class/financial writes.
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
