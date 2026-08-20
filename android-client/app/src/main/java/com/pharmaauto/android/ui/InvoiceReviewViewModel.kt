package com.pharmaauto.android.ui

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.ViewModel
import java.time.LocalDate

class InvoiceReviewViewModel : ViewModel() {
    var uiState by mutableStateOf(sampleInvoiceReviewState())
        private set

    private var nextExpiryId = 100L

    fun previousLine() {
        if (uiState.currentLineIndex > 0) {
            uiState = uiState.copy(
                currentLineIndex = uiState.currentLineIndex - 1,
                showOcrEvidence = false
            )
        }
    }

    fun nextLine() {
        val validation = InvoiceReviewRules.validate(uiState.currentLine)
        val errorMessage = validation.errorMessage()
        if (errorMessage != null) {
            uiState = uiState.copy(message = errorMessage)
            return
        }

        val reviewedLines = uiState.lines.markReviewed(uiState.currentLineIndex)
        uiState = uiState.copy(
            lines = reviewedLines,
            currentLineIndex = (uiState.currentLineIndex + 1).coerceAtMost(uiState.lines.lastIndex),
            showOcrEvidence = false,
            message = null
        )
    }

    fun toggleOcrEvidence() {
        uiState = uiState.copy(showOcrEvidence = !uiState.showOcrEvidence)
    }

    fun toggleTotals() {
        uiState = uiState.copy(totalsExpanded = !uiState.totalsExpanded)
    }

    fun updateCommercial(field: CommercialField, input: String) {
        val normalized = InvoiceReviewRules.normalizeDecimalInput(input)
        updateCurrentLine { line ->
            val confirmed = when (field) {
                CommercialField.PurchaseUnitPrice ->
                    line.confirmed.copy(purchaseUnitPrice = normalized)
                CommercialField.DiscountOne -> line.confirmed.copy(discountOne = normalized)
                CommercialField.DiscountTwo -> line.confirmed.copy(discountTwo = normalized)
                CommercialField.SellingUnitPrice ->
                    line.confirmed.copy(sellingUnitPrice = normalized)
            }
            line.copy(confirmed = confirmed, reviewed = false)
        }
    }

    fun updateExpiryQuantity(expiryIndex: Int, input: String) {
        val normalized = InvoiceReviewRules.normalizeDecimalInput(input)
        updateCurrentLine { line ->
            line.copy(
                expiries = line.expiries.mapIndexed { index, expiry ->
                    if (index == expiryIndex) expiry.copy(quantity = normalized) else expiry
                },
                reviewed = false
            )
        }
    }

    fun updateExpiryDate(expiryIndex: Int, date: LocalDate) {
        updateCurrentLine { line ->
            line.copy(
                expiries = line.expiries.mapIndexed { index, expiry ->
                    if (index == expiryIndex) expiry.copy(expiryDate = date) else expiry
                },
                reviewed = false
            )
        }
    }

    fun splitExpiry(expiryIndex: Int) {
        updateCurrentLine { line ->
            InvoiceReviewRules.splitExpiry(
                line = line,
                expiryIndex = expiryIndex,
                newId = "expiry-${nextExpiryId++}"
            )
        }
    }

    fun addExpiry() {
        updateCurrentLine { line ->
            line.copy(
                expiries = line.expiries + ExpiryDraft(
                    id = "expiry-${nextExpiryId++}",
                    quantity = "0",
                    expiryDate = null
                ),
                reviewed = false
            )
        }
    }

    fun removeExpiry(expiryIndex: Int) {
        updateCurrentLine { line ->
            if (line.expiries.size == 1) {
                line
            } else {
                line.copy(
                    expiries = line.expiries.filterIndexed { index, _ -> index != expiryIndex },
                    reviewed = false
                )
            }
        }
    }

    fun finishReview() {
        val currentValidation = InvoiceReviewRules.validate(uiState.currentLine)
        val currentError = currentValidation.errorMessage()
        if (currentError != null) {
            uiState = uiState.copy(message = currentError)
            return
        }

        val reviewedLines = uiState.lines.markReviewed(uiState.currentLineIndex)
        val invalidIndex = reviewedLines.indexOfFirst { line ->
            InvoiceReviewRules.validate(line).errorMessage() != null
        }
        if (invalidIndex >= 0) {
            val invalidMessage = InvoiceReviewRules.validate(reviewedLines[invalidIndex])
                .errorMessage()
                ?: ReviewMessage.InvalidCommercial
            uiState = uiState.copy(
                lines = reviewedLines,
                currentLineIndex = invalidIndex,
                showOcrEvidence = false,
                message = invalidMessage
            )
            return
        }

        val remainingIndex = reviewedLines.indexOfFirst { line -> !line.reviewed }
        uiState = if (remainingIndex >= 0) {
            uiState.copy(
                lines = reviewedLines,
                currentLineIndex = remainingIndex,
                showOcrEvidence = false,
                message = ReviewMessage.ReviewRemainingLine
            )
        } else {
            uiState.copy(
                lines = reviewedLines,
                message = ReviewMessage.PrototypeReviewComplete
            )
        }
    }

    fun consumeMessage() {
        uiState = uiState.copy(message = null)
    }

    private inline fun updateCurrentLine(transform: (InvoiceLineDraft) -> InvoiceLineDraft) {
        val currentIndex = uiState.currentLineIndex
        uiState = uiState.copy(
            lines = uiState.lines.mapIndexed { index, line ->
                if (index == currentIndex) transform(line) else line
            }
        )
    }

    private fun LineValidation.errorMessage(): ReviewMessage? = when {
        !commercialValid -> ReviewMessage.InvalidCommercial
        !expiryValid -> ReviewMessage.InvalidExpiry
        else -> null
    }

    private fun List<InvoiceLineDraft>.markReviewed(indexToReview: Int): List<InvoiceLineDraft> =
        mapIndexed { index, line ->
            if (index == indexToReview) line.copy(reviewed = true) else line
        }
}
