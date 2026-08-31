package com.pharmaauto.android.ui

import androidx.compose.runtime.Composable
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import com.pharmaauto.android.R
import com.pharmaauto.android.domain.DecimalInputError
import com.pharmaauto.android.domain.DecimalInputRules
import java.math.BigDecimal

@Composable
internal fun decimalInputErrorText(
    rawText: String,
    isPercentage: Boolean = false,
    positiveRequired: Boolean = false
): String? {
    val input = if (isPercentage) {
        DecimalInputRules.percentage(rawText)
    } else {
        DecimalInputRules.decimal(rawText)
    }
    if (input.error == null && positiveRequired && input.value!! <= BigDecimal.ZERO) {
        return stringResource(R.string.numeric_value_must_be_positive)
    }

    val error = input.error ?: return null
    return when (error) {
        DecimalInputError.Required -> stringResource(R.string.numeric_value_required)
        DecimalInputError.TooLong -> pluralStringResource(
            R.plurals.numeric_value_too_long,
            DecimalInputRules.MaximumInputLength,
            DecimalInputRules.MaximumInputLength
        )
        DecimalInputError.GroupingSeparatorNotAllowed ->
            stringResource(R.string.numeric_grouping_not_allowed)
        DecimalInputError.SignNotAllowed -> stringResource(R.string.numeric_sign_not_allowed)
        DecimalInputError.ExponentNotAllowed ->
            stringResource(R.string.numeric_exponent_not_allowed)
        DecimalInputError.InvalidCharacter ->
            stringResource(R.string.numeric_invalid_character)
        DecimalInputError.MultipleDecimalSeparators ->
            stringResource(R.string.numeric_multiple_decimal_separators)
        DecimalInputError.MissingIntegerDigits ->
            stringResource(R.string.numeric_missing_integer_digits)
        DecimalInputError.MissingFractionDigits ->
            stringResource(R.string.numeric_missing_fraction_digits)
        DecimalInputError.LeadingZero -> stringResource(R.string.numeric_leading_zero)
        DecimalInputError.TooManyIntegerDigits -> {
            val maximumDigits = if (isPercentage) {
                DecimalInputRules.PercentageMaximumIntegerDigits
            } else {
                DecimalInputRules.DecimalMaximumIntegerDigits
            }
            pluralStringResource(
                R.plurals.numeric_too_many_integer_digits,
                maximumDigits,
                maximumDigits
            )
        }
        DecimalInputError.TooManyFractionDigits -> {
            val maximumDigits = if (isPercentage) {
                DecimalInputRules.PercentageMaximumFractionDigits
            } else {
                DecimalInputRules.DecimalMaximumFractionDigits
            }
            pluralStringResource(
                R.plurals.numeric_too_many_fraction_digits,
                maximumDigits,
                maximumDigits
            )
        }
        DecimalInputError.PercentageOutOfRange ->
            stringResource(R.string.numeric_percentage_out_of_range)
    }
}
