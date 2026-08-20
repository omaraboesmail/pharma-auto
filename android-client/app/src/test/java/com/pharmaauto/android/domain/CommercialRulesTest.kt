package com.pharmaauto.android.domain

import java.math.BigDecimal
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class CommercialRulesTest {
    @Test
    fun calculateAppliesTheApprovedSequentialPercentageRules() {
        val result = CommercialRules.calculate(
            quantity = BigDecimal("2"),
            values = validValues()
        )

        assertEquals(BigDecimal("100.00"), result.grossPurchaseUnitPrice)
        assertEquals(0, BigDecimal("90").compareTo(result.purchaseUnitPriceAfterDiscount1))
        assertEquals(0, BigDecimal("180").compareTo(result.lineSubtotalAfterDiscount1))
        assertEquals(0, BigDecimal("171").compareTo(result.netLineSubtotalAfterDiscount2))
    }

    @Test
    fun validateRejectsAnyPolicyThatCouldRepriceOldStock() {
        val invalid = validValues().copy(
            sellingPriceScope = "GLOBAL_ITEM",
            existingStockPriceBehavior = "REPRICE"
        )

        val errors = CommercialRules.validate(BigDecimal("2"), invalid)

        assertTrue(errors.contains("sellingPriceScope must be NEW_STOCK_ONLY."))
        assertTrue(errors.contains("existingStockPriceBehavior must be PRESERVE."))
    }

    private fun validValues() = CommercialValues(
        currency = "EGP",
        purchaseUnit = "BOX",
        purchaseUnitPrice = BigDecimal("100.00"),
        purchasePriceTaxTreatment = "EXCLUSIVE",
        discounts = listOf(
            PercentageDiscount(
                sequence = 1,
                percentage = BigDecimal("10.00"),
                applicationBasis = DiscountApplicationBasis.PurchaseUnitPrice,
                affectsPurchaseUnitPrice = true
            ),
            PercentageDiscount(
                sequence = 2,
                percentage = BigDecimal("5.00"),
                applicationBasis = DiscountApplicationBasis.RemainingLineSubtotal,
                affectsPurchaseUnitPrice = false
            )
        ),
        sellingUnit = "BOX",
        sellingUnitPrice = BigDecimal("150.00"),
        sellingPriceTaxTreatment = "INCLUSIVE",
        sellingPriceScope = "NEW_STOCK_ONLY",
        existingStockPriceBehavior = "PRESERVE",
        unsupportedScopeBehavior = "BLOCK_COMMIT"
    )
}
