package com.pharmaauto.android.ui

import com.pharmaauto.android.domain.DecimalInputError
import com.pharmaauto.android.domain.DecimalInputRules
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ConnectorReviewCompatibilityTest {
    @Test
    fun legacyQuantityOutsideCurrentBoundsLoadsAsEditableInvalidRawInput() {
        val rawRevision = fixture("legacy-review-v1-out-of-range-decimal.json")

        val (_, state) = parseReview(rawRevision)

        val line = state.lines.single()
        val rawQuantity = line.postingLines.single().quantity
        assertEquals("1000000000000", rawQuantity)
        assertEquals(rawQuantity, line.requiredQuantity)
        val currentValidation = DecimalInputRules.decimal(rawQuantity)
        assertFalse(currentValidation.isValid)
        assertEquals(DecimalInputError.TooManyIntegerDigits, currentValidation.error)

        val correctedLine = line.copy(
            postingLines = listOf(
                line.postingLines.single().copy(quantity = "999999999999"),
                line.postingLines.single().copy(
                    postingLineId = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa6",
                    splitIndex = 2,
                    postingSequence = 2,
                    quantity = "1"
                )
            )
        )
        assertEquals(null, validateLine(correctedLine))
    }

    @Test
    fun legacyCompatibilityDoesNotPermitExponentNotation() {
        val rawRevision = fixture("legacy-review-v1-out-of-range-decimal.json")
            .replace("1000000000000", "1e3")

        val failure = runCatching { parseReview(rawRevision) }.exceptionOrNull()

        assertTrue(failure is IllegalStateException)
        assertTrue(failure?.message?.contains("invalid legacy quantity") == true)
    }

    private fun fixture(name: String): String = requireNotNull(
        javaClass.classLoader?.getResource("fixtures/$name")
    ).readText()
}
