# Vision and Scope

## 1. تعريف المنتج

الاسم المؤقت: **Pharma Invoice Bridge**.

المنتج يحول صور وPDF الخاصة بـ Vendor Purchase Invoices إلى Purchase Transactions داخل e-plus/Genius DB عبر خمس مراحل ملزمة:

1. Capture and validation.
2. Structured OCR.
3. Local Vendor/Product resolution.
4. Human verification and expiry splitting.
5. Certified Direct DB Commit followed by reconciliation.

الهدف ليس إزالة الإنسان بأي ثمن. الهدف هو تقليل data entry مع منع silently wrong stock أوVendor balance.

## 2. المستخدمون

| المستخدم | الصلاحيات الأساسية |
|---|---|
| Pharmacy Operator | Capture، تعديل OCR، اختيار Items، إضافة expiry splits، وإرسال الفاتورة للموافقة |
| Catalog Manager | كل ما سبق بالإضافة إلى إنشاء New Item وربطه بالـ Vendor |
| Pharmacy Supervisor | اعتماد invoices عالية المخاطر، معالجة duplicates وCommit warnings |
| Local Technician | إعداد Connector وDB connection دون الوصول لمحتوى SaaS الإداري |
| SaaS Support | مشاهدة health metadata فقط |
| SaaS Security Admin | break-glass access محدود ومؤقت ومدقق |
| SaaS Billing Admin | subscriptions وquotas دون raw invoices أوSQL access |

## 3. In Scope

- Android capture من camera أوPDF.
- multi-page invoice ordering.
- file sanitization وtemporary encrypted retention.
- Gemini structured OCR من SaaS فقط.
- Vendor matching إلى `ven_id` محلي.
- Product matching إلى `itm_id` محلي.
- multiple expiry/batch splits لنفس source line.
- إنشاء New Item من Android بعد user confirmation.
- missing/reused Vendor invoice number policy.
- Direct DB insertion عبر versioned Genius profile.
- stock، class، store، Vendor وfinancial reconciliation.
- subscription time limits وrequest/page quotas.
- offline queue قبل OCR وبعد استلام OCR result وفق policy محددة.
- immutable audit trail في Sidecar وSaaS.

## 4. Out of Scope للإصدار الأول

- دعم ERP آخر غير الـ Genius schema المعتمدة.
- autonomous product creation.
- automatic approval لكل OCR fields.
- إضافة أوتعديل Genius schema.
- full cloud copy من catalog أوstock أوtransactions.
- customer/patient data processing.
- sales invoices.
- purchase returns حتى يتم اعتماد profile منفصل لها.
- accounting adjustments خارج المسار الذي تم إثباته من e-plus.
- cross-pharmacy auto-mapping دون human confirmation.

## 5. افتراضات صريحة

- Beconnect contract وofficial API لن يحدثا.
- Genius schema المستهدفة ثابتة، لكن Connector يظل يتحقق من fingerprint قبل كل write deployment.
- الصيدلية تسمح بتثبيت Windows Service وControl UI محليين.
- الصيدلية توفر account محدود للـ DB بدل `sa` أو`db_owner`.
- SQL Server legacy instance قد يحتاج TLS 1.2 patch أوlocal-only network containment.
- هناك فترة Reverse Engineering وGolden Testing قبل أي Live Commit.

## 6. مؤشرات النجاح

- 100% من committed invoices لها reconciliation result.
- صفر duplicate commits في retry وpower-loss testing.
- أقل من 1% من jobs تدخل `CommitUnknown`، وصفر منها يُعاد تلقائيًا.
- 100% من expirable Posting Lines لها expiry أوexplicit supervised override.
- 100% من New Items لها actor وevidence وduplicate-check audit.
- product match precision أعلى من recall؛ unresolved أفضل من wrong match.
- كل privileged admin access ينتج immutable audit event.

## 7. Failure Budget

لا يوجد failure budget مقبول للـ silent data corruption. يمكن قبول OCR failure أوmanual review، لكن لا يمكن قبول:

- stock delta غير متطابق مع الفاتورة.
- financial document مفقود عندما يكون واجبًا.
- duplicate Purchase Invoice.
- product strength/form mismatch تم اعتماده تلقائيًا.
- retry بعد حالة Commit غير معروفة.
