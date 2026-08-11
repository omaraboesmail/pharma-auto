# Contracts

المصدر الوحيد للـ versioned API،OCR schema،domain payloads وevents.

- `openapi/`: Android↔Connector،Connector↔SaaS وAdmin↔SaaS specifications.
- `schemas/`: OCR،Invoice Revision،Posting Line،Catalog Creation وReconciliation JSON Schemas.
- `events/`: audit/job event definitions عند الحاجة.
- `examples/`: synthetic valid/invalid examples فقط.

لا تحتوي contracts على SQL credentials أوGenius table-specific write commands.
