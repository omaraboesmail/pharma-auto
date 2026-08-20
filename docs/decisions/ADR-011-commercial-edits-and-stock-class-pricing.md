# ADR-011: Commercial Edits and Stock-Class Pricing

**Status:** Accepted with certified-write gate

## Context

The Android operator must edit purchase unit price, two sequential percentage discounts, and tax-inclusive selling price per `BOX` for every Posting Line. The approved selling price applies only to newly received stock. Existing Genius stock must retain its prior price.

Live read-only evidence confirms a line discount in `pur_trans_d.itm_extra_dis`, an additional header discount stage in the Vendor balance formula, a purchase-detail selling snapshot in `pur_trans_d.itm_sell`, and class-specific selling values in `Item_Class`. It also shows that Genius commonly reuses a class across receipts, so an in-place class price update can violate the new-stock-only rule.

## Decision

- Pharma Auto owns two percentage values per Posting Line. Discount 1 changes the purchase-unit-price path; Discount 2 applies to the remaining line subtotal.
- Android may edit both discounts, purchase unit price, and tax-inclusive selling unit price independently on every Posting Line. Every correction creates audit evidence and a new immutable invoice revision.
- The Genius profile may translate a domain value to a different legacy storage shape only after a Golden scenario proves the financial result, rounding, return behavior, and reconciliation.
- A changed selling price must resolve or create a stock class that contains only the new receipt. The profile must not change an old class, all classes, or the Item master as a fallback.
- `Item_Catalog.itm_def_sell_price` is not proof of historical stock price. Any catalog-default update is a separate, explicit, reconciled side effect.
- If class isolation, tax mapping, or discount translation cannot be proven for the certified fingerprint, Commit returns `CommitRejected` and remains available for manual entry in e-plus.

## Consequences

- The first executable vertical slice remains read-only with respect to Genius.
- Contracts use decimal strings and retain the two user-entered percentages even if Genius stores a derived invoice amount.
- Reconciliation must compare old and new class IDs, quantities, selling prices, tax values, purchase snapshots, header totals, and Vendor financial side effects.
- Golden tests must cover expiring and non-expiring Items, same-expiry price changes, class reuse, tax, units, rollback, and ambiguous connection loss.
- The connector needs a second reviewer for any implementation that writes these fields.
