# Production Risk Register

## Scoring

- **Critical:** قد ينتج silent stock/financial corruption أوtenant-wide security incident.
- **High:** يوقف التشغيل أوينتج data exposure محدودة.
- **Medium:** يؤثر التكلفة،الدقة أوالدعم دون corruption مباشر.

## Active Risks

| ID | Risk | Severity | Detection | Required treatment | Owner / due gate | Evidence and residual state |
|---|---|---|---|---|---|---|
| R-01 | Reverse-engineered Purchase logic ناقصة | Critical | Golden/reconciliation mismatch | منع Live Commit حتى تغطية كل enabled financial scenario | DB Integration Owner / Phase 3 exit | `WA-001`–`WA-008`؛open until approved Golden bundles |
| R-02 | e-plus يكتب بالتزامن مع Connector | Critical | lock waits،identity/financial conflicts | consistent DB locks،short transactions،queue وpilot observation | DB Integration Owner / Phase 3 exit | `WA-010`, `TM-13`؛open |
| R-03 | `CommitUnknown` يعاد فينشئ duplicate | Critical | duplicate fingerprint/commit journal | hard no-retry gate وread-only investigation | DB Integration Owner / Phase 3 exit | `WA-011`, `TM-12`؛open |
| R-04 | Wrong Pharma match رغم vector similarity | Critical | hard mismatch أوuser correction | exact/structured constraints،pgvector candidates فقط،human confirmation | Product Owner / Pilot entry | ADR-003،`TM-15` and matching benchmark؛open |
| R-05 | Unit conversion في New Item خاطئة | Critical | stock delta غير منطقي | permission،explicit conversion review،Golden unit scenarios | DB Integration Owner + Product Owner / Phase 2 exit | `WA-009`؛open |
| R-06 | SQL 2008 R2 خارج الدعم/TLS incompatible | High | installer connectivity/security check | required patch أوisolated local risk acceptance؛لا system-wide TLS downgrade | Security Reviewer + Local Technician / Pilot entry | deployment-specific TLS evidence and signed residual acceptance؛open |
| R-07 | Row-at-a-time triggers تتلقى bulk update | Critical | trigger audit mismatch | single-row statements وGolden trigger tests | DB Integration Owner / Phase 3 exit | `WA-004`, `TM-18`؛open |
| R-08 | Financial auto-doc أوVendor balance غير صحيح | Critical | mandatory reconciliation | scenario-specific strategies وaccounting SME approval | Pharmacy Accounting SME / Phase 3 exit | `WA-007`, `WA-008`؛open |
| R-09 | Cross-tenant mapping leakage/poisoning | High | tenant isolation/anomaly tests | anonymization،minimum support،no cross-tenant auto-confirm | SaaS Security Owner / Pilot entry | `TM-08` plus tenant-isolation report؛open |
| R-10 | Admin account compromise | High | privileged audit/anomaly alerts | mandatory AAL2،RBAC،break-glass وshort sessions | Security Reviewer / Admin production entry | `TM-09`؛open |
| R-11 | Invoice file retention تتجاوز policy | High | deletion backlog metric | encrypted TTL،verified deletion وalert | SaaS Operations Owner / Production entry | `TM-10` and deletion-recovery report؛open |
| R-12 | Gemini cost يتجاوز subscription revenue | Medium | cost/page/tenant metrics | atomic quota،page-based plans وprovider budget limits | SaaS Operations Owner + Product Owner / paid OCR rollout | `TM-16` and measured provider-cost baseline؛open |
| R-13 | Embedding model change يغير candidate behavior | Medium | offline recall benchmark | versioned embeddings،parallel reindex وrollback | SaaS OCR Owner / each embedding upgrade | ADR-003 benchmark/reindex evidence؛open per version |
| R-14 | DB fingerprint يفشل بعد local customization | High | startup/preflight mismatch | writes disabled؛new profile review،لا bypass flag | DB Integration Owner / every profile activation | DBFP-1 comparison؛fail-closed residual by design |
| R-15 | Local workstation compromise | High | endpoint/security telemetry | least privilege،device cert،encrypted storage،incident revocation | Security Reviewer / Pilot entry | `TM-14` and pharmacy compensating-control acceptance؛open |
| R-16 | Corrupted Genius name يتحول إلى false product match | Critical | raw-name flags،identifier mismatch،manual corrections | identifiers أولًا،name-only auto-match ممنوع،Canonical/manual overlay وBiDi-safe display | Product Owner / Pilot entry | ADR-010،`TM-15` and mixed-script regression report؛manual-review residual remains |
| R-17 | أول stock write بعد month boundary يشغّل `close_stock` ويغير snapshot/settings على نطاق واسع | Critical | named preflight + all-table Golden diff | block Connector writes عند pending month close؛e-plus/runbook أوprofile certified فقط | DB Integration Owner / Phase 3 exit | `WA-015`, `TM-18`؛open |
| R-18 | `delete_duplicate_records` يحذف financial rows أوينشئ archive table كـ trigger side effect | Critical | trigger hash،archive-table invariant وdeletion reconciliation | financial writes disabled حتى scenario يثبت type/notes guard والـ complete side effect | Pharmacy Accounting SME / Phase 3 exit | `WA-016`, `TM-18`؛open |
| R-19 | stock/Vendor audit triggers تفقد attribution أوrows بسبب permissions/row shape | High | audit row reconciliation | least-privilege read/write evidence،single-row statements وprogram/host verification | Security Reviewer / before affected Phase 2/3 write | `WA-017`؛open |
| R-20 | paired device،shared credential أوstale role ينتحل Operator/Supervisor ويصدر New Item/Commit أوaudit actor كاذب | Critical | actor/session/role/approval mismatch | per-person local identity،device+actor sessions،versioned deny-default roles،revocation وaction-bound step-up؛لا caller-supplied actor | Security Reviewer + Product Owner / Phase 2A exit،before any Genius mutation path | [ADR-012](decisions/ADR-012-local-human-identity-and-step-up.md), `TM-19`؛design accepted،implementation/evidence open |
| R-21 | OCR entitlement،expired/replayed grant،revoked Connector أوclock rollback يمدد offline Genius write authority | Critical | capability/generation/time/preflight mismatch | separate signed write capabilities،8h default/24h max TTL،monotonic trusted time within one service/OS run،online revalidation after restart،replay/revocation gates وno bypass | Security Reviewer + SaaS Operations Owner / Phase 2A exit،before any Genius mutation path | [ADR-013](decisions/ADR-013-offline-genius-write-entitlements.md), `TM-20`؛design accepted،implementation/evidence open; offline revocation exposure needs Pilot acceptance |

## Release-Blocking Risks

لا يمكن قبول Production إذا بقي أي من الآتي بلا evidence:

- R-01،R-02،R-03،R-04،R-05،R-07،R-08،R-16،R-17،R-18،R-20 أوR-21.
- SQL account يستخدم `sa` أو`db_owner`.
- reconciliation غير مفعلة لكل Commit.
- actual duplicate scenario غير مختبر.
- backup/restore وpower-loss tests غير ناجحة.

## Ownership Rule

كل risk يحتاج role owner،due date أوnamed delivery gate،evidence link وresidual-risk acceptance قبل Pilot. الجدول أعلاه هو الحد الأدنى التشغيلي؛calendar commitment يمكن أن يعيش في tracker مرتبط،لكن لا يجوز أن تختفي owner أوgate أوevidence state منه. استخدام كلمة “assumed” ليس treatment؛ إما اختبار أوfeature تعطيل.
