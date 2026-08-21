# JSON Schemas

توضع versioned schemas للـ OCR result،domain revisions وevidence manifests. Gemini يدعم subset من JSON Schema،لذلك provider request schema قد تكون projection أبسط من domain OCR result الكاملة،لكن النتيجة المقبولة داخل Pharma Auto يجب أن تمر بالـ canonical schema.

Product label schema تفصل `rawLabel` و`canonicalLabel` و`labelSource` و`qualityFlags` و`displayDirection`. لا contract يسمح للـ client بإرسال heuristic-corrected label كأنه Genius source value.

Initialized Phase 0 contracts:

- `commercial-values.v1.schema.json`: EGP commercial values،two sequential percentage discounts،and new-stock-only box selling price.
- `posting-line.v1.schema.json`: split identity, editable quantity and expiry, commercial evidence, and unified review-correction audit.
- `invoice-revision.v1.schema.json`: immutable revision envelope and policy snapshot.
- `ocr-result.v1.schema.json`: canonical Gemini-backed field/line evidence with page bounds،warnings and no Genius identity authority.
- `dataset-manifest.v1.schema.json`: synthetic-only dataset provenance،coverage and cryptographic page/document identity.
- `db-fingerprint-definition.v1.schema.json`: fail-closed Genius metadata profile definition.
- `golden-scenario-manifest.v1.schema.json`: all-table before/after evidence and approval record.

Phase 1 adds `pairing-session`،`entitlement`،`invoice-job`،`catalog-candidate` and `review-package` schemas. The review package fixes `geniusWritePerformed` to false and separates immutable OCR/candidate evidence from operator-selected opaque local references.

Run `pnpm contracts:validate` from the repository root.
