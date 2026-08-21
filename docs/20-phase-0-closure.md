# Phase 0 Closure

- Status: **Complete**
- Closure date: 2026-08-21
- Scope: Phase 0 — Evidence and Contracts only

Phase 0 completion establishes safe evidence, contracts and investigation ownership. It does not certify, enable or imply any Genius write capability.

## 1. Approval Record

- The product/architecture baseline is accepted through the **Initialization approved** gate in [Initialization Decisions](14-initialization-decisions.md), and every ADR in the [ADR index](decisions/README.md) is `Accepted` or `Accepted with ... gate`.
- On 2026-08-21, the repository owner explicitly selected fully synthetic fixtures and role-based investigation owners for Phase 0.
- New Phase 0 specifications declare their status at the top of each document. A gated ADR remains accepted as a decision while its later production evidence gate remains closed.

## 2. Deliverable Evidence

| Roadmap deliverable | Authoritative evidence | Completion result |
|---|---|---|
| Approved system docs and ADRs | `docs/00`–`15`, initialization approval and `docs/decisions/README.md` | Complete; statuses are recorded and gated decisions do not enable writes. |
| Sanitized test dataset | `test-data/phase-0/manifest.v1.json`, generated PNG pages, expected OCR results and generator | Complete; source is generated from scratch, owner-approved as synthetic, hashed and machine-validated. |
| DB fingerprint definition | [DB Fingerprint Definition](16-db-fingerprint-definition.md), machine definition and JSON Schema | Complete as `DBFP-1`; fail-closed behavior and critical objects are explicit. No database is write-certified. |
| Golden Scenario capture procedure | [Golden Scenario Capture Procedure](17-golden-scenario-capture-procedure.md) and versioned evidence manifest | Complete; all-table before/after capture, redaction, approvals and decision rules are defined. |
| Versioned domain/OCR contracts | `commercial-values`, `posting-line`, `invoice-revision` and `ocr-result` v1 schemas plus examples | Complete for Phase 0; OCR cannot carry final Genius identities or SQL authority. |
| Threat model | [Threat Model](19-threat-model.md) | Complete; assets, actors, boundaries, threats, controls, verification and owners are recorded. |

## 3. Exit Gate Evidence

The [Write Assumptions and Side-Effect Owners](18-write-assumptions-and-side-effect-owners.md) register assigns every currently known Genius write uncertainty a `WA-*` identifier, evidence requirement, owner and capability gate. Its critical-object matrix assigns role-based investigation ownership to each known header, detail, catalog, class/store, trigger, Vendor and financial surface.

An unlisted dependency or changed table automatically becomes an owned blocking investigation and makes a Golden result `INCONCLUSIVE`. This rule prevents a newly discovered side effect from becoming an implicit write assumption.

## 4. Repository Data Decision

The five unclassified WhatsApp-named invoice images formerly present on `dev` are deleted in the Phase 0 completion branch and `/invoice_examp/` is ignored. They are not Phase 0 fixtures or evidence. The only approved committed invoice-like sources are the generated files under `test-data/phase-0/`.

Deletion from the current tree does not erase prior Git history. Any decision to rewrite shared history requires a separate explicit repository-owner authorization after confirming whether the old images contain sensitive business data.

## 5. Verification Contract

Phase 0 remains complete only while all of these hold:

```powershell
pnpm contracts:validate
pnpm phase0:validate
git diff --check
git status --short
```

The contract validator must cover every `*.schema.json`, validate all three OCR ground-truth results, verify PNG signatures/dimensions/page hashes/document hashes, validate the fingerprint and Golden manifests, reject unsafe negative cases and confirm the read-only OpenAPI paths.

## 6. Explicit Non-Claims

- No Golden Scenario has run or passed merely because its procedure exists.
- `EPLUS_GENIUS_DB539_PROFILE_1` remains write-disabled and not production-certified.
- No restored backup observation is production proof.
- Phase 1 OCR/matching accuracy, installer/service operation and usable end-to-end workflow are not claimed.
- Pilot and production gates remain open work.
