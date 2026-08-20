# JSON Schemas

توضع versioned schemas للـ OCR result،canonical retrieval،invoice revisions،posting lines وreconciliation. Gemini يدعم subset من JSON Schema،لذلك provider schema قد تكون projection أبسط من domain schema الكاملة.

Product label schema تفصل `rawLabel` و`canonicalLabel` و`labelSource` و`qualityFlags` و`displayDirection`. لا contract يسمح للـ client بإرسال heuristic-corrected label كأنه Genius source value.

Initialized v1 commercial contracts:

- `commercial-values.v1.schema.json`: EGP commercial values،two sequential percentage discounts،and new-stock-only box selling price.
- `posting-line.v1.schema.json`: split identity, editable quantity and expiry, commercial evidence, and unified review-correction audit.
- `invoice-revision.v1.schema.json`: immutable revision envelope and policy snapshot.

Run `pnpm contracts:validate` from the repository root.
