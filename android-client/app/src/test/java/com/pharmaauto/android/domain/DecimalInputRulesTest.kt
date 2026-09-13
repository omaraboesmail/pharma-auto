package com.pharmaauto.android.domain

import java.math.BigDecimal
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class DecimalInputRulesTest {
    @Test
    fun decimalAcceptsOnlyContractShapeAndPreservesScale() {
        val cases = listOf(
            "0" to "0",
            "1" to "1",
            "999999" to "999999",
            "0.0" to "0.0",
            "12.3400" to "12.3400",
            "999.123456" to "999.123456"
        )

        cases.forEach { (raw, canonical) ->
            val result = DecimalInputRules.decimal(raw)
            assertTrue(raw, result.isValid)
            assertEquals(raw, raw, result.rawText)
            assertEquals(raw, canonical, result.canonicalText)
            assertEquals(raw, BigDecimal(canonical), result.value)
        }
    }

    @Test
    fun arabicIndicAndEasternArabicDigitsCanonicalizeOnlyAtTheBoundary() {
        val cases = listOf(
            "٠" to "0",
            "١٢٣٫٤٥٦" to "123.456",
            "١٢٣.45" to "123.45",
            "۴۵۶٫۷۸" to "456.78"
        )

        cases.forEach { (raw, canonical) ->
            val result = DecimalInputRules.decimal(raw)
            assertTrue(raw, result.isValid)
            assertEquals(raw, raw, result.rawText)
            assertEquals(raw, canonical, result.canonicalText)
        }
    }

    @Test
    fun signsAreRejectedWithoutChangingTheEnteredText() {
        listOf("-1", "+1", "−1", "-١٢٫٥").forEach { raw ->
            assertInvalid(raw, DecimalInputError.SignNotAllowed)
        }
    }

    @Test
    fun exponentNotationIsRejectedWithoutReinterpretingIt() {
        listOf("1e3", "1E3", "١e٣", "0.1E+2").forEach { raw ->
            assertInvalid(raw, DecimalInputError.ExponentNotAllowed)
        }
    }

    @Test
    fun groupingAndAmbiguousSeparatorsAreRejected() {
        listOf(
            "1,000",
            "1 000",
            "1_000",
            "1\u00A0000",
            "1\u202F000",
            "١٬٠٠٠",
            "1.000,50"
        ).forEach { raw ->
            assertInvalid(raw, DecimalInputError.GroupingSeparatorNotAllowed)
        }
    }

    @Test
    fun emptyAndPartialValuesRemainVisibleButInvalid() {
        mapOf(
            "" to DecimalInputError.Required,
            "." to DecimalInputError.MissingIntegerDigits,
            "٫" to DecimalInputError.MissingIntegerDigits,
            "1." to DecimalInputError.MissingFractionDigits,
            "١٫" to DecimalInputError.MissingFractionDigits
        ).forEach { (raw, error) -> assertInvalid(raw, error) }
    }

    @Test
    fun malformedDecimalShapesAreRejectedRatherThanRepaired() {
        mapOf(
            ".5" to DecimalInputError.MissingIntegerDigits,
            "00" to DecimalInputError.LeadingZero,
            "01.5" to DecimalInputError.LeadingZero,
            "٠١٫٥" to DecimalInputError.LeadingZero,
            "1.2.3" to DecimalInputError.MultipleDecimalSeparators,
            "1٫2.3" to DecimalInputError.MultipleDecimalSeparators,
            "12abc" to DecimalInputError.InvalidCharacter
        ).forEach { (raw, error) -> assertInvalid(raw, error) }
    }

    @Test
    fun amountAndQuantityScaleIsLimitedToSixFractionDigits() {
        assertTrue(DecimalInputRules.decimal("1.123456").isValid)
        assertInvalid("1.1234567", DecimalInputError.TooManyFractionDigits)
        assertTrue(DecimalInputRules.decimal("١٫١٢٣٤٥٦").isValid)
        assertInvalid(
            "١٫١٢٣٤٥٦٧",
            DecimalInputError.TooManyFractionDigits
        )
    }

    @Test
    fun amountAndQuantityUseTheSharedDecimal18Scale6Range() {
        listOf("999999999999", "999999999999.999999").forEach { raw ->
            assertTrue(raw, DecimalInputRules.decimal(raw).isValid)
        }
        listOf("1000000000000", "9999999999999.0").forEach { raw ->
            assertInvalid(raw, DecimalInputError.TooManyIntegerDigits)
        }
    }

    @Test
    fun percentageUsesTheFourDigitScaleAndZeroToOneHundredContract() {
        listOf("0", "0.0000", "99.9999", "100", "100.0", "100.0000").forEach { raw ->
            assertTrue(raw, DecimalInputRules.percentage(raw).isValid)
        }
        mapOf(
            "0.00000" to DecimalInputError.TooManyFractionDigits,
            "99.99999" to DecimalInputError.TooManyFractionDigits,
            "100.0001" to DecimalInputError.PercentageOutOfRange,
            "101" to DecimalInputError.PercentageOutOfRange
        ).forEach { (raw, error) -> assertInvalid(raw, error, percentage = true) }
    }

    @Test
    fun inputLengthIsCheckedBeforeBigDecimalParsing() {
        val maximum = "9".repeat(DecimalInputRules.DecimalMaximumIntegerDigits) +
            "." + "9".repeat(DecimalInputRules.DecimalMaximumFractionDigits)
        val tooLong = "9".repeat(DecimalInputRules.MaximumInputLength + 1)

        assertEquals(DecimalInputRules.MaximumInputLength, maximum.length)
        assertTrue(DecimalInputRules.decimal(maximum).isValid)
        assertInvalid(tooLong, DecimalInputError.TooLong)
    }

    @Test
    fun everyInvalidCaseKeepsRawTextAndProducesNoContractValue() {
        val invalidValues = listOf("", "-5", "1e3", "1,000", "1.", "01", "1.1234567", "١٬٠٠٠")

        invalidValues.forEach { raw ->
            val result = DecimalInputRules.decimal(raw)
            assertFalse(raw, result.isValid)
            assertEquals(raw, raw, result.rawText)
            assertNull(raw, result.canonicalText)
            assertNull(raw, result.value)
        }
    }

    @Test
    fun everyAcceptedDecimalProducesTheSharedContractPattern() {
        val contractPattern = Regex("^(0|[1-9][0-9]{0,11})(\\.[0-9]{1,6})?$")
        val digitSets = listOf(
            "0123456789",
            "٠١٢٣٤٥٦٧٨٩",
            "۰۱۲۳۴۵۶۷۸۹"
        )

        digitSets.forEachIndexed { digitSetIndex, digits ->
            (0..9).forEach { whole ->
                (0..6).forEach { scale ->
                    val raw = buildString {
                        append(digits[whole])
                        if (scale > 0) {
                            append(if (digitSetIndex == 0) '.' else '٫')
                            repeat(scale) { index -> append(digits[(whole + index) % 10]) }
                        }
                    }
                    val result = DecimalInputRules.decimal(raw)
                    assertTrue(raw, result.isValid)
                    assertTrue(raw, contractPattern.matches(requireNotNull(result.canonicalText)))
                }
            }
        }
    }

    private fun assertInvalid(
        raw: String,
        expectedError: DecimalInputError,
        percentage: Boolean = false
    ) {
        val result = if (percentage) {
            DecimalInputRules.percentage(raw)
        } else {
            DecimalInputRules.decimal(raw)
        }
        assertFalse(raw, result.isValid)
        assertEquals(raw, raw, result.rawText)
        assertEquals(raw, expectedError, result.error)
        assertNull(raw, result.canonicalText)
        assertNull(raw, result.value)
    }
}
