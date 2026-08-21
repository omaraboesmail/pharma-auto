# Testing and Acceptance

## 1. Test Strategy

الاختبارات تنقسم إلى أربع طبقات:

1. deterministic domain tests.
2. component/integration tests.
3. Golden Genius DB tests.
4. fault،security وoperational tests.

عدد unit tests لا يعوض Golden DB evidence.

## 2. Android Acceptance

- capture single/multi-page invoice.
- resume interrupted upload.
- retain page order after rotation/restart.
- add/remove multiple expiry splits.
- same expiry with different price remains separate.
- next Source Line shifts correctly while source identity remains unchanged.
- edit purchase unit price،Discount 1%،Discount 2% وselling unit price لكل Posting Line،بما في ذلك splits لنفس Source Line.
- Discount 1 يخفض purchase unit price،وDiscount 2 يخفض remaining line subtotal دون تغيير stored purchase-price snapshot مرة ثانية.
- selling price يعرض `EGP / box / tax included` ويطبق على new stock فقط؛existing stock price لا يتغير.
- profile لا تستطيع عزل new-stock selling price تمنع Commit وتعرض سببًا قابلًا للتصرف.
- apply-to-all يعرض عدد splits المتأثرة،ثم يسمح بـ per-split override دون تغيير Source Line identity.
- original OCR commercial values تظل قابلة للعرض بعد edits/restart،وكل edit يظهر في revision audit.
- totals تتغير مع purchase price/discount فقط طبقًا للعقد؛selling price لا يغير purchase total.
- unauthorized selling-price master impact يُمنع،والـ authorized change يعرض old/new value والـ scope قبل confirmation.
- create New Item only with permission.
- duplicate candidates displayed before creation.
- totals recalculate after every split/edit.
- immutable confirmed revision؛ edit requires reconfirmation.
- accessibility،Arabic/English layout وlow-end device testing.
- screen reader يقرأ canonical/manual label بوضوح ويُعرّف raw corrupted label كـ unverified عند عرضه.

## 3. OCR and Matching

- Arabic،English ومختلط invoices.
- rotated،faint،multi-page وtable-less formats.
- omitted invoice number.
- Vendor aliases.
- strength/form/pack hard mismatches.
- barcode/vendor code exact priority.
- duplicate names لا تنتج auto-match.
- regression fixture لـ `itm_id = 60495`: Arabic/English raw fields متطابقة،والحرف المفقود لا يُخترع.
- لا heuristic تنقل fragments أوتغير raw label bytes.
- `dir="auto"`/BiDi isolation تعرض Arabic،English وmixed labels دون تغيير copyable text.
- long mixed-script labels،numbers،parentheses وdosage units تُختبر عند 200% text scaling.
- OCR schema rejects extra/unexpected fields.
- field evidence points إلى الصفحة والمنطقة الصحيحة.
- provider retry لا يستهلك quota ثانية.
- pgvector candidate recall يقاس بعد hard filters،مع comparison ضد exact baseline.
- embedding model upgrade لا يخلط index versions أوconfirmed mappings.

## 4. Golden DB Scenario Matrix

Every capture follows the versioned [Golden Scenario Capture Procedure](17-golden-scenario-capture-procedure.md); a row in this matrix is not complete without its validated evidence bundle and required approvals.

كل scenario يُدخل يدويًا في e-plus على Clone نظيفة، ثم تؤخذ before/after snapshots. Adapter output يجب أن يطابق business-equivalent state.

| Scenario | إلزامي قبل Live |
|---|:---:|
| Credit invoice،Item واحد بلا expiry | Yes |
| Item واحد بخمس expiries | Yes |
| نفس expiry بسعرين | Yes |
| نفس Source Line بقيم purchase/discount/selling مختلفة بين splits | Yes |
| Existing Item selling-price change والـ scope/read-back الخاص به | Yes |
| Existing class وnew class | Yes |
| New Item ثم أول purchase | Yes |
| Box/Strip/Tablet conversions | Yes |
| Fractional quantity | Yes |
| Bonus | Yes |
| Line/header discounts | Yes |
| Other expenses/source tax | Yes |
| Cash وpartial payment | Yes |
| Missing Vendor number fallback | Yes |
| Reused Vendor number لفاتورة مختلفة | Yes |
| Actual duplicate block | Yes |
| `Item_Class_Store` write with no pending month close،and with a pending month close safely blocked | Yes |
| `F_Transaction_Header` legacy duplicate-delete trigger guard and archive-table invariant | Yes |
| Stock/Vendor trigger audit rows and program/host attribution | Yes |
| Return scenarios | No؛ profile لاحق قبل تفعيل feature |

## 5. DB Integrity Tests

- no orphan details.
- detail count/order match.
- Item/Class/Store pair existence.
- expected stock delta.
- financial auto-doc expected shape.
- Vendor balance delta.
- trigger audit/history effects.
- no unrelated row changes.
- transaction rollback leaves no partial rows.
- identity gaps tolerated دون إعادة استخدام.

## 6. Concurrency and Fault Injection

- simultaneous e-plus manual purchase.
- two Connector jobs queued.
- lock timeout قبل write.
- service crash قبل transaction.
- process kill بين header/details.
- SQL connection loss أثناء commit acknowledgement.
- power loss بعد commit وقبل Sidecar update.
- SaaS retry during Gemini timeout.
- duplicate Android request.
- full disk وread-only filesystem.
- expired/revoked certificate.

Expected result لا يحتوي أي blind retry أوduplicate commit.

## 7. Security Tests

- tenant isolation across every SaaS query.
- JWT issuer/audience/expiry/AAL validation.
- device/Connector revocation.
- file type confusion وzip/PDF bombs.
- path traversal.
- malicious OCR text/prompt injection treated as data.
- secret scanning وAPK inspection.
- SQL account cannot alter schema أوread unrelated sensitive tables.
- admin break-glass expires ويُدقق.

## 8. Performance Tests

- 100,000 Item local search dataset.
- invoice 1،20،100 و500 Posting Lines.
- 50 concurrent OCR jobs عبر tenants.
- quota contention لنفس tenant.
- Connector startup/catalog rebuild.
- Sidecar recovery after unclean shutdown.

لا يتم parallelize DB commits داخل Genius واحدة لتحسين benchmark شكلي.

## 9. Production Entry Gates

- DB fingerprint ثابت ومعتمد.
- كل mandatory Golden Scenario pass.
- zero duplicate commit في fault suite.
- 100% reconciliation coverage.
- least-privilege SQL account مثبت.
- TLS/legacy risk موثق.
- backup/restore drill ناجح.
- runbooks مكتملة.
- staged pilot مع صيدلية واحدة وsupervised commits.

## 10. Pilot Exit Criteria

- 500 invoice على الأقل أوفترة تشغيل تمثل الأنماط الأساسية، أيهما أشد.
- لا silent stock/financial mismatch.
- كل reconciliation failure لها root cause وإجراء.
- mapping correction rate مقاسة حسب Vendor وdocument template.
- support load وتكلفة OCR ضمن business model.
- formal go/no-go review قبل التوسع.
