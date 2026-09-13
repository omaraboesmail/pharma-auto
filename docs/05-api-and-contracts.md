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
- pairing response لا يحمل pharmacy-human role؛device identity وhuman identity مستقلتان.

### Planned Phase 2 Local Human Identity and Session

The normative policy is [ADR-012](decisions/ADR-012-local-human-identity-and-step-up.md). The required contract families below are not present in the current Phase 1 OpenAPI or runtime؛they must be versioned and must contain no credential verifier or recovery secret.

- enroll the first Supervisor through a single-use local ceremony،then provision/disable local users and assign versioned roles وفق authorization policy.
- authenticate/switch/lock the active human actor على paired device and issue a device-bound + actor-bound local session.
- revoke a session،list the current actor's effective permissions and require re-authentication after idle/absolute expiry.
- request a single-use step-up approval bound to exact action،immutable revision/command ID،initiator،approver،device،impact hash and expiry.
- Android never supplies the authoritative `actorId`،role أوapproval result؛Connector derives them from authenticated state.

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

Android يقبل JPEG/PNG أوPDF من المستخدم. كل PDF يُفحص ويُرسم محليًا إلى JPEG pages مرتبة قبل إنشاء draft؛Local API ثم Connector ↔ SaaS OCR transport يقبلان صفحات `image/jpeg` أو`image/png` فقط ولا ينقلان raw PDF. `pageCount`،upload index وOCR quota كلها تعد كل normalized page مرة واحدة،لذلك PDF متعدد الصفحات لا يمكن احتسابه كصفحة واحدة.

Local API لا يقبل caller-provided `pth_id`, `c_id` أوfinal SQL values.

**Phase 1 implementation surface stops at `confirm revision`،and acceptance remains pending.** `request commit` وcommit/reconciliation states في القائمة أعلاه contracts مستقبلية للمراحل write-enabled؛لا يوجد لها endpoint في `local-connector.v1.json` أوruntime حاليًا. Full-catalog manual selection يعيد Connector التحقق منها مقابل Sidecar الحالية ويضيف candidate evidence بنفسه؛Android لا يستطيع اختراع opaque local reference. اعتماد ADR-012 وADR-013 يثبت boundary فقط ولا يفعّل write؛Phase 2 لا يضيف mutation endpoint قبل اكتمال implementation/verification gates للقرارين وGolden evidence على disposable Clone.

## 3. Connector ↔ SaaS

كل request يستخدم mTLS connector identity بالإضافة إلى tenant/Connector headers،nonce،timestamp،content hash وHMAC signature. Connector يتحقق من ES256 entitlement payload قبل إرسال OCR work.

### Entitlement

- activate Connector.
- fetch signed OCR/subscription entitlement.
- Phase 2 required surface،not current runtime: fetch separately signed capability grants مثل `GENIUS_MASTER_ITEM_CREATE` و`GENIUS_PURCHASE_COMMIT`؛OCR entitlement لا تحتوي write authority.
- each future grant includes Connector/pharmacy/profile/fingerprint scope،capability،policy version،grant ID،revocation generation،capability-scoped issuance sequence and signed time window.
- report highest observed generation/sequence and invalidate cached grants on authoritative revocation،certificate rejection or subscription denial.
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

Canonical editable amounts and quantities use an unsigned `DECIMAL(18,6)` lexical contract:
1..12 integer digits (or the single value `0`), optionally followed by a dot and 1..6
fractional digits, with a maximum value of `999999999999.999999`. Percentages are unsigned
canonical strings from `0` through `100`, allow at most four fractional digits, and may express
`100` only with zero fractional digits. Signs،exponents،grouping separators،partial decimals and
leading zeroes are rejected. These bounds keep Connector `decimal` calculations below overflow;
Android may accept Arabic digits for entry but emits ASCII canonical strings only at the contract
boundary.

### Planned Phase 2 Local Human Principal and Step-Up Approval

- opaque `actorId`،actor status and role-policy version scoped to tenant/pharmacy/Connector.
- session reference bound to actor + paired device،with authentication time،idle expiry and absolute expiry.
- effective permission reason codes evaluated server-side; no caller-supplied roles.
- step-up reference bound to action،target immutable revision/command،initiating actor،approving actor،device،impact hash،policy version،issued time and short expiry.
- approval is single-use؛role/revision/impact change invalidates it.

### Planned Phase 2 Offline Genius Write Grant

The normative semantics are [ADR-013](decisions/ADR-013-offline-genius-write-entitlements.md).

- signed capability name distinct from OCR/quota entitlement.
- tenant،pharmacy،Connector installation،Genius profile and approved fingerprint scope.
- grant ID،policy version،revocation generation،capability-scoped monotonic issuance sequence،`issuedAt`،`notBefore` and `expiresAt`.
- maximum signed lifetime 24 hours؛production default eight hours.
- protected trusted-time/high-water evidence is local state،not caller input؛offline write eligibility does not survive a service/OS restart without online revalidation.
- confirmation does not extend a grant. Validity is checked again immediately before a DB transaction begins.

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
- step-up approval single-use ومربوطة بالـ command/revision/impact hash؛replay أوrevision مختلفة تفشل.
- write idempotency key لا يتجاوز expiry/revocation/trusted-time أوhuman authorization gates.
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
| Authorization | expired grant،revoked device/user،missing role/step-up أوclock rollback | بعد re-auth/online renewal أوtrusted-time remediation؛لا bypass |
| Quota | exhausted page limit | بعد plan/period change |
| OCR transient | provider timeout | controlled retry داخل نفس reservation |
| Matching | no acceptable Item | manual selection/New Item |
| DB preflight | fingerprint mismatch | لا retry حتى remediation |
| DB contention | locks unavailable | queued retry قبل أي write |
| Commit ambiguous | connection lost near commit | no automatic retry |
| Reconciliation | financial/stock mismatch | supervised investigation |
