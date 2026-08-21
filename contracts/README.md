# Contracts

المصدر الوحيد للـ versioned API،OCR schema،domain/evidence payloads وevents.

- `openapi/`: Android↔Connector،Connector↔SaaS وAdmin↔SaaS specifications.
- `schemas/`: OCR result،Invoice Revision،Posting Line،commercial values وPhase 0 evidence schemas.
- `events/`: audit/job event definitions عند الحاجة.
- `examples/`: synthetic valid/invalid examples فقط.

لا تحتوي contracts على SQL credentials أوGenius table-specific write commands.

Phase 0 initializes the evidence boundary. Phase 1 adds pairing،entitlement،job،local-candidate and read-only review-package contracts plus complete Android↔Connector and Connector↔SaaS OpenAPI paths. Catalog Creation،Commit and Reconciliation write-era schemas are added only when their resource/state models reach the corresponding delivery phase; their absence is not filled with guessed DTOs.
