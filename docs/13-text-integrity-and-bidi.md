# Text Integrity and BiDi Policy

## 1. Problem Statement

Genius تخزن Item names في `varbinary` مع byte order معكوس. فك هذا الـ reversal لا يضمن اسمًا صحيحًا؛ قد تكون الحروف ناقصة،المسافات تالفة أوArabic/English fields متطابقة.

العينة المثبتة:

- `itm_id = 60495`
- `itm_code = 60495`
- `itm_int_code = NULL`
- Arabic bytes = 23
- English bytes = 23
- الحقلان متطابقان byte-for-byte
- raw label بعد الفك ما زال ناقصًا؛ الحرف المطلوب غير موجود في source bytes

لا SQL conversion أوBiDi mark يستطيع استرجاع byte غير موجود.

## 2. Data Layers

| Layer | المعنى | مسموح للـ identity؟ |
|---|---|---|
| Stored bytes | القيمة الأصلية في Genius | hash/audit فقط |
| Raw label | byte reversal + code-page decode فقط | weak evidence فقط |
| Display label | raw أوcanonical label مع BiDi-safe rendering | لا |
| Canonical label | General Product Catalog structured name | candidate evidence |
| Confirmed local identity | `database fingerprint + itm_id` | نعم |

## 3. Matching Policy

الأولوية:

1. Barcode/GTIN.
2. Vendor item code.
3. `itm_code`, `itm_code2`, `itm_int_code`.
4. confirmed pharmacy/Vendor mapping.
5. structured Canonical Pharma fields.
6. raw local label lexical/vector evidence،بشرط عدم وجود corruption flag مانع.

Raw label لا تصبح key ولا final truth.

## 4. Quality Flags

- `UNVERIFIED`
- `LANGUAGE_FIELDS_IDENTICAL`
- `EMPTY_OR_BLANK`
- `MALFORMED_BIDI`
- `TRUNCATED_OR_CORRUPT`
- `CANONICAL_OVERLAY_AVAILABLE`
- `MANUALLY_CONFIRMED`

Flags وصفية ولا تحاول إصلاح القيمة.

## 5. UI Rendering Policy

### Android

- base direction مشتقة من content لكل label dynamic.
- English brand/code/strength segments المهيكلة تُعزل Unicode-wise داخل RTL context.
- raw corrupted label يظهر كـ secondary evidence مع warning،بينما canonical/manual label يصبح primary إن وجد.
- copy action ينسخ النص الخام أوcanonical حسب label واضح؛لا ينسخ invisible repair characters دون بيان.

### Web/Admin

- dynamic mixed labels تستخدم `dir="auto"` أوUnicode isolation مثل `bdi`.
- `unicode-bidi: plaintext` مناسب للـ raw standalone label عندما لا توجد segments مهيكلة.
- لا يفرض RTL على product code/barcode.
- RTL mark مثل U+200F يمكن أن يكون diagnostic display hint فقط،وليس persisted data.

## 6. Prohibited Heuristics

- نقل آخر حرف أوحرفين إلى بداية الاسم.
- دمج Arabic وEnglish fields لمجرد تطابقهما أواختلافهما.
- اختراع حرف مفقود من context دون user/canonical evidence.
- حفظ invisible BiDi marks داخل Genius names.
- استخدام raw-name vector similarity لتجاوز identifier أوstrength/form/pack mismatch.

## 7. Manual Resolution

عند corruption:

1. اعرض identifiers وVendor context وraw evidence.
2. ابحث في Canonical General Products Catalog.
3. اطلب user confirmation للـ local `itm_id`.
4. احفظ confirmed display overlay/mapping في Sidecar.
5. لا تعدّل Genius name تلقائيًا.

## 8. Acceptance Tests

- sample 60495 لا يتحول تلقائيًا إلى اسم كامل.
- Arabic/English identical fields تنتج flag.
- mixed Arabic + Latin + numbers + dosage units تُعرض بلا visual reordering destructive.
- copied raw text يطابق raw decoded text byte-for-character وفق code page.
- canonical overlay لا يغير `itm_id` ولا source bytes.
- search يمكنه العثور بالـ identifiers حتى عندما raw name غير صالح.
