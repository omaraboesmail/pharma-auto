# Contract Examples

يحتوي synthetic examples بلا بيانات صيدلية حقيقية. Phase-specific examples تُضاف مع contracts الناضجة ولا تُستبدل ببيانات production منزوعة جزئيًا.

`invoice-revision.v1.example.json` covers EGP،two sequential percentage discounts and a tax-inclusive per-box selling price for new stock only.

`review-package.v1.example.json` covers the Phase 1 Connector-owned evidence/candidate envelope. Its local Vendor and Item references remain unselected and every candidate requires manual confirmation.

`golden-scenario-manifest.v1.example.json` is explicitly a `SYNTHETIC_TEMPLATE`; it is not evidence that a Golden scenario ran or passed. OCR examples and source pages live under `test-data/phase-0/` so their hashes and provenance remain together.
