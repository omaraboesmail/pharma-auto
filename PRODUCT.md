# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Stack

- Android client: Kotlin 2.x, Jetpack Compose with Material 3, CameraX, Room, WorkManager, Hilt, and Kotlinx Serialization.
- Local connector: .NET 10 Windows Service, ASP.NET Core/Kestrel, WPF control UI, SQLite sidecar, raw ADO.NET for certified Genius writes, and OpenTelemetry.
- SaaS platform: ASP.NET Core on .NET 10, PostgreSQL 18 with pgvector, managed object storage, Gemini structured OCR, and a modular-monolith architecture with separate workers.
- Admin portal: Next.js 16, React 19, TypeScript 5.x, Node.js 24 LTS, Supabase Auth with mandatory TOTP MFA, and generated OpenAPI clients.
- Shared contracts: OpenAPI 3.1 and versioned JSON Schemas.

## Users

The primary user is a pharmacy operator entering supplier purchase invoices into e-plus/Genius. The operator captures an invoice, reviews OCR output against source evidence, resolves products and vendors, splits quantities by expiry or batch, and submits the confirmed invoice.

Supporting roles are:

- Catalog managers, who can create and link new catalog items after duplicate checks.
- Pharmacy supervisors, who approve high-risk cases and handle duplicate or commit warnings.
- Local technicians, who install and maintain the connector and its Genius database connection.
- SaaS support, security, and billing administrators, whose access is limited to their operational duties.

## Product Purpose

Pharma Auto converts photographed or PDF supplier purchase invoices into auditable purchase transactions in an existing e-plus/Genius installation. It exists to reduce repetitive pharmacy data entry without accepting silently incorrect stock, vendor balances, or financial effects as an automation tradeoff.

Success means that operators can move from capture to a verified transaction with less manual entry, while every committed invoice is reconciled and every uncertain match or write state is surfaced for human resolution.

## Positioning

Pharma Auto is not a generic OCR importer. Its distinguishing mechanism combines structured OCR evidence, local pharmacy-controlled vendor and product resolution, human verification, expiry and batch splitting, a certified direct write profile for the legacy Genius database, and mandatory post-commit reconciliation. OCR and semantic search may propose data, but neither can choose final Genius identities or authorize a commit.

## Operating Context

- The product runs across a native Android capture/review client, a Windows connector inside the pharmacy network, a SaaS control plane, and a web administration portal.
- The Android client communicates with the local connector rather than directly with Genius or Gemini.
- The connector is the local authority for catalog projection, matching, durable jobs, database writes, and reconciliation.
- New OCR work requires SaaS connectivity; supported local capture, queues, and stored review work remain available on the pharmacy LAN according to offline policy.
- Purchase invoices may be Arabic, English, or mixed-language, and may arrive as camera images or multi-page PDFs.
- The product is privately deployed for pharmacy operations and will not be marketed on the public internet.

## Capabilities and Constraints

- Capture JPEG, PNG, and PDF invoices while preserving page order and checking image and file quality.
- Extract structured invoice fields with source text, page location, and evidence bounds.
- Resolve vendors and products locally using exact identifiers, confirmed mappings, structured pharmaceutical constraints, lexical retrieval, and vector candidates in that order.
- Treat names recovered from Genius reversed `varbinary` fields as raw untrusted labels; never repair missing or displaced Arabic/English characters heuristically.
- Let authorized users search the full local catalog and create new items only after explicit confirmation and duplicate checks.
- Split a source line into independently posted expiry or batch lines while preserving invoice order; every expiry row owns an editable quantity and expiry date.
- Let authorized Android users edit every Posting Line's purchase unit price, line discount, and selling unit price before confirmation, while preserving the OCR value and an audit trail of each correction.
- Never turn a selling-price edit into a silent global catalog change; the certified Genius profile must expose the affected scope, validate authorization, and reconcile the resulting price state.
- Detect true duplicate invoices and never change an invoice number merely to bypass duplicate detection.
- Commit only through a certified Genius database profile and an approved database fingerprint.
- Reconcile header, details, line order, stock, store, class, vendor, and financial side effects before reporting success.
- Never automatically retry an unknown commit outcome, mutate the Genius schema, store pharmacy SQL credentials in SaaS, or place Gemini or SQL credentials in Android.
- Customer or patient data processing, sales invoices, and unsupported ERP systems are outside the first release.
- Silent data corruption has no acceptable failure budget; unresolved data is preferable to a confident but incorrect match.

## Brand Commitments

- The product name is **Pharma Auto**.
- The name remains “Pharma Auto” in every language and must not be translated.
- The Android client uses conventional Material 3 with restrained light surfaces, forest-green primary actions, Android system typography, familiar controls, and plain operational labels.
- The primary operator may be non-technical; editing flows use progressive disclosure and visible source evidence instead of policy prose or implementation terminology.
- Future work may generate the product assets it needs; there are no existing visual assets that must be preserved.
- Public-internet marketing, customer claims, and testimonials are not required and must not be invented.

## Evidence on Hand

- Product scope, requirements, workflows, architecture, security rules, technology choices, testing criteria, operational plans, risks, and architecture decisions are documented under `docs/`.
- A restored `Genius.bak` database is present for controlled reverse engineering and Golden Scenario testing; it is not production evidence by itself.
- The repository defines Golden DB comparisons, fault injection, security tests, and a supervised single-pharmacy pilot as required validation work.
- No confirmed testimonials, customer claims, production benchmarks, completed pilot results, or public marketing proof are on hand, and future work must not fabricate them.
- `Order-Automating/` is explicitly outside the new product boundary and is not an implementation, dependency, asset, or visual baseline for Pharma Auto.

## Product Principles

1. Human authority at irreversible decisions: automation proposes and prepares; authorized users resolve ambiguity and approve consequential actions.
2. Local truth before cloud inference: final Genius identities, catalog state, database writes, and reconciliation remain controlled inside the pharmacy boundary.
3. Evidence over confidence: preserve source evidence, explain match reasons, and prefer an unresolved state to a silently wrong result.
4. Verified completion: a successful SQL transaction is not success until business effects have been independently reconciled.
5. Recover safely: durable jobs, idempotent transitions, explicit unknown states, and audited recovery take priority over automatic retries.

## Accessibility & Inclusion

- Android and administrative interfaces must use accessible interaction primitives and be tested on low-end devices.
- Product interfaces must support Arabic, English, and mixed-language invoice content, including correct right-to-left and left-to-right layout behavior.
- Mixed-script labels must use Unicode BiDi isolation and content-derived display direction without mutating stored or matched text.
- Safety-critical warnings, evidence, review states, and recovery actions must remain understandable without relying on color alone.
