# Phase 1 Read-Only Vertical Slice Closure

Status: **Complete for the local, non-billable read-only acceptance scope on 2026-08-21.**

This closure does not authorize a Genius write, a production deployment, or a claim about live Gemini accuracy. Phase 2 and later write gates remain unchanged.

## 1. Roadmap Evidence

| Phase 1 deliverable | Implemented evidence | Result |
|---|---|---|
| Connector installer/service/control UI | `local-connector/installer/`, Windows Service host, WPF pairing/catalog/job/device UI, signed-manifest enforcement, DPAPI secrets, service/private-key ACLs, Private/Domain LocalSubnet firewall rule and production mTLS certificate gate | Complete |
| Android pairing/upload | One-time QR claim, Android Keystore P-256 identity, pinned Connector certificate, short challenges/tokens, CameraX capture, JPEG/PNG/PDF validation, Room drafts and resumable WorkManager chunk upload | Complete |
| Local catalog extraction and text integrity | SELECT-only `SqlGeniusCatalogReader`, opaque local identities, reversed CP1256 decoding retained as untrusted raw text, source-byte hashes, integrity flags and content-derived display direction | Complete |
| SaaS tenant/subscription/quota | Tenant-scoped Connector authentication, forced PostgreSQL RLS, active entitlement, ES256 signature, Connector-side signature verification and atomic idempotent page reservation/settlement | Complete |
| Gemini structured OCR | Production Interactions provider with strict structured output and post-response validation; Development uses only deterministic fixtures and refuses fixture mode outside Development | Implemented; no paid call made |
| Vendor/Product matching | Local identifier/FTS candidates, full-catalog manual search, structured strength/form hard mismatches, canonical exact/lexical candidates and versioned 768-dimension semantic retrieval with lexical fallback | Complete |
| Android review | Vendor/Item manual confirmation, hard-mismatch blocking, expiry/batch splits, per-split purchase price and two ordered percentage discounts, tax-inclusive selling price per BOX, new-stock-only policy, OCR evidence and totals | Complete |
| No DB writes | No Phase 1 commit endpoint or Genius mutation command; confirmation returns `commitAvailable=false` and `geniusWritePerformed=false` | Complete |

## 2. Measured Local Baseline

The committed synthetic dataset contains 3 Arabic/English/mixed invoices, 4 PNG pages and 5 source lines. Contract validation accepts all three OCR results. Across ten reviewed line fields per source line, 49 of 50 normalized values are present (98%); the missing value is an intentional fixture case, not an extraction success claim. All 5 source lines with a Vendor item code are covered by an exact identifier in the synthetic canonical seed.

The live SELECT-only catalog projection against the restored `Genius_Legacy` database recorded:

- 67,377 Items and 209 Vendors.
- 43,447 barcodes and 6,883 Vendor item codes.
- 67,377 untrusted labels and 43,810 identical Arabic/English byte fields.
- zero Genius writes.

The final local workflow smoke used one synthetic invoice page and produced 2 source lines, 20 local candidates and 4 canonical candidates. Two canonical candidates had exact identifiers; two wrong-strength candidates carried hard mismatches. A Vendor and Item selected through full local catalog search produced immutable revision 2, then `CONFIRMED`, with `commitAvailable=false` and `geniusWritePerformed=false`.

These are controlled acceptance metrics. They are not pharmacy-production OCR precision/recall, match-acceptance, latency, cost or correction-rate metrics. Those require an approved staging/pilot dataset and remain production-entry evidence.

## 3. Verification Evidence

No test sources were added and no test task or billable service was run. The Phase 1 evidence commands are:

```powershell
pnpm contracts:validate
pnpm phase0:validate
pnpm phase1:validate

dotnet build saas-platform/src/Saas.Api/PharmaAuto.Saas.Api.csproj --no-restore
dotnet build local-connector/src/Connector.LocalApi/PharmaAuto.Connector.LocalApi.csproj --no-restore
dotnet build local-connector/src/Connector.ControlUi/PharmaAuto.Connector.ControlUi.csproj --no-restore

Set-Location android-client
./gradlew.bat lintDebug assembleDebug
```

PowerShell parser inspection covers every installer script. `git diff --check` is the repository whitespace gate.

## 4. Fail-Closed Boundaries Proven by the Slice

- The Connector verifies the exact ES256 entitlement bytes before reserving OCR work and requires a SaaS mTLS client certificate outside Development.
- OCR evidence and issued candidate lists are immutable across edits. A full-catalog selection is resolved again by the Connector; a fabricated opaque reference or a known hard mismatch is rejected.
- Vendor/Item selection remains empty until an operator acts. Canonical candidates never become local Genius identities.
- Android drafts are scoped to the paired Connector so re-pairing cannot send an old pharmacy draft to a different Connector.
- Confirming a revision only records read-only review state. There is no code path from Phase 1 confirmation to a Genius transaction.

## 5. Explicitly Not Claimed

- No live Gemini request, paid cloud device, Firebase upload or Test Lab run occurred.
- No API-28+ physical Android device or emulator was available on this host for final visual/device acceptance; the known API-27 PAX device cannot install the minSdk-28 APK.
- PostgreSQL production deployment, certificate enrollment, KMS/secret injection, canonical embedding population, release signing and upgrade/rollback drills require environment-specific operations.
- Open threat-model verification, real-invoice accuracy, concurrency, load, retention recovery and security exercises remain production or pilot gates.
- Genius master-item creation, purchase Commit and reconciliation remain Phase 2/3+ work and are still blocked by Golden certification.
