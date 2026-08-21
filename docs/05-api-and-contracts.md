# API and Contract Boundaries

## 1. Contract Strategy

كل contract يكون versioned وmachine-validatable قبل implementation. التنسيقات المرجعية:

- OpenAPI 3.1 للـ REST APIs.
- JSON Schema للـ OCR/domain payloads.
- AsyncAPI فقط إذا أضيف message broker فعليًا؛ لا يُنشأ لمجرد التوثيق.
- semantic event versions منفصلة عن application release.

الـ contracts لا تحتوي Genius table names. التحويل إلى DB fields مسؤولية Local Adapter.

## 2. Android ↔ Local Connector

### Pairing

- create one-time pairing session.
- exchange device public identity.
- issue/revoke device credential.
- fetch Connector/pharmacy display identity.

### Catalog

- search Vendors.
- search Items.
- fetch Item detail and units.
- request New Item duplicate check.
- submit/track Catalog Creation Command.

### Invoice

- create draft.
- upload pages resumably.
- get processing state.
- fetch OCR/matching revision.
- submit field corrections.
- submit per-Posting-Line purchase unit price،Discount 1%،Discount 2% وselling unit price corrections مع original-value references.
- add/remove/reorder expiry splits within Source Line rules.
- confirm revision.
- request commit.
- observe commit/reconciliation state.

Local API لا يقبل caller-provided `pth_id`, `c_id` أوfinal SQL values.

**Phase 1 implemented surface stops at `confirm revision`.** `request commit` وcommit/reconciliation states في القائمة أعلاه contracts مستقبلية للمراحل write-enabled؛لا يوجد لها endpoint في `local-connector.v1.json` أوruntime حاليًا. Full-catalog manual selection يعيد Connector التحقق منها مقابل Sidecar الحالية ويضيف candidate evidence بنفسه؛Android لا يستطيع اختراع opaque local reference.

## 3. Connector ↔ SaaS

كل request يستخدم mTLS connector identity بالإضافة إلى tenant/Connector headers،nonce،timestamp،content hash وHMAC signature. Connector يتحقق من ES256 entitlement payload قبل إرسال OCR work.

### Entitlement

- activate Connector.
- fetch signed entitlement.
- report health and version.
- revoke/rotate certificate.

### OCR

- reserve OCR quota.
- initialize resumable document upload.
- submit OCR job.
- poll/fetch validated result.
- settle/release reservation.

### Status

- send state metadata دون raw invoice lines افتراضيًا.
- report final usage and reconciliation category.
- send security/operational events.

## 4. Admin Portal ↔ SaaS

- Supabase Auth login وMFA enrollment/challenge.
- tenant/subscription CRUD وفق RBAC.
- Connector/device list and revoke.
- usage،cost وhealth views.
- support case and break-glass request.
- audit search.

الـ SaaS API يتحقق من Supabase JWT signature،issuer،audience،expiry و`aal2` للعمليات privileged. لا يعتمد على UI لإجبار MFA.

## 5. Core Payload Semantics

### OCR Field

- raw value.
- normalized value.
- page and bounding box.
- evidence text.
- model signal.
- validation warnings.
- user-confirmation state.

### Product Candidate

- opaque local Item reference.
- display name/codes.
- raw local label منفصلة عنcanonical/user-confirmed display label.
- name source،quality flags وlanguage-field equality indicator.
- structured Pharma attributes.
- reason codes.
- hard mismatch list.
- prior mapping evidence category.
- canonical product reference عند وجوده،دون اعتباره local Item ID.

لا يوجد single confidence percentage مطلوب للعرض.

### Product Label Contract

- `rawLabel`: نتيجة byte reversal/code-page decode دون heuristic repair.
- `rawLabelHash`: hash للـ source bytes لأغراض audit/cache invalidation.
- `labelSource`: Genius raw،Canonical Catalog أوmanual confirmation.
- `qualityFlags`: مثل `LANGUAGE_FIELDS_IDENTICAL`, `MALFORMED_BIDI`, `TRUNCATED_OR_CORRUPT`, `UNVERIFIED`.
- `displayDirection`: hint للعرض فقط،ولا يغيّر النص.
- `canonicalLabel`: optional؛لا يُشتق تلقائيًا من raw label التالف.

### Canonical Retrieval Request/Result

- normalized OCR description.
- structured Pharma constraints.
- locale وVendor context المسموح بهما.
- embedding schema/model version.
- top candidate limit.
- returned canonical fields،lexical/vector reason components وhard mismatch flags.

لا يرسل Connector full local catalog لهذا endpoint. ولا يستطيع SaaS إصدار `itm_id` نهائي.

### Posting Line

- parent Source Line ID.
- split ID/index.
- final posting sequence.
- local Item reference.
- expiry/batch/serial.
- quantities،units وconversion factors.
- purchase unit price مع currency،unit basis وtax treatment.
- exactly two ordered percentage-discount objects: first targets purchase unit price،second targets remaining line subtotal.
- selling unit price: `EGP` per `BOX`،tax-inclusive.
- selling-price policy snapshot: `NEW_STOCK_ONLY` + `PRESERVE_EXISTING_STOCK` + `BLOCK_COMMIT` when isolation is unsupported.
- original OCR values وcorrection actor/reason metadata.
- selling-price impact intent،affected scope وauthorization state عندما ينتج master-data side effect.
- validation flags.

### Commit Result

- job/revision identity.
- state.
- generated internal invoice ID عند معرفته.
- effective Vendor bill number.
- reconciliation checks and failures.
- timestamps.
- retry prohibition flag.

## 6. Idempotency Rules

- `job_id` ثابت لدورة المستند.
- upload parts لها content hashes.
- OCR reservation keyed by tenant + job + OCR revision.
- Catalog Creation Command لها command ID مستقلة.
- commit keyed by confirmed revision ID.
- إعادة نفس command ترجع النتيجة السابقة ولا تنشئ write جديدة.
- revision مختلفة لا تعيد استخدام successful commit key.

## 7. Version Compatibility

- Android يعلن supported Local API range.
- Connector يعلن supported SaaS contract range وGenius profile.
- SaaS يرفض obsolete insecure Connector versions وفق staged rollout policy.
- breaking contract يتطلب major version ومسار migration.
- OCR schema version محفوظ مع كل result وmapping event.

## 8. Error Taxonomy

| الفئة | مثال | Retry policy |
|---|---|---|
| Validation | malformed PDF أوinvalid expiry | بعد user correction |
| Authorization | expired entitlement أوrevoked device | بعد re-auth/renewal |
| Quota | exhausted page limit | بعد plan/period change |
| OCR transient | provider timeout | controlled retry داخل نفس reservation |
| Matching | no acceptable Item | manual selection/New Item |
| DB preflight | fingerprint mismatch | لا retry حتى remediation |
| DB contention | locks unavailable | queued retry قبل أي write |
| Commit ambiguous | connection lost near commit | no automatic retry |
| Reconciliation | financial/stock mismatch | supervised investigation |
