# Genius DB Adapter Specification

## 1. Status and Boundary

اسم profile: `EPLUS_GENIUS_DB539_PROFILE_1`.

هذا Adapter unsupported من Beconnect ويعتمد على reverse engineering للـ schema والسلوك. ثبات الـ schema لا يثبت ثبات business rules؛ لذلك لا يصبح Adapter معتمدًا إلا بعد Golden Scenario certification.

كل معرفة بأسماء جداول وأعمدة Genius تبقى داخل:

- Catalog Reader.
- Master Item Writer.
- Purchase Commit Writer.
- Reconciliation Reader.
- DB Fingerprint Validator.

لا يجوز للـ Android أوSaaS أوdomain layer إرسال SQL concepts مثل `c_id` كقرار خارجي.

## 2. Verified Database Facts

من الـ backup المستعاد:

- 218 user tables.
- 174 primary keys.
- 232 indexes.
- صفر Foreign Keys.
- 6 triggers؛ بعضها row-at-a-time وغير set-safe.
- five of those triggers attach to write-critical purchase dependencies: `Item_Class_Store` (`close_stock`, `watch_stock`), `F_Transaction_Header` (`delete_duplicate_records`) and `Vendor` (`VUPDATEDATE`, `watch_vendor_credit_chnge`).
- `close_stock` can create a whole-stock `ICS_Month_Close` snapshot and update `Sys_setting.last_month_close_dt`; `delete_duplicate_records` can archive then delete matching financial rows and can create its archive table when absent. These are mandatory fingerprint،preflight and Golden surfaces،not incidental trigger noise.
- لا توجد Purchase Invoice Stored Procedure رسمية.
- `Item_Catalog`: 67,377 row.
- `Item_Vendor`: 129,006 row.
- `Vendor`: 209 row.
- `pur_trans_h`: 18,438 row، بينما أعلى `pth_id` هو 18,453.
- `pur_trans_d`: 163,729 row.
- `Item_Class`: 75,276 row؛6,133 Items have multiple positive-quantity classes and 4,734 have multiple class selling prices.
- `pth_id` و`itm_id` هما `decimal(18,0) IDENTITY`.
- `ptd_id` ليس identity ويُستخدم كتسلسل 1..N داخل الفاتورة.
- `no_of_items` يطابق detail row count في الحالات المختبرة، لا distinct Items.
- حقول أسماء Items ليست encrypted؛ byte reversal يكشف raw text فقط،وقد يكون النص نفسه تالفًا أوtruncated أوmixed بصورة غير قابلة للاستعادة.
- العينة `itm_id = 60495` تحتوي 23 byte في كل حقل،والـ Arabic/English varbinary values متطابقة byte-for-byte؛ الحرف المفقود غير موجود في الـ bytes أصلًا.

هذه facts تصف الـ backup فقط. Production Connector يعيد التحقق من fingerprint وcritical invariants.

## 3. DB Fingerprint

The normative Phase 0 format،canonicalization،critical-object inventory and fail-closed comparison policy are defined in [DB Fingerprint Definition](16-db-fingerprint-definition.md). The summary below remains the adapter-facing boundary.

يتكون fingerprint من:

- database version وcompatibility level.
- collation.
- table/column names،types،nullability وidentity flags للجداول الحرجة.
- primary keys وindexes الحرجة.
- trigger names وتعريفاتها hashes.
- view/function hashes المستخدمة في reconciliation.
- profile version.

أي mismatch في write-critical object يوقف Commit ويترك read-only diagnostics متاحة.

## 4. Catalog Read Model

### Item Projection

- `itm_id`
- `itm_code`, `itm_code2`, `itm_int_code`
- raw Arabic/English labels recovered من reversed `varbinary`
- raw bytes hash،field equality indicator وname-quality flags
- medicine/expiry/active/stop flags
- unit IDs وconversion factors
- default pharmacy/sell/tax prices
- company،scientific attributes،tax codes عند توفرها

Plaintext name columns لا تستخدم كمصدر رئيسي لأن البيانات الحالية تتركها فارغة. والـ reversed labels لا تصبح مصدر identity بديلًا؛ هي untrusted display/search evidence.

### Vendor Projection

- `ven_id`
- codes/names/contact identifiers المطلوبة للمطابقة
- active/credit-related fields اللازمة للعرض فقط

### Item Vendor Projection

- `itm_ven_id`
- `itm_id`, `ven_id`
- Vendor item code
- price/discount/min-expiry attributes

`Item_Vendor` يحتوي duplicates وblank codes، لذلك uniqueness لا تُفترض دون validation.

## 5. Master Item Creation

Master Item command منفصلة عن invoice transaction:

1. duplicate check داخل transaction.
2. insert `Item_Catalog` والحصول على `itm_id` الفعلي.
3. إذا لم يوجد user code، ضبط `itm_code` إلى identity المولدة طبقًا للنمط الحالي.
4. كتابة Arabic/English names بصيغة reversed bytes في الحقول المستخدمة فعليًا.
5. عدم ملء language field مفقود بنسخة من field آخر.
6. كتابة units/conversions وexpiry/medicine/active flags والأسعار المعتمدة.
7. إنشاء `Item_Vendor` للـ current Vendor إذا لم توجد علاقة مكافئة.
8. commit.
9. read-back verification من Genius.
10. refresh local projection.

لا يتم إنشاء `Item_Class` لمجرد وجود Master Item. أول purchased lot ينشئ class عند الحاجة.

### Name Recovery Contract

- `REVERSE(CONVERT(varchar(...), field))` يفك byte reversal فقط.
- أي RTL mark مثل `NCHAR(8207)` presentation hint،وليس data repair ولا يُحفظ كجزء من الاسم.
- Adapter لا يخمن الحروف المفقودة ولا يعيد ترتيب fragments.
- field equality بين Arabic/English تُسجل كـ `LANGUAGE_FIELDS_IDENTICAL`.
- malformed أوtruncated result تُسجل كـ `RAW_NAME_UNTRUSTED` وتمنع name-only auto-match.
- manual/Canonical correction تحفظ في Sidecar كـ overlay؛ لا تغيّر Genius master تلقائيًا.

## 6. Purchase Header Rules

الجداول الأساسية تبدأ من `pur_trans_h`، لكن نجاح header لا يعني نجاح Purchase Transaction.

قواعد مهمة:

- `pth_id` من identity الفعلية فقط.
- `ven_bill_no` يحتفظ برقم Vendor عندما يكون صالحًا وفريدًا داخل Vendor.
- fallback يستخدم string representation للـ `pth_id` بعد توليده.
- `no_of_items` هو final Posting Line count.
- totals،discounts،expenses،paid amount وsource tax تأتي من confirmed revision.
- `sec_insert_*` و`sec_update_*` تتبع integration service identity وفق behavior المعتمد.
- correlation marker قصير يمكن وضعه في `pth_notice` فقط بعد إثبات أنه لا يكسر business usage أوlength؛ وإلا يبقى correlation في Sidecar.

## 7. Purchase Detail Rules

كل Posting Line يكتب row واحدًا في `pur_trans_d`:

- `ptd_id` = final sequence 1..N.
- `pth_id` = generated header identity.
- `itm_id` = resolved local item.
- `c_id` = resolved/created lot class.
- `exp_date`, `qnty`, `bonus`, `ptd_batch` من confirmed split.
- unit snapshots وconversion factors تُحفظ كما فعل e-plus reference scenario.
- purchase/sell/cost/tax/discount fields تُشتق من confirmed commercial values بالـ formulas المثبتة، لا بالـ OCR raw total وحده.
- read-only evidence يثبت أن `pur_trans_d.itm_sell` detail snapshot وأن `Item_Class.sell_price` class-specific value؛لكن exact write-set وstore/catalog side effects تحتاج Golden certification.
- لا يغيّر Adapter selling price عالميًا كـ side effect غير معلن؛أي master impact يحتاج permission وconfirmed intent وread-back reconciliation.
- profile يجب أن تطبق selling price tax-inclusive per `BOX` على new stock فقط وتحافظ على existing stock price؛عدم القدرة على هذا العزل ينتج `CommitRejected`.
- Discount 1% يطبق في legacy Vendor formula عبر `pur_trans_d.itm_extra_dis` على purchase price path،ثم توجد مرحلة header discount ثانية. Pharma Auto يحتفظ بالخصمين كنسب على كل Posting Line؛translation وrounding للمرحلة الثانية تحتاج Golden Scenario مطابقًا لـ e-plus.

Invoice `18452` تثبت أن نفس `itm_id` يمكن أن يظهر عدة مرات، ونفس expiry يمكن أن يظهر بسعر مختلف مع نفس `c_id`.

## 8. Item Class Resolution

قبل كتابة detail:

1. normalize expiry إلى policy e-plus الفعلية، غالبًا month-level للبيانات الحالية لكن يجب إثباتها.
2. match existing class باستخدام item،expiry،batch/serial وstock context.
3. إذا تغيّر selling price،لا يعاد استخدام class يحتوي existing quantity إلا إذا أثبت scenario أن القديم لن يتغير؛الـ profile تعزل receipt الجديدة في class مستقل.
4. إذا لم يوجد class صالح، lock item/class key ثم allocate `c_id` بطريقة concurrency-safe.
5. create `Item_Class` و`Item_Class_Store` rows المطلوبة.
6. apply quantity delta row-at-a-time.

لا يستخدم `MAX(c_id)+1` دون lock. Live data يثبت أن نفس class قد يعاد استخدامه عبر selling prices مختلفة،كما يثبت وجود multiple non-expiry classes بأسعار مختلفة؛لذلك class-split write-set يحتاج Golden certification ولا يستنتج من schema فقط.

## 9. Stock and Trigger Constraints

Triggers على `Item_Class_Store` تقرأ scalar values من `inserted/deleted`، ولذلك multi-row update غير آمن.

الـ Adapter:

- لا يعطل triggers.
- لا يعمل bulk update على هذه الجداول.
- يستخدم consistent lock order.
- يختبر quantity before/after.
- يميز overall class quantity عنper-store quantity.
- blocks stock writes when the named month-close invariant is pending؛it never lets a receipt become the accidental `close_stock` initiator.
- reconciles `watch_qty_chng`, `ICS_Month_Close` and `Sys_setting` even when the expected business action appears limited to one class/store row.

## 10. Financial Side Effects

الـ DB تحتوي على `F_Auto_Doc_h`, `F_Auto_Doc_d`, `F_Transaction_Header` و`F_Transaction_Bills`. وجودها وشكلها يعتمد على invoice payment scenario.

يجب اعتماد write-set منفصل لكل نوع:

- Credit purchase.
- Cash purchase.
- Partially paid purchase.
- Header discount.
- Other expenses.
- Source tax.
- Bonus.
- Item return/purchase return في profile لاحق.

Vendor balance formula يجب أن يطابق behavior الظاهر في e-plus views والـ reference transactions. لا يتم استخدام `total_bill` وحده كقيمة Vendor impact.

The `delete_duplicate_records` trigger on `F_Transaction_Header` is a destructive hidden surface. The fingerprint requires `F_Transaction_Header_SaveDeleteRecords` to exist and hashes the trigger definition. Golden evidence must prove that purchase-generated type/form/notes values cannot unexpectedly archive or delete another row; all-table reconciliation treats any deletion as blocking.

## 11. Transaction and Locking

- transaction واحدة لكل invoice.
- New Item commands قبلها وفي transactions مستقلة.
- final duplicate check داخل transaction.
- consistent lock ordering على ID/financial/stock resources.
- timeout قصير؛ failure في lock acquisition يعيد job إلى queue.
- no blind retry بعد ambiguous connection failure.
- SQL statements على trigger-sensitive tables تكون single-row.

`sp_getapplock` يمكن أن يسلسل Connector مع نفسه لكنه لا يمنع e-plus من الكتابة؛ لذلك لا يعتبر بديلًا عن actual DB locks.

## 12. Reconciliation Contract

Mandatory postconditions:

- header موجود مرة واحدة ويرتبط بالـ Vendor الصحيح.
- `ven_bill_no` يطابق numbering decision.
- detail count يساوي `no_of_items`.
- `ptd_id` متصل 1..N.
- كل detail يطابق confirmed `itm_id`, quantity, bonus, expiry, batch, units،purchase price،discount وselling price snapshots.
- أي selling-price master/class/store impact يطابق old/new value والـ scope المؤكدين.
- `Item_Class` و`Item_Class_Store` موجودان وتأثير الكمية متسق.
- required financial auto-doc موجود وخطوطه متطابقة.
- Vendor balance delta مطابق للسيناريو.
- لا توجد orphan rows أوpartial side effects.

## 13. Prohibited Operations

- `sa`, `db_owner` أوbroad writer credentials.
- direct deletion لrollback.
- disabling triggers أوconstraints.
- adding platform tables إلى Genius.
- treating SQL success كـ business success.
- computing identities بالعدد أوmaximum.
- auto-retrying `CommitUnknown`.
- approving adapter against backup-only assumptions دون Golden e-plus scenarios.
