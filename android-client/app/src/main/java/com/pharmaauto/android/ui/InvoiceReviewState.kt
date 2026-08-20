package com.pharmaauto.android.ui

import androidx.compose.runtime.Immutable
import java.math.BigDecimal
import java.math.RoundingMode
import java.time.LocalDate

enum class CommercialField {
    PurchaseUnitPrice,
    DiscountOne,
    DiscountTwo,
    SellingUnitPrice
}

enum class ReviewMessage {
    PrototypeReviewComplete,
    ReviewRemainingLine,
    InvalidCommercial,
    InvalidExpiry
}

@Immutable
data class OcrCommercialEvidence(
    val purchaseUnitPrice: String,
    val discountOne: String,
    val discountTwo: String,
    val sellingUnitPrice: String
)

@Immutable
data class CommercialDraft(
    val purchaseUnitPrice: String,
    val discountOne: String,
    val discountTwo: String,
    val sellingUnitPrice: String
)

@Immutable
data class ExpiryDraft(
    val id: String,
    val quantity: String,
    val expiryDate: LocalDate?
)

@Immutable
data class InvoiceLineDraft(
    val id: String,
    val itemReference: String,
    val sourceQuantity: String,
    val evidence: OcrCommercialEvidence,
    val confirmed: CommercialDraft,
    val expiries: List<ExpiryDraft>,
    val reviewed: Boolean
)

@Immutable
data class InvoiceReviewUiState(
    val lines: List<InvoiceLineDraft>,
    val currentLineIndex: Int = 0,
    val showOcrEvidence: Boolean = false,
    val totalsExpanded: Boolean = false,
    val message: ReviewMessage? = null
) {
    val currentLine: InvoiceLineDraft
        get() = lines[currentLineIndex]

    val reviewedLineCount: Int
        get() = lines.count(InvoiceLineDraft::reviewed)
}

@Immutable
data class LineValidation(
    val commercialValid: Boolean,
    val expiryQuantitiesValid: Boolean,
    val expiryDatesComplete: Boolean,
    val expiryValid: Boolean,
    val assignedQuantity: BigDecimal,
    val requiredQuantity: BigDecimal
)

@Immutable
data class InvoiceTotals(
    val grossPurchase: BigDecimal,
    val netPurchase: BigDecimal,
    val expectedSelling: BigDecimal
) {
    val discountSavings: BigDecimal
        get() = grossPurchase.subtract(netPurchase).max(BigDecimal.ZERO)
}

object InvoiceReviewRules {
    private val OneHundred = BigDecimal("100")

    fun normalizeDecimalInput(input: String): String {
        val mapped = buildString(input.length) {
            input.forEach { character ->
                append(
                    when (character) {
                        '٠' -> '0'
                        '١' -> '1'
                        '٢' -> '2'
                        '٣' -> '3'
                        '٤' -> '4'
                        '٥' -> '5'
                        '٦' -> '6'
                        '٧' -> '7'
                        '٨' -> '8'
                        '٩' -> '9'
                        '٫', ',' -> '.'
                        else -> character
                    }
                )
            }
        }

        var decimalSeen = false
        return buildString(mapped.length) {
            mapped.forEach { character ->
                when {
                    character.isDigit() -> append(character)
                    character == '.' && !decimalSeen -> {
                        append(character)
                        decimalSeen = true
                    }
                }
            }
        }
    }

    fun decimalOrNull(value: String): BigDecimal? =
        normalizeDecimalInput(value).takeIf(String::isNotBlank)?.toBigDecimalOrNull()

    fun validate(line: InvoiceLineDraft): LineValidation {
        val purchase = decimalOrNull(line.confirmed.purchaseUnitPrice)
        val discountOne = decimalOrNull(line.confirmed.discountOne)
        val discountTwo = decimalOrNull(line.confirmed.discountTwo)
        val selling = decimalOrNull(line.confirmed.sellingUnitPrice)
        val required = decimalOrNull(line.sourceQuantity) ?: BigDecimal.ZERO
        val assigned = line.expiries.fold(BigDecimal.ZERO) { total, expiry ->
            total.add(decimalOrNull(expiry.quantity) ?: BigDecimal.ZERO)
        }

        val commercialValid = purchase != null && purchase >= BigDecimal.ZERO &&
            selling != null && selling >= BigDecimal.ZERO &&
            discountOne != null && discountOne in BigDecimal.ZERO..OneHundred &&
            discountTwo != null && discountTwo in BigDecimal.ZERO..OneHundred
        val expiryQuantitiesValid = line.expiries.isNotEmpty() &&
            line.expiries.all { expiry ->
                val quantity = decimalOrNull(expiry.quantity)
                quantity != null && quantity > BigDecimal.ZERO
            } && assigned.compareTo(required) == 0
        val expiryDatesComplete = line.expiries.isNotEmpty() &&
            line.expiries.all { expiry -> expiry.expiryDate != null }
        val expiryValid = expiryQuantitiesValid && expiryDatesComplete

        return LineValidation(
            commercialValid = commercialValid,
            expiryQuantitiesValid = expiryQuantitiesValid,
            expiryDatesComplete = expiryDatesComplete,
            expiryValid = expiryValid,
            assignedQuantity = assigned,
            requiredQuantity = required
        )
    }

    fun splitExpiry(
        line: InvoiceLineDraft,
        expiryIndex: Int,
        newId: String
    ): InvoiceLineDraft {
        val selected = line.expiries[expiryIndex]
        val quantity = decimalOrNull(selected.quantity) ?: BigDecimal.ZERO
        val newQuantity = if (quantity > BigDecimal.ONE) {
            quantity.divide(BigDecimal("2"), 3, RoundingMode.HALF_UP)
                .stripTrailingZeros()
        } else {
            BigDecimal.ZERO
        }
        val remainingQuantity = quantity.subtract(newQuantity).stripTrailingZeros()
        val updated = selected.copy(quantity = remainingQuantity.toPlainString())
        val inserted = ExpiryDraft(
            id = newId,
            quantity = newQuantity.toPlainString(),
            expiryDate = null
        )
        val expiries = line.expiries.toMutableList().apply {
            this[expiryIndex] = updated
            add(expiryIndex + 1, inserted)
        }
        return line.copy(expiries = expiries, reviewed = false)
    }

    fun totals(lines: List<InvoiceLineDraft>): InvoiceTotals? {
        if (lines.isEmpty()) return null

        var totals = InvoiceTotals(BigDecimal.ZERO, BigDecimal.ZERO, BigDecimal.ZERO)
        for (line in lines) {
            val validation = validate(line)
            if (!validation.commercialValid || !validation.expiryValid) return null

            val quantity = validation.assignedQuantity
            val purchase = decimalOrNull(line.confirmed.purchaseUnitPrice) ?: return null
            val discountOne = decimalOrNull(line.confirmed.discountOne) ?: return null
            val discountTwo = decimalOrNull(line.confirmed.discountTwo) ?: return null
            val selling = decimalOrNull(line.confirmed.sellingUnitPrice) ?: return null
            val gross = purchase.multiply(quantity)
            val afterDiscountOne = gross.multiply(
                OneHundred.subtract(discountOne).divide(OneHundred)
            )
            val net = afterDiscountOne.multiply(
                OneHundred.subtract(discountTwo).divide(OneHundred)
            )
            totals = totals.copy(
                grossPurchase = totals.grossPurchase.add(gross),
                netPurchase = totals.netPurchase.add(net),
                expectedSelling = totals.expectedSelling.add(selling.multiply(quantity))
            )
        }
        return totals
    }
}

fun sampleInvoiceReviewState(): InvoiceReviewUiState = InvoiceReviewUiState(
    lines = listOf(
        InvoiceLineDraft(
            id = "line-1",
            itemReference = "60495",
            sourceQuantity = "3",
            evidence = OcrCommercialEvidence("100.00", "10%", "5%", "150.00"),
            confirmed = CommercialDraft("100.00", "10", "5", "150.00"),
            expiries = listOf(
                ExpiryDraft("line-1-expiry-1", "2", LocalDate.of(2028, 6, 30)),
                ExpiryDraft("line-1-expiry-2", "1", LocalDate.of(2029, 6, 30))
            ),
            reviewed = false
        ),
        InvoiceLineDraft(
            id = "line-2",
            itemReference = "71320",
            sourceQuantity = "1",
            evidence = OcrCommercialEvidence("80.00", "2.5%", "0%", "120.00"),
            confirmed = CommercialDraft("80.00", "2.5", "0", "120.00"),
            expiries = listOf(
                ExpiryDraft("line-2-expiry-1", "1", LocalDate.of(2027, 12, 31))
            ),
            reviewed = true
        ),
        InvoiceLineDraft(
            id = "line-3",
            itemReference = "48271",
            sourceQuantity = "4",
            evidence = OcrCommercialEvidence("50.00", "0%", "3%", "75.00"),
            confirmed = CommercialDraft("50.00", "0", "3", "75.00"),
            expiries = listOf(
                ExpiryDraft("line-3-expiry-1", "4", LocalDate.of(2028, 3, 31))
            ),
            reviewed = true
        )
    )
)
