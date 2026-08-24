# Phase 1 Read-Only Vertical Slice Implementation Record

Status: **Implementation baseline recorded for the local, non-billable read-only scope on 2026-08-21 and updated on 2026-08-24; acceptance remains pending.**

This record documents implemented surfaces and controlled local checks. It does not close the Phase 1 roadmap exit gate،authorize a Genius write or production deployment،or claim live Gemini/device/security acceptance. ADR-012 and ADR-013 are accepted design boundaries only؛Phase 2 mutation remains blocked by the remaining Phase 1 acceptance work and their implementation،test and approval gates.

## 1. Implementation Evidence

| Phase 1 deliverable | Implemented evidence | Result |
|---|---|---|
| Connector installer/service/control UI | `local-connector/installer/`, Windows Service host, WPF pairing/catalog/job/device UI, signed-manifest enforcement, DPAPI secrets, service/private-key ACLs, Private/Domain LocalSubnet firewall rule and production mTLS certificate gate | Implemented; environment acceptance pending |
| Android pairing/upload | One-time QR claim, Android Keystore P-256 identity, pinned Connector certificate, short challenges/tokens, CameraX capture, JPEG/PNG validation،PDF validation/rendering to ordered normalized JPEG pages،Room drafts and resumable WorkManager chunk upload | Implemented; target-device acceptance pending |
| Local catalog extraction and text integrity | SELECT-only `SqlGeniusCatalogReader`, opaque local identities, reversed CP1256 decoding retained as untrusted raw text, source-byte hashes, integrity flags and content-derived display direction | Implemented; independent regression evidence pending |
| SaaS tenant/subscription/quota | Tenant-scoped Connector authentication, forced PostgreSQL RLS, active entitlement, ES256 signature, Connector-side signature verification and atomic idempotent page reservation/settlement | Implemented; deployed tenant-isolation/concurrency acceptance pending |
| Gemini structured OCR | Production Interactions provider with strict structured output and post-response validation; Development uses only deterministic fixtures and refuses fixture mode outside Development | Implemented; no paid call made |
| Vendor/Product matching | Local identifier/FTS candidates, full-catalog manual search, structured strength/form hard mismatches, canonical exact/lexical candidates and versioned 768-dimension semantic retrieval with lexical fallback | Implemented; real/staging accuracy acceptance pending |
| Android review | Vendor/Item manual confirmation, hard-mismatch blocking, expiry/batch splits, per-split purchase price and two ordered percentage discounts, tax-inclusive selling price per BOX, new-stock-only policy, OCR evidence and totals | Implemented; target-device/accessibility acceptance pending |
| No DB writes | No Phase 1 commit endpoint or Genius mutation command; confirmation returns `commitAvailable=false` and `geniusWritePerformed=false` | Verified implementation boundary |

## 2. Measured Local Baseline

The committed synthetic dataset contains 3 Arabic/English/mixed invoices, 4 PNG pages and 5 source lines. Contract validation accepts all three OCR results. Across ten reviewed line fields per source line, 49 of 50 normalized values are present (98%); the missing value is an intentional fixture case, not an extraction success claim. All 5 source lines with a Vendor item code are covered by an exact identifier in the synthetic canonical seed.

The live SELECT-only catalog projection against the restored `Genius_Legacy` database recorded:

- 67,377 Items and 209 Vendors.
- 43,447 barcodes and 6,883 Vendor item codes.
- 67,377 untrusted labels and 43,810 identical Arabic/English byte fields.
- zero Genius writes.

The final local workflow smoke used one synthetic invoice page and produced 2 source lines, 20 local candidates and 4 canonical candidates. Two canonical candidates had exact identifiers; two wrong-strength candidates carried hard mismatches. A Vendor and Item selected through full local catalog search produced immutable revision 2, then `CONFIRMED`, with `commitAvailable=false` and `geniusWritePerformed=false`.

These are controlled local baseline metrics،not acceptance results. They are not pharmacy-production OCR precision/recall, match-acceptance, latency, cost or correction-rate metrics. Those require an approved staging/pilot dataset and remain production-entry evidence.

## 3. Current Verification Contract

The historic 2026-08-21 baseline used build/static checks only. The repository now contains deterministic test sources؛the current non-billable verification contract is:

```powershell
pnpm install --frozen-lockfile
pnpm contracts:validate
pnpm phase0:validate
pnpm phase1:validate

dotnet restore saas-platform/PharmaAuto.Saas.slnx --locked-mode
dotnet build saas-platform/PharmaAuto.Saas.slnx --no-restore
dotnet format saas-platform/PharmaAuto.Saas.slnx --verify-no-changes --no-restore
dotnet test saas-platform/PharmaAuto.Saas.slnx --no-build --no-restore
dotnet restore local-connector/PharmaAuto.Connector.slnx --locked-mode
dotnet build local-connector/PharmaAuto.Connector.slnx --no-restore
dotnet format local-connector/PharmaAuto.Connector.slnx --verify-no-changes --no-restore
dotnet test local-connector/PharmaAuto.Connector.slnx --no-build --no-restore

Set-Location android-client
./gradlew.bat testDebugUnitTest lintDebug assembleDebug
```

CI additionally audits transitive NuGet vulnerabilities and applies `saas-platform/db/001-phase-1.sql` to PostgreSQL 18 with pgvector. PowerShell parser inspection covers every installer script. `git diff --check` is the repository whitespace gate. A local pass is necessary evidence،not a substitute for target-device،live-provider or independent security acceptance.

## 4. Fail-Closed Boundaries Demonstrated by the Slice

- The Connector verifies the exact ES256 entitlement bytes before reserving OCR work and requires a SaaS mTLS client certificate outside Development.
- OCR evidence and issued candidate lists are immutable across edits. A full-catalog selection is resolved again by the Connector; a fabricated opaque reference or a known hard mismatch is rejected.
- Vendor/Item selection remains empty until an operator acts. Canonical candidates never become local Genius identities.
- Android drafts are scoped to the paired Connector so re-pairing cannot send an old pharmacy draft to a different Connector.
- A user-selected PDF is validated/rendered on Android into ordered JPEG pages before upload. Connector and SaaS reject raw PDF transport،and quota/page count equals the number of normalized page indices.
- Confirming a revision only records read-only review state. There is no code path from Phase 1 confirmation to a Genius transaction.

## 5. Acceptance Work Still Open

- run the full workflow on an API-28+ physical device or emulator and decide whether PAX API 27 is unsupported or triggers a deliberate minSdk/security revalidation.
- execute a controlled live Gemini staging run using synthetic data and record field-level behavior،schema rejection،latency،quota idempotency and cost.
- attach repeatable test/CI evidence for pairing/replay،tenant isolation،file handling،retention recovery،accessibility and restart/resume behavior.
- implement and independently verify [ADR-012](decisions/ADR-012-local-human-identity-and-step-up.md) so a paired device is never confused with an Operator،Catalog Manager or Supervisor.
- implement and independently verify [ADR-013](decisions/ADR-013-offline-genius-write-entitlements.md) before any offline New Item or Purchase mutation work.

Until these are complete،the roadmap status is **implementation complete / acceptance pending**.

## 6. Explicitly Not Claimed

- No live Gemini request, paid cloud device, Firebase upload or Test Lab run occurred.
- No API-28+ physical Android device or emulator was available on this host for final visual/device acceptance; the known API-27 PAX device cannot install the minSdk-28 APK.
- PostgreSQL production deployment, certificate enrollment, KMS/secret injection, canonical embedding population, release signing and upgrade/rollback drills require environment-specific operations.
- Open threat-model verification, real-invoice accuracy, concurrency, load, retention recovery and security exercises remain production or pilot gates.
- Genius master-item creation, purchase Commit and reconciliation remain Phase 2/3+ work and are still blocked by Golden certification.
