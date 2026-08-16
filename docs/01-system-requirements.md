# System Requirements

## 1. Functional Requirements

### FR-01 — Connector Pairing

- Android يقترن بـ Local Connector باستخدام one-time QR pairing.
- pairing ينتج device identity قابلة للإلغاء، وليس shared pharmacy password.
- لا يمكن للجهاز الاتصال بصيدلية أخرى دون pairing جديد.

### FR-02 — Document Capture

- يدعم JPEG،PNG وPDF ضمن limits معلنة.
- يحافظ على page order.
- يولد SHA-256 hash للملف الكامل ولكل صفحة.
- يكتشف blur،glare،cropping وrotation قبل upload.

### FR-03 — File Validation

- Local Connector يتحقق من magic bytes وMIME الفعلي.
- يرفض encrypted PDF،embedded files،active content،decompression bombs والصفحات التي تتجاوز limits.
- يحتفظ بالملف temporary encrypted storage مع TTL، وليس cache غير معرفة.

### FR-04 — OCR Job

- SaaS يحجز quota atomically قبل استدعاء Gemini.
- نفس `job_id` لا يستهلك quota مرتين.
- Gemini يرجع schema مقيدة تشمل source text وbounding box لكل field.
- OCR output لا يحتوي على `itm_id` أو`ven_id` نهائيين.

### FR-05 — Vendor Resolution

- resolution النهائي محلي إلى `ven_id`.
- exact confirmed mappings تسبق fuzzy matching.
- duplicate Vendor invoice number يُفحص داخل نفس `ven_id`.

### FR-06 — Product Resolution

- exact identifiers تسبق name/vector similarity.
- النص الناتج من byte reversal يُعامل كـ raw untrusted label،وليس decoded canonical name.
- `itm_name_ar_encrypt = itm_name_en_encrypt` ينتج quality flag ولا يُفترض أنه translation صالح.
- يمنع أي heuristic تعيد ترتيب الحروف أوتنقل suffix/prefix لمحاولة إصلاح Arabic/English BiDi.
- hard mismatches في strength،form،pack أوactive ingredient تمنع suggestion عالية الثقة.
- vector retrieval ينتج shortlist فقط.
- المستخدم يستطيع البحث في Local Catalog كاملًا.

### FR-07 — Expiry Splitting

- كل Source Line يمكن أن يحتوي على عدد غير محدود عمليًا من Posting Lines ضمن invoice limit.
- كل split يحتوي quantity،bonus،expiry،batch،price،discount،tax وunit conversion مستقلة.
- إضافة split تدفع البنود التالية إلى أسفل.
- النظام يولد `posting_sequence` نهائية 1..N؛ وهي التي تصبح `ptd_id`.
- `no_of_items` يساوي N، وليس عدد `itm_id` المميزة.

### FR-08 — New Item

- يتطلب permission `CATALOG_CREATE`.
- يعرض duplicate candidates قبل الإنشاء.
- يتطلب units وconversion factors صريحة.
- يخزن الأسماء في reversed `varbinary` fields طبقًا للـ schema الحالية.
- لا ينسخ Arabic name تلقائيًا إلى English field أوالعكس؛ missing language تبقى missing.
- ينشئ `Item_Vendor` للـ Vendor الحالي عند وجود العلاقة.
- item master creation تسبق invoice commit وتُدقق كعملية منفصلة.

### FR-09 — Invoice Numbering

- يحتفظ بـ `source_invoice_no` داخل Sidecar.
- يستخدم source number في `ven_bill_no` إذا كان موجودًا وغير مستخدم لنفس Vendor.
- duplicate invoice الحقيقية تُمنع.
- missing أوreused number لفاتورة مختلفة يستخدم `pth_id` المولد فعليًا كـ `ven_bill_no`.
- يمنع `COUNT + 1` و`MAX + 1` لتوليد `pth_id`.

### FR-10 — Direct DB Commit

- يعمل فقط على DB fingerprint معتمد.
- يستخدم transaction واحدة لكل invoice بعد اكتمال New Item commands.
- يكتب Posting Lines بالترتيب النهائي.
- يتعامل row-at-a-time مع الجداول ذات triggers غير set-safe.
- يمنع concurrent Connector commits لنفس DB.
- إذا تعذر الحصول على locks المطلوبة، يعيد job إلى queue دون partial write.

### FR-11 — Reconciliation

- يتحقق من header،details،line count/order،classes،store quantities،financial documents وVendor effects.
- لا يصدر success للمستخدم قبل اكتمال reconciliation.
- mismatch ينتج `CommittedNeedsReview` أو`ReconciliationFailed`.

### FR-12 — Recovery

- network/DB timeout بعد write محتمل ينتج `CommitUnknown`.
- `CommitUnknown` يمنع automatic retry.
- correction بعد commit تتم من خلال e-plus business flow أوcompensating transaction معتمدة، لا direct delete.

## 2. Non-Functional Requirements

### Reliability

- durable local queue تتحمل restart وpower loss.
- state transitions idempotent ومبنية على immutable revision.
- Sidecar backup يومي وrestore test ربع سنوي.

### Performance

- Local Catalog search: p95 أقل من 250ms على 100,000 Item.
- فتح invoice review بعد وصول OCR: أقل من ثانيتين محليًا.
- Direct DB transaction target: أقل من 10 ثوانٍ لفاتورة 100 Posting Line، وإلا تُراجع locking strategy.
- لا يوجد parallel commit داخل نفس Genius DB.

### Availability

- Android وConnector يعملان على LAN دون Internet لعمليات capture،queue وreview المخزنة.
- OCR الجديد يحتاج SaaS connectivity.
- SaaS target availability 99.9% بعد مرحلة Production stabilization.

### Maintainability

- contracts versioned ومستقلة عن implementation.
- Genius Adapter معزول عن باقي Connector.
- كل DB behavior مدعوم بـ Golden Scenario.
- لا تنتشر أسماء جداول Genius خارج Adapter وreconciliation modules.

### Auditability

- كل تعديل field،mapping،item creation،number fallback وcommit attempt له actor وtimestamp وrevision.
- logs لا تحتوي raw invoice أوcredentials.
- audit records لا يمكن تعديلها من Android.
- manual name confirmation يحتفظ بالـ raw bytes hash،raw label،canonical selection وactor دون الكتابة العكسية إلى Genius تلقائيًا.

## 3. Job States

`Captured → LocallyValidated → OCRReserved → OCRProcessing → OCRValidated → Matching → AwaitingUserReview → Confirmed → CommitQueued → Preflight → Committing → Reconciling → CommittedAndReconciled`

Failure/review states:

- `Rejected`
- `OCRFailed`
- `MatchingFailed`
- `AwaitingCatalogCreation`
- `CommitRejected`
- `CommitUnknown`
- `CommittedNeedsReview`
- `ReconciliationFailed`

## 4. Role Matrix

| Action | Operator | Catalog Manager | Supervisor | SaaS Admin |
|---|:---:|:---:|:---:|:---:|
| Capture/Review | Yes | Yes | Yes | No |
| Add expiry split | Yes | Yes | Yes | No |
| Create New Item | No | Yes | Yes | No |
| Override missing expiry | No | No | Yes | No |
| Resolve ambiguous duplicate | No | No | Yes | No |
| Start Commit | Policy-based | Policy-based | Yes | No |
| Retry `CommitUnknown` | No | No | No؛ reconciliation فقط | No |
| Manage subscriptions | No | No | No | Scoped role |
| View raw invoice | Tenant users only | Tenant users only | Tenant users only | Break-glass only |
