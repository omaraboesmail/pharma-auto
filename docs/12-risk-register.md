# Production Risk Register

## Scoring

- **Critical:** قد ينتج silent stock/financial corruption أوtenant-wide security incident.
- **High:** يوقف التشغيل أوينتج data exposure محدودة.
- **Medium:** يؤثر التكلفة،الدقة أوالدعم دون corruption مباشر.

## Active Risks

| ID | Risk | Severity | Detection | Required treatment |
|---|---|---|---|---|
| R-01 | Reverse-engineered Purchase logic ناقصة | Critical | Golden/reconciliation mismatch | منع Live Commit حتى تغطية كل enabled financial scenario |
| R-02 | e-plus يكتب بالتزامن مع Connector | Critical | lock waits،identity/financial conflicts | consistent DB locks،short transactions،queue وpilot observation |
| R-03 | `CommitUnknown` يعاد فينشئ duplicate | Critical | duplicate fingerprint/commit journal | hard no-retry gate وread-only investigation |
| R-04 | Wrong Pharma match رغم vector similarity | Critical | hard mismatch أوuser correction | exact/structured constraints،pgvector candidates فقط،human confirmation |
| R-05 | Unit conversion في New Item خاطئة | Critical | stock delta غير منطقي | permission،explicit conversion review،Golden unit scenarios |
| R-06 | SQL 2008 R2 خارج الدعم/TLS incompatible | High | installer connectivity/security check | required patch أوisolated local risk acceptance؛لا system-wide TLS downgrade |
| R-07 | Row-at-a-time triggers تتلقى bulk update | Critical | trigger audit mismatch | single-row statements وGolden trigger tests |
| R-08 | Financial auto-doc أوVendor balance غير صحيح | Critical | mandatory reconciliation | scenario-specific strategies وaccounting SME approval |
| R-09 | Cross-tenant mapping leakage/poisoning | High | tenant isolation/anomaly tests | anonymization،minimum support،no cross-tenant auto-confirm |
| R-10 | Admin account compromise | High | privileged audit/anomaly alerts | mandatory AAL2،RBAC،break-glass وshort sessions |
| R-11 | Invoice file retention تتجاوز policy | High | deletion backlog metric | encrypted TTL،verified deletion وalert |
| R-12 | Gemini cost يتجاوز subscription revenue | Medium | cost/page/tenant metrics | atomic quota،page-based plans وprovider budget limits |
| R-13 | Embedding model change يغير candidate behavior | Medium | offline recall benchmark | versioned embeddings،parallel reindex وrollback |
| R-14 | DB fingerprint يفشل بعد local customization | High | startup/preflight mismatch | writes disabled؛new profile review،لا bypass flag |
| R-15 | Local workstation compromise | High | endpoint/security telemetry | least privilege،device cert،encrypted storage،incident revocation |

## Release-Blocking Risks

لا يمكن قبول Production إذا بقي أي من الآتي بلا evidence:

- R-01،R-02،R-03،R-04،R-05،R-07 أوR-08.
- SQL account يستخدم `sa` أو`db_owner`.
- reconciliation غير مفعلة لكل Commit.
- actual duplicate scenario غير مختبر.
- backup/restore وpower-loss tests غير ناجحة.

## Ownership Rule

كل risk يحتاج owner،due date،evidence link وresidual-risk acceptance قبل Pilot. استخدام كلمة “assumed” ليس treatment؛ إما اختبار أوfeature تعطيل.
