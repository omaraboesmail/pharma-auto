package com.pharmaauto.android.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class InvoiceReviewRulesTest {
    @Test
    fun invalidCommercialTextIsPreservedAndBlocksTotalsAndCompletion() {
        val viewModel = InvoiceReviewViewModel()

        viewModel.updateCommercial(CommercialField.PurchaseUnitPrice, "-5")

        assertEquals("-5", viewModel.uiState.currentLine.confirmed.purchaseUnitPrice)
        assertFalse(InvoiceReviewRules.validate(viewModel.uiState.currentLine).commercialValid)
        assertNull(InvoiceReviewRules.totals(viewModel.uiState.lines))

        viewModel.finishReview()
        assertEquals(ReviewMessage.InvalidCommercial, viewModel.uiState.message)
    }

    @Test
    fun invalidQuantityTextIsPreservedAndBlocksTotalsAndCompletion() {
        val viewModel = InvoiceReviewViewModel()

        viewModel.updateExpiryQuantity(expiryIndex = 0, input = "1e3")

        assertEquals("1e3", viewModel.uiState.currentLine.expiries[0].quantity)
        assertFalse(InvoiceReviewRules.validate(viewModel.uiState.currentLine).expiryValid)
        assertNull(InvoiceReviewRules.totals(viewModel.uiState.lines))

        viewModel.finishReview()
        assertEquals(ReviewMessage.InvalidExpiry, viewModel.uiState.message)
    }

    @Test
    fun excessivePercentageScaleBlocksTotals() {
        val state = sampleInvoiceReviewState()
        val line = state.currentLine.copy(
            confirmed = state.currentLine.confirmed.copy(discountOne = "1.12345")
        )

        assertFalse(InvoiceReviewRules.validate(line).commercialValid)
        assertNull(InvoiceReviewRules.totals(listOf(line)))
    }

    @Test
    fun splitPreservesInvalidRawQuantityAndShowsTargetedMessage() {
        val viewModel = InvoiceReviewViewModel()
        viewModel.updateExpiryQuantity(expiryIndex = 0, input = "1e3")
        val before = viewModel.uiState.currentLine.expiries

        viewModel.splitExpiry(expiryIndex = 0)

        assertEquals(before, viewModel.uiState.currentLine.expiries)
        assertEquals("1e3", viewModel.uiState.currentLine.expiries[0].quantity)
        assertEquals(ReviewMessage.InvalidSplitQuantity, viewModel.uiState.message)
    }

    @Test
    fun splitPreservesNonPositiveQuantityAndShowsTargetedMessage() {
        val viewModel = InvoiceReviewViewModel()
        viewModel.updateExpiryQuantity(expiryIndex = 0, input = "0")
        val before = viewModel.uiState.currentLine.expiries

        viewModel.splitExpiry(expiryIndex = 0)

        assertEquals(before, viewModel.uiState.currentLine.expiries)
        assertEquals("0", viewModel.uiState.currentLine.expiries[0].quantity)
        assertEquals(ReviewMessage.InvalidSplitQuantity, viewModel.uiState.message)
    }

    @Test
    fun arabicIndicValuesRemainVisibleAndParticipateInCalculations() {
        val state = sampleInvoiceReviewState()
        val line = state.currentLine.copy(
            confirmed = CommercialDraft(
                purchaseUnitPrice = "١٠٠٫٠٠",
                discountOne = "١٠",
                discountTwo = "٥",
                sellingUnitPrice = "١٥٠٫٠٠"
            ),
            expiries = state.currentLine.expiries.mapIndexed { index, expiry ->
                expiry.copy(quantity = if (index == 0) "٢" else "١")
            }
        )

        assertTrue(InvoiceReviewRules.validate(line).commercialValid)
        assertTrue(InvoiceReviewRules.validate(line).expiryValid)
        val totals = requireNotNull(InvoiceReviewRules.totals(listOf(line)))
        assertEquals(0, totals.netPurchase.compareTo(java.math.BigDecimal("256.5")))
    }
}
