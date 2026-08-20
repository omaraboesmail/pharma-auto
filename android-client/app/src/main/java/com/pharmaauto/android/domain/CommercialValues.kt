package com.pharmaauto.android.domain

import java.math.BigDecimal

enum class DiscountApplicationBasis {
    PurchaseUnitPrice,
    RemainingLineSubtotal
}

data class PercentageDiscount(
    val sequence: Int,
    val percentage: BigDecimal,
    val applicationBasis: DiscountApplicationBasis,
    val affectsPurchaseUnitPrice: Boolean
)

data class CommercialValues(
    val currency: String,
    val purchaseUnit: String,
    val purchaseUnitPrice: BigDecimal,
    val purchasePriceTaxTreatment: String,
    val discounts: List<PercentageDiscount>,
    val sellingUnit: String,
    val sellingUnitPrice: BigDecimal,
    val sellingPriceTaxTreatment: String,
    val sellingPriceScope: String,
    val existingStockPriceBehavior: String,
    val unsupportedScopeBehavior: String
)

data class CommercialCalculation(
    val grossPurchaseUnitPrice: BigDecimal,
    val purchaseUnitPriceAfterDiscount1: BigDecimal,
    val lineSubtotalAfterDiscount1: BigDecimal,
    val netLineSubtotalAfterDiscount2: BigDecimal
)

object CommercialRules {
    const val Currency = "EGP"
    const val SellingUnit = "BOX"
    const val SellingPriceTaxTreatment = "INCLUSIVE"
    const val SellingPriceScope = "NEW_STOCK_ONLY"
    const val ExistingStockPriceBehavior = "PRESERVE"
    const val UnsupportedScopeBehavior = "BLOCK_COMMIT"

    private val OneHundred = BigDecimal("100")

    fun calculate(quantity: BigDecimal, values: CommercialValues): CommercialCalculation {
        val errors = validate(quantity, values)
        require(errors.isEmpty()) { errors.joinToString(separator = " ") }

        val discountOneMultiplier = OneHundred
            .subtract(values.discounts[0].percentage)
            .divide(OneHundred)
        val purchaseUnitPriceAfterDiscountOne = values.purchaseUnitPrice
            .multiply(discountOneMultiplier)
        val lineSubtotalAfterDiscountOne = quantity
            .multiply(purchaseUnitPriceAfterDiscountOne)
        val discountTwoMultiplier = OneHundred
            .subtract(values.discounts[1].percentage)
            .divide(OneHundred)
        val netLineSubtotalAfterDiscountTwo = lineSubtotalAfterDiscountOne
            .multiply(discountTwoMultiplier)

        return CommercialCalculation(
            grossPurchaseUnitPrice = values.purchaseUnitPrice,
            purchaseUnitPriceAfterDiscount1 = purchaseUnitPriceAfterDiscountOne,
            lineSubtotalAfterDiscount1 = lineSubtotalAfterDiscountOne,
            netLineSubtotalAfterDiscount2 = netLineSubtotalAfterDiscountTwo
        )
    }

    fun validate(quantity: BigDecimal, values: CommercialValues): List<String> = buildList {
        if (quantity <= BigDecimal.ZERO) {
            add("quantity must be greater than zero.")
        }

        if (values.purchaseUnitPrice < BigDecimal.ZERO) {
            add("purchaseUnitPrice cannot be negative.")
        }

        if (values.sellingUnitPrice < BigDecimal.ZERO) {
            add("sellingUnitPrice cannot be negative.")
        }

        requireValue("currency", values.currency, Currency)
        requireValue("sellingUnit", values.sellingUnit, SellingUnit)
        requireValue(
            "sellingPriceTaxTreatment",
            values.sellingPriceTaxTreatment,
            SellingPriceTaxTreatment
        )
        requireValue("sellingPriceScope", values.sellingPriceScope, SellingPriceScope)
        requireValue(
            "existingStockPriceBehavior",
            values.existingStockPriceBehavior,
            ExistingStockPriceBehavior
        )
        requireValue(
            "unsupportedScopeBehavior",
            values.unsupportedScopeBehavior,
            UnsupportedScopeBehavior
        )

        if (values.discounts.size != 2) {
            add("discounts must contain exactly two sequential percentage discounts.")
            return@buildList
        }

        values.discounts.forEachIndexed { index, discount ->
            val expectedSequence = index + 1
            if (discount.sequence != expectedSequence) {
                add("discount $expectedSequence must have sequence $expectedSequence.")
            }
            if (discount.percentage < BigDecimal.ZERO || discount.percentage > OneHundred) {
                add("discount $expectedSequence percentage must be between 0 and 100.")
            }
        }

        val discountOne = values.discounts[0]
        if (
            discountOne.applicationBasis != DiscountApplicationBasis.PurchaseUnitPrice ||
            !discountOne.affectsPurchaseUnitPrice
        ) {
            add("discount 1 must affect the purchase unit price.")
        }

        val discountTwo = values.discounts[1]
        if (
            discountTwo.applicationBasis != DiscountApplicationBasis.RemainingLineSubtotal ||
            discountTwo.affectsPurchaseUnitPrice
        ) {
            add(
                "discount 2 must apply to the remaining line subtotal without rewriting " +
                    "the purchase unit price."
            )
        }
    }

    private fun MutableList<String>.requireValue(
        field: String,
        actual: String,
        required: String
    ) {
        if (actual != required) {
            add("$field must be $required.")
        }
    }
}
