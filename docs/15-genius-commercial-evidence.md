# Genius Commercial Field Evidence

## Status and Safety Boundary

This is a read-only observation of `Genius_Legacy` on `localhost\SQL2008R2`, captured on 2026-08-19. The instance reports SQL Server `10.50.4000.0`, database compatibility level `80`, and an online multi-user database. Every query used `SELECT` under `READ UNCOMMITTED`; no setting, schema, row, trigger, backup, or service was changed.

These observations are strong enough to shape contracts and Golden scenarios. They do not certify a write profile. A production Commit remains disabled until the same behavior is reproduced and reconciled against a disposable restored Clone.

## Discount Evidence

The live `dbo.v_VenTrans` definition calculates the purchase value in this order:

1. start with `pur_trans_d.itm_pur_price`.
2. apply the line percentage in `pur_trans_d.itm_extra_dis`.
3. apply the header percentage in `pur_trans_h.total_dis_per` to the remaining value.
4. add purchase tax and other header-level adjustments.

This confirms the sequential percentage model used by the domain. It also confirms that Discount 1 is the line discount that affects the purchase-price path. `dbo.Sys_setting.pur_extra_disc_update_stock` is `1` in this database.

The storage boundary is not yet a certified write mapping:

- `itm_extra_dis` is non-zero on 438 of 163,729 purchase-detail rows.
- `itm_dis_per` and `itm_dis_mon` are zero on every purchase-detail row and are ignored by `v_VenTrans`; they must not be adopted merely because their names look relevant.
- `pur_trans_h.total_dis_per` exists but is zero on all 18,438 headers.
- `pur_trans_h.total_des_mon` is non-zero on 1,260 headers and is subtracted by the Vendor balance view as an invoice-level amount.

Pharma Auto therefore keeps both discounts as per-Posting-Line percentages in its own immutable revision. Discount 2 cannot be written to a guessed detail column. The Golden profile must prove whether the correct Genius translation is a calculated aggregate header amount, a header percentage constrained to one value for the invoice, or another required write-set.

## Selling-Price and Stock-Class Evidence

`pur_trans_d.itm_sell` is the purchase-detail selling-price snapshot. The stock tables hold class-specific values:

- `Item_Class.sell_price` and `Item_Class.sell_tax`.
- `Item_Class_Store.sell_price` and `Item_Class_Store.sell_tax`.
- `Item_Catalog.itm_def_sell_price` is a mutable current default, not the historical price of every stock class.

For 75,117 purchase lines that still identify the current receipt behind an `Item_Class` row, all 75,117 `Item_Class.sell_price` values equal the purchase-detail `itm_sell` snapshot. The database currently has 6,133 Items with multiple positive-quantity classes; 4,734 of those Items have more than one class selling price.

One identifier-only sample, `itm_id = 18911`, has positive remaining quantities at four historical class prices: EGP 22.50, 26.00, 30.00, and 43.00. Its catalog default is EGP 43.00, while older classes retain their earlier values. This demonstrates that class-specific pricing can preserve old stock.

The safe behavior is not automatic. Purchase history contains 3,982 item/class/expiry groups where the same `c_id` was reused across different `itm_sell` snapshots. Of these, 749 are non-expiring groups and 3,233 have an expiry. Updating such a class in place can affect stock received earlier.

The schema can represent the required isolation: live Items `65005` and `66681` each have two non-expiring class IDs with different selling prices and positive remaining quantities. Their history is consistent with separating new stock into a new class and retaining the old class price. That proves representability, not the correct insert/update sequence.

## Tax-Inclusive Input

The Android contract accepts one tax-inclusive selling price per `BOX`, as approved by the product owner. Genius stores `itm_sell`/`sell_price` plus separate tax-related columns. Current rows do not prove one universal formula for deriving `sell_tax`, especially across legacy tax configurations. The adapter must preserve the confirmed tax-inclusive input and block Commit until a Golden scenario proves the exact database representation and read-back calculation.

## Required Golden Scenarios

Before enabling any commercial write, record the complete reference write-set for:

1. a new expiry class with a new selling price.
2. an existing expiry/batch whose selling price is unchanged.
3. the same expiry/batch with a changed selling price, proving old quantity and old price remain intact.
4. a non-expiring Item with a changed selling price, proving an isolated new class is usable by later sales.
5. Discount 1 only, Discount 2 only, and both sequential discounts.
6. tax-inclusive selling prices for taxable and non-taxable Items.
7. unit conversion where purchase input and selling price are both per `BOX`.
8. rollback, connection-loss, and reconciliation failures for every affected table.

Each scenario must compare header, detail, class, store, catalog, Vendor financial side effects, and subsequent sales-class selection. If any required isolation cannot be reconciled, the result is `CommitRejected`, never a global fallback price update.
