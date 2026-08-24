package com.pharmaauto.android.domain

import java.math.BigDecimal

enum class DecimalInputError {
    Required,
    TooLong,
    GroupingSeparatorNotAllowed,
    SignNotAllowed,
    ExponentNotAllowed,
    InvalidCharacter,
    MultipleDecimalSeparators,
    MissingIntegerDigits,
    MissingFractionDigits,
    LeadingZero,
    TooManyIntegerDigits,
    TooManyFractionDigits,
    PercentageOutOfRange
}

data class DecimalInputState(
    val rawText: String,
    val canonicalText: String?,
    val value: BigDecimal?,
    val error: DecimalInputError?
) {
    val isValid: Boolean
        get() = error == null
}

/**
 * Validates operator-entered decimal text without rewriting the text shown in the field.
 *
 * The Connector contracts accept unsigned ASCII decimal strings without grouping, with up to
 * twelve integer and six fractional digits for amounts and quantities (DECIMAL(18,6)), and up to
 * four fractional digits for percentages in the 0..100 range. Android also accepts Arabic-Indic
 * digits and the Arabic decimal separator, but converts them to the contract representation only
 * at calculation or serialization boundaries.
 */
object DecimalInputRules {
    const val MaximumInputLength = 19
    const val DecimalMaximumIntegerDigits = 12
    const val DecimalMaximumFractionDigits = 6
    const val PercentageMaximumIntegerDigits = 3
    const val PercentageMaximumFractionDigits = 4

    fun decimal(rawText: String): DecimalInputState = parse(
        rawText = rawText,
        maximumIntegerDigits = DecimalMaximumIntegerDigits,
        maximumFractionDigits = DecimalMaximumFractionDigits,
        maximumValue = null
    )

    fun percentage(rawText: String): DecimalInputState = parse(
        rawText = rawText,
        maximumIntegerDigits = PercentageMaximumIntegerDigits,
        maximumFractionDigits = PercentageMaximumFractionDigits,
        maximumValue = BigDecimal("100")
    )

    private fun parse(
        rawText: String,
        maximumIntegerDigits: Int,
        maximumFractionDigits: Int,
        maximumValue: BigDecimal?
    ): DecimalInputState {
        if (rawText.isEmpty()) return rawText.invalid(DecimalInputError.Required)
        if (rawText.length > MaximumInputLength) {
            return rawText.invalid(DecimalInputError.TooLong)
        }

        val canonical = StringBuilder(rawText.length)
        var decimalSeparatorSeen = false
        rawText.forEach { character ->
            val digit = character.asciiDigitOrNull()
            when {
                digit != null -> canonical.append(digit)
                character == '.' || character == ArabicDecimalSeparator -> {
                    if (decimalSeparatorSeen) {
                        return rawText.invalid(DecimalInputError.MultipleDecimalSeparators)
                    }
                    canonical.append('.')
                    decimalSeparatorSeen = true
                }
                character == ',' || character == ArabicThousandsSeparator ||
                    character == '_' || character == NonBreakingSpace ||
                    character == NarrowNonBreakingSpace || character.isWhitespace() -> {
                    return rawText.invalid(DecimalInputError.GroupingSeparatorNotAllowed)
                }
                character == '+' || character == '-' || character == UnicodeMinus -> {
                    return rawText.invalid(DecimalInputError.SignNotAllowed)
                }
                character == 'e' || character == 'E' -> {
                    return rawText.invalid(DecimalInputError.ExponentNotAllowed)
                }
                else -> return rawText.invalid(DecimalInputError.InvalidCharacter)
            }
        }

        val canonicalText = canonical.toString()
        if (canonicalText.startsWith('.')) {
            return rawText.invalid(DecimalInputError.MissingIntegerDigits)
        }
        if (canonicalText.endsWith('.')) {
            return rawText.invalid(DecimalInputError.MissingFractionDigits)
        }

        val integerPart = canonicalText.substringBefore('.')
        val fractionalPart = canonicalText.substringAfter('.', missingDelimiterValue = "")
        if (integerPart.length > 1 && integerPart.startsWith('0')) {
            return rawText.invalid(DecimalInputError.LeadingZero)
        }
        if (integerPart.length > maximumIntegerDigits) {
            return rawText.invalid(DecimalInputError.TooManyIntegerDigits)
        }
        if (fractionalPart.length > maximumFractionDigits) {
            return rawText.invalid(DecimalInputError.TooManyFractionDigits)
        }

        val value = canonicalText.toBigDecimalOrNull()
            ?: return rawText.invalid(DecimalInputError.InvalidCharacter)
        if (maximumValue != null && value > maximumValue) {
            return rawText.invalid(DecimalInputError.PercentageOutOfRange)
        }
        return DecimalInputState(
            rawText = rawText,
            canonicalText = canonicalText,
            value = value,
            error = null
        )
    }

    private fun String.invalid(error: DecimalInputError): DecimalInputState = DecimalInputState(
        rawText = this,
        canonicalText = null,
        value = null,
        error = error
    )

    private fun Char.asciiDigitOrNull(): Char? = when (this) {
        in '0'..'9' -> this
        in '\u0660'..'\u0669' -> '0' + (code - '\u0660'.code)
        in '\u06F0'..'\u06F9' -> '0' + (code - '\u06F0'.code)
        else -> null
    }

    private const val ArabicDecimalSeparator = '\u066B'
    private const val ArabicThousandsSeparator = '\u066C'
    private const val NonBreakingSpace = '\u00A0'
    private const val NarrowNonBreakingSpace = '\u202F'
    private const val UnicodeMinus = '\u2212'
}
