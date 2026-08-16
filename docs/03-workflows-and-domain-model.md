# Workflows and Domain Model

## 1. Core Aggregates

### Invoice Job

يمثل دورة حياة المستند كاملة:

- `job_id`
- tenant،pharmacy،connector وdevice identity
- document/page hashes
- state
- current immutable revision
- subscription reservation
- commit journal
- final `pth_id`
- reconciliation result

### Invoice Revision

كل user edit ينشئ revision جديدة بدل تعديل history:

- Vendor selection.
- invoice number/date.
- financial summary.
- ordered Source Lines.
- Posting Lines الناتجة عن splits.
- user،timestamp وreason.

### Source Line

يمثل ترتيب الفاتورة المصورة:

- `source_line_index`
- OCR text/evidence.
- bounding box/page.
- raw quantity/price/expiry hints.
- selected local item أوunresolved state.

### Posting Line

يمثل row واحدًا في `pur_trans_d`:

- immutable split identity.
- parent Source Line.
- local `itm_id`.
- `split_index`.
- final `posting_sequence`.
- quantity،bonus،expiry،batch،unit،price،discount وtax.
- resolved `c_id` بعد preflight/commit.

### Local Item Mapping

- tenant/database fingerprint.
- raw Vendor description.
- Vendor context.
- structured Pharma attributes.
- selected `itm_id`.
- rejected candidates.
- evidence،actor وmapping version.
- raw Genius label + raw bytes hash + name-quality flags عند استخدام الاسم كدليل.

### Catalog Creation Command

عملية منفصلة لإنشاء Item:

- proposed fields.
- duplicate candidates.
- approving actor.
- generated `itm_id` و`itm_code`.
- `Item_Vendor` result.
- status مستقل عن Invoice Commit.

## 2. Capture to Review

1. Android ينشئ local draft ويرتب الصفحات.
2. Connector يتحقق من الملف ويحسب hashes.
3. SaaS يحجز quota باستخدام `job_id` كـ idempotency boundary.
4. Gemini يرجع structured fields وevidence locations.
5. SaaS يتحقق من schema والحسابات الأساسية.
6. Connector ينفذ Vendor/Product retrieval محليًا.
6.1 SaaS يمكنه إرجاع Canonical candidates عبر hybrid PostgreSQL + pgvector search بعد hard filtering.
7. Android يعرض كل field مع source crop.
8. المستخدم يحل ambiguous mappings ويضيف expiry splits.
9. النظام يعيد حساب totals وposting order.
10. confirmation تقفل revision؛ أي تعديل لاحق يحتاج confirmation جديدة.

## 3. Expiry Split Rules

- `source_line_index` لا يتغير عند إضافة split.
- `split_index` يبدأ من 1 داخل Source Line.
- `posting_sequence` يعاد اشتقاقها بترتيب Source Line ثم split.
- نفس expiry مسموحة مرتين إذا اختلف field اقتصادي أوbatch.
- duplicate identical splits ينتجان warning مع خيار merge.
- مجموع split quantities/bonus يجب أن يطابق invoice evidence أوexplicit correction.
- expirable Item دون expiry يُمنع إلا بتصريح Supervisor وسياسة موثقة.

## 4. New Item Workflow

1. user يختار `Create New Item` بعد فشل البحث.
2. Connector يعيد duplicate search باستخدام identifiers وstructured attributes.
3. Catalog Manager يملأ required units،conversions،prices وflags.
4. system يعرض impact summary قبل approval.
5. Genius Adapter ينفذ Catalog Creation Command.
6. Connector يقرأ Item من Genius مجددًا؛ لا يثق فقط بنتيجة insert.
7. Local projection تتحدث ويصبح line resolved.
8. إذا فشل Invoice لاحقًا يبقى Item مع audit state `CreatedButNotYetPurchased`.

## 5. Duplicate and Numbering Workflow

يحتفظ النظام بثلاث قيم:

- source invoice number كما ظهر.
- `ven_bill_no` الذي سيدخل Genius.
- `pth_id` الداخلي.

قواعد duplicate fingerprint:

- database fingerprint.
- `ven_id`.
- normalized source invoice number.
- invoice date.
- total/currency.
- document hash.
- normalized line fingerprint.

القرار:

- نفس fingerprint: block/reopen existing job.
- نفس Vendor number لكن content مختلف: Supervisor warning ثم fallback إلى generated `pth_id`.
- missing number: fallback إلى generated `pth_id`.
- source number unique لنفس Vendor: retain it.

## 6. Commit Workflow

1. freeze confirmed revision.
2. acquire Connector commit lease.
3. run DB fingerprint and preflight.
4. perform final duplicate check داخل transaction.
5. insert header and capture actual identity.
6. finalize `ven_bill_no` policy.
7. resolve/create item classes and write Posting Lines بالترتيب.
8. apply verified stock وfinancial write-set.
9. commit transaction.
10. run independent reconciliation.
11. publish final state إلى Android وSaaS metadata.

## 7. Recovery Semantics

| نقطة الفشل | النتيجة |
|---|---|
| قبل DB transaction | safe retry بنفس `job_id` |
| داخل transaction قبل commit confirmed | rollback أو`CommitUnknown` حسب connection evidence |
| بعد commit وقبل response | `CommitUnknown` ثم read-only investigation |
| reconciliation mismatch | `CommittedNeedsReview`؛ لا retry |
| New Item نجح والفاتورة فشلت | Item يبقى؛ invoice يمكن تصحيحه وإعادة محاولة جديدة مسيطر عليها |

## 8. Matching Order

1. Barcode/GTIN exact.
2. Vendor item code exact.
3. `itm_code` / `itm_code2` / `itm_int_code` exact.
4. prior confirmed tenant+Vendor mapping.
5. exact normalized local name.
6. raw reversed-byte label كـ weak evidence فقط إذا لم يحمل corruption flags مانعة.
7. active ingredient + strength +form +pack constraints.
8. manufacturer/price compatibility.
9. vector similarity shortlist.

الـ score يعرض reason categories مثل `EXACT_IDENTIFIER` أو`PREVIOUS_CONFIRMED_MAPPING`، ولا يعرض percentage مزيفة كأنها calibrated probability.

## 9. Canonical Pharma Vector Retrieval

الـ Canonical record يحتوي fields مستقلة: brand،active ingredient،strength/value unit،dosage form،route،pack count/unit،manufacturer،regulatory identifiers وnormalized aliases.

Embedding لا يستبدل هذه الحقول. يتم توليده من canonical representation versioned ويحفظ معه:

- embedding model/version.
- source text hash.
- locale.
- generated timestamp.
- active index version.

البحث يكون hybrid:

- exact identifiers أولًا.
- structured SQL filters ثانيًا.
- lexical/trigram retrieval.
- pgvector cosine candidate retrieval.
- deterministic reranking وhard mismatch removal.

Confirmed cross-pharmacy mappings يمكن أن ترفع candidate rank بعد anonymization وminimum-support threshold، لكنها لا تنتج auto-confirm خارج tenant.

## 10. Raw Name Integrity

Catalog Projection تفصل بين:

- raw stored bytes.
- raw label بعد byte reversal وفك code page.
- display direction metadata.
- name-quality flags.
- user-confirmed أوCanonical display name.

Byte reversal لا يعيد الحروف المفقودة ولا يصلح ترتيبًا فاسدًا داخل البيانات. لا تُطبّق قواعد من نوع “انقل آخر حرفين إلى البداية”. إذا كان الاسم مقطوعًا أوmixed بصورة غير قابلة للتحقق،يرجع matching إلى identifiers وCanonical Catalog أوmanual confirmation.
