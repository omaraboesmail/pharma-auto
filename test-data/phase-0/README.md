# Phase 0 Synthetic Dataset

Status: **Approved synthetic fixture set**

This dataset contains no copied invoice, pharmacy, supplier, product, person, phone number, tax identifier or commercial transaction. Every image and expected value is generated from scratch. The repository owner's 2026-08-21 instruction approved synthetic fixtures instead of treating the unclassified WhatsApp invoice images on another branch as test evidence.

## Contents

- `manifest.v1.json`: classification, provenance, page hashes, document hashes and coverage.
- `sources/`: four 1600×1200 PNG pages covering English, Arabic and mixed-script invoices, including one two-page document and a deliberately missing invoice number.
- `expected/`: schema-valid OCR evidence for each logical document.
- `generate-fixtures.ps1`: deterministic-content generator. Font rasterization can vary by Windows/font version, so regeneration intentionally updates hashes.

The logical document SHA-256 is calculated over UTF-8 lines in page order using `pageNumber:lowercasePageSha256\n`. It detects page changes and reordering without concatenating ambiguous page bytes.

## Safety Rules

- Do not add real or merely blurred invoices to this directory.
- A new fixture must declare `SYNTHETIC`, `containsRealData: false`, coverage and an expected OCR result.
- Realistic values must remain visibly synthetic and must not reuse actual customer identifiers.
- Validate with `pnpm contracts:validate` after generation.

Regenerate from the repository root:

```powershell
& .\test-data\phase-0\generate-fixtures.ps1
```
