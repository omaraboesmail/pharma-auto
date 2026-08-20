# Initialization Decision Gate

Status: **Initialization approved**

Baseline date: 2026-08-19

This gate keeps package identity and financial semantics out of placeholder code. The repository can move directly to a contract-first, read-only vertical slice after the blocking answers below are approved.

## 1. Decisions Already Fixed

These choices are accepted in the current product and architecture documents and do not need to be reopened for initialization:

- Product name: **Pharma Auto**.
- Greenfield monorepo; `Order-Automating/` is not a source or dependency.
- Android: Kotlin, Jetpack Compose and Material 3.
- Local Connector and SaaS: .NET 10; SaaS begins as a modular monolith.
- Admin: Next.js 16, React 19 and TypeScript.
- Contract-first boundaries: OpenAPI 3.1 and versioned JSON Schemas.
- Android talks only to the Local Connector; it contains no Gemini or SQL credentials.
- Local Connector owns local Genius identity, certified writes and reconciliation.
- The first executable slice is read-only with respect to Genius; Direct DB Commit remains behind Golden DB evidence and fingerprint certification.

## 2. Approved Initialization Decisions

| ID | Approved baseline | Status and guardrail |
|---|---|---|
| INIT-01 | Versioned invoice contracts + Android review shell + Connector Local API skeleton + contract tests; no Genius writes | Approved |
| INIT-02 | Android `applicationId` and namespace: `com.pharmaauto.android`; .NET root namespace: `PharmaAuto` | Approved as the permanent Android identity on 2026-08-19 |
| INIT-03 | `minSdk 28`; `compileSdk 37`; `targetSdk 37`; Firebase App Distribution | Approved. Project `pharma-auto-eg-smartsolustions`, Android package `com.pharmaauto.android`, and group `internal-testers` initialized on 2026-08-19; uploads remain explicit. A connected PAX A920Pro reported Android 8.1 / API 27 on 2026-08-20 and is therefore outside this baseline |
| INIT-04 | `purchaseUnitPrice` is the supplier price for the selected purchase unit before discounts; tax treatment remains explicit | Approved |
| INIT-05 | Exactly two sequential percentage discounts. Discount 1 changes the purchase unit price; Discount 2 applies to the remaining line subtotal after Discount 1 and does not rewrite the stored purchase-unit-price snapshot | Approved. Live read-side evidence confirms the sequence; the write translation and rounding still require Golden evidence |
| INIT-06 | Currency `EGP`; `sellingUnitPrice` is per `BOX` and tax-inclusive | Approved |
| INIT-07 | A new selling price applies only to newly received stock. Existing stock retains its previous selling price | Approved. Live data proves distinct stock classes can carry different prices, but ordinary class reuse is unsafe; a certified class-split write-set is required |
| INIT-08 | A Source Line action may explicitly apply a value to all current splits; each Posting Line may then override quantity, expiry date and commercial values independently | Approved. The Android editor exposes add, edit, split and remove actions, and blocks save when split quantities do not match the source Item quantity |
| INIT-09 | Decimal strings in contracts; no binary floating point | EGP display precision and Genius rounding remain profile-certified Golden-evidence items |
| INIT-10 | Retrofit + OkHttp + Kotlinx Serialization, with MockWebServer tests | Approved |

### PAX hardware compatibility gate

Do not install the current APK on the observed PAX A920Pro: its API 27 runtime is below `minSdk 28`. Before PAX deployment, choose one of these paths deliberately:

1. Keep `minSdk 28` and use an Android 9 / API 28 or newer PAX model or firmware.
2. Reopen INIT-03, lower the minimum to API 27, and revalidate every Android dependency, security assumption, device-management path and invoice-review flow on that terminal.

The initialization keeps `minSdk 28` until that choice is explicitly changed. No cloud-device or billable workaround is authorized.

## 3. Decisions That May Be Deferred

These do not block the first local vertical slice:

- cloud provider, region and production account layout.
- Terraform versus OpenTofu.
- final subscription tiers and OCR page pricing.
- production document-retention duration within the already accepted encrypted-TTL policy.
- managed container and object-storage vendors.
- production release rings and store/MDM vendor.

They must be fixed before the corresponding SaaS/Infrastructure production work begins.

## 4. Required Genius Evidence Before Price Writes

Executable scaffolding may model the fields immediately, but a live price write remains blocked until Golden e-plus scenarios prove:

- the exact rounding and storage translation for the two percentage discounts, including the invoice-level monetary side effect.
- the certified class-split write-set that preserves an old class while assigning the new selling price to newly received stock.
- how unit conversions affect purchase and selling prices.
- decimal scale and rounding at detail, header and financial-document levels.
- how the tax-inclusive selling price maps to `sell_price`, `sell_tax`, catalog defaults and store-specific values.
- the required permission, audit rows and reconciliation read-back for that scope.

The read-only evidence is recorded in [Genius Commercial Field Evidence](15-genius-commercial-evidence.md) and the resulting boundary in [ADR-011](decisions/ADR-011-commercial-edits-and-stock-class-pricing.md).

If a Genius formula or storage scope is unknown, record `needs Golden evidence` rather than guessing. That does not block the read-only review slice; it blocks only certified Genius price writes.
