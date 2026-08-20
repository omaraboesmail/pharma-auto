/*
THESIS: One invoice item is a linear evidence-to-confirmation task; this refuses a dense all-lines editor.
OWN-WORLD: Restrained Material 3, white and cool surfaces, forest-green actions, pale-green confirmed values, thin dividers, and 8–12 dp shapes.
 STORY: The operator compares OCR values, confirms prices, assigns every box to an expiry, reviews totals, and completes a non-persistent prototype check.
 FIRST VIEWPORT: Review header, previous/current/next line navigation, OCR comparison, stacked expiry rows, and one anchored expandable totals/finish-review surface.
FORM: Approved progressive previous/next composition, option three, seed e2b2e110.
FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, and DESIGN.md
*/
package com.pharmaauto.android.ui

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.tween
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.automirrored.filled.List
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.KeyboardArrowUp
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import androidx.lifecycle.viewmodel.compose.viewModel
import com.pharmaauto.android.R
import java.math.BigDecimal
import java.text.NumberFormat
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle
import java.util.Locale

@Composable
fun InvoiceReviewRoute(viewModel: InvoiceReviewViewModel = viewModel()) {
    val state = viewModel.uiState
    val snackbarHostState = remember { SnackbarHostState() }
    val message = state.message?.let { currentMessage ->
        stringResource(
            when (currentMessage) {
                ReviewMessage.PrototypeReviewComplete -> R.string.review_complete_prototype
                ReviewMessage.ReviewRemainingLine -> R.string.review_remaining_line
                ReviewMessage.InvalidCommercial -> R.string.invalid_commercial
                ReviewMessage.InvalidExpiry -> R.string.invalid_expiry
            }
        )
    }

    LaunchedEffect(state.message, message) {
        if (message != null) {
            snackbarHostState.showSnackbar(message)
            viewModel.consumeMessage()
        }
    }

    InvoiceReviewScreen(
        state = state,
        snackbarHostState = snackbarHostState,
        onPreviousLine = viewModel::previousLine,
        onNextLine = viewModel::nextLine,
        onToggleOcr = viewModel::toggleOcrEvidence,
        onCommercialChange = viewModel::updateCommercial,
        onQuantityChange = viewModel::updateExpiryQuantity,
        onExpiryDateChange = viewModel::updateExpiryDate,
        onSplitExpiry = viewModel::splitExpiry,
        onAddExpiry = viewModel::addExpiry,
        onRemoveExpiry = viewModel::removeExpiry,
        onToggleTotals = viewModel::toggleTotals,
        onFinishReview = viewModel::finishReview
    )
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun InvoiceReviewScreen(
    state: InvoiceReviewUiState,
    snackbarHostState: SnackbarHostState,
    onPreviousLine: () -> Unit,
    onNextLine: () -> Unit,
    onToggleOcr: () -> Unit,
    onCommercialChange: (CommercialField, String) -> Unit,
    onQuantityChange: (Int, String) -> Unit,
    onExpiryDateChange: (Int, LocalDate) -> Unit,
    onSplitExpiry: (Int) -> Unit,
    onAddExpiry: () -> Unit,
    onRemoveExpiry: (Int) -> Unit,
    onToggleTotals: () -> Unit,
    onFinishReview: () -> Unit
) {
    var datePickerExpiryIndex by remember(state.currentLine.id) { mutableStateOf<Int?>(null) }
    var lastScrolledLineId by remember { mutableStateOf<String?>(null) }
    val listState = rememberLazyListState()

    LaunchedEffect(state.currentLine.id, state.message) {
        val targetIndex = when (state.message) {
            ReviewMessage.InvalidCommercial -> 3
            ReviewMessage.InvalidExpiry -> 4
            else -> if (lastScrolledLineId != state.currentLine.id) 0 else null
        }
        if (targetIndex != null) {
            listState.animateScrollToItem(targetIndex)
        }
        lastScrolledLineId = state.currentLine.id
    }

    Scaffold(
        modifier = Modifier.testTag("invoice-review-e2b2e110"),
        contentWindowInsets = WindowInsets.safeDrawing,
        topBar = { PharmaAutoTopBar() },
        snackbarHost = { SnackbarHost(snackbarHostState) },
        bottomBar = {
            InvoiceTotalsBar(
                state = state,
                onToggleTotals = onToggleTotals,
                onFinishReview = onFinishReview
            )
        }
    ) { innerPadding ->
        LazyColumn(
            state = listState,
            modifier = Modifier
                .fillMaxSize()
                .padding(innerPadding),
            contentPadding = PaddingValues(bottom = 24.dp)
        ) {
            item {
                ConstrainedContent {
                    ReviewProgressHeader(state)
                }
            }
            item {
                ConstrainedContent {
                    LineNavigation(
                        state = state,
                        onPreviousLine = onPreviousLine,
                        onNextLine = onNextLine
                    )
                }
            }
            item {
                ConstrainedContent {
                    OcrEvidenceHeader(
                        state = state,
                        onToggleOcr = onToggleOcr
                    )
                }
            }
            item {
                ConstrainedContent {
                    CommercialEvidenceEditor(
                        line = state.currentLine,
                        onCommercialChange = onCommercialChange
                    )
                }
            }
            item {
                ConstrainedContent {
                    ExpiryEditor(
                        line = state.currentLine,
                        onQuantityChange = onQuantityChange,
                        onDateClick = { datePickerExpiryIndex = it },
                        onSplitExpiry = onSplitExpiry,
                        onRemoveExpiry = onRemoveExpiry,
                        onAddExpiry = onAddExpiry
                    )
                }
            }
        }
    }

    datePickerExpiryIndex?.let { expiryIndex ->
        key(state.currentLine.id, expiryIndex) {
            ExpiryDateDialog(
                currentDate = state.currentLine.expiries[expiryIndex].expiryDate,
                onDismiss = { datePickerExpiryIndex = null },
                onConfirm = { date ->
                    onExpiryDateChange(expiryIndex, date)
                    datePickerExpiryIndex = null
                }
            )
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun PharmaAutoTopBar() {
    TopAppBar(
        title = {
            Text(
                text = stringResource(R.string.app_name),
                style = MaterialTheme.typography.titleLarge,
                fontWeight = FontWeight.SemiBold
            )
        },
        colors = TopAppBarDefaults.topAppBarColors(
            containerColor = MaterialTheme.colorScheme.surface
        )
    )
}

@Composable
private fun ReviewProgressHeader(state: InvoiceReviewUiState) {
    val progress = if (state.lines.isEmpty()) {
        0f
    } else {
        state.reviewedLineCount.toFloat() / state.lines.size.toFloat()
    }
    val progressDescription = pluralStringResource(
        R.plurals.review_progress_description,
        state.reviewedLineCount,
        state.reviewedLineCount,
        state.lines.size
    )

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(MaterialTheme.colorScheme.surfaceVariant)
            .padding(horizontal = 20.dp, vertical = 18.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(14.dp)
    ) {
        Surface(
            modifier = Modifier.size(48.dp),
            shape = CircleShape,
            color = MaterialTheme.colorScheme.primaryContainer
        ) {
            Box(contentAlignment = Alignment.Center) {
                Icon(
                    imageVector = Icons.AutoMirrored.Filled.List,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.onPrimaryContainer
                )
            }
        }
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = stringResource(R.string.invoice_review),
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold
            )
            Text(
                text = pluralStringResource(
                    R.plurals.lines_checked,
                    state.reviewedLineCount,
                    state.reviewedLineCount,
                    state.lines.size
                ),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.primary
            )
        }
        Column(
            modifier = Modifier.widthIn(min = 104.dp, max = 132.dp),
            horizontalAlignment = Alignment.End
        ) {
            Text(
                text = stringResource(R.string.percent_value, (progress * 100).toInt()),
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Spacer(Modifier.height(6.dp))
            LinearProgressIndicator(
                progress = { progress },
                modifier = Modifier
                    .fillMaxWidth()
                    .semantics { contentDescription = progressDescription },
                color = MaterialTheme.colorScheme.primary,
                trackColor = MaterialTheme.colorScheme.outlineVariant
            )
        }
    }
}

@Composable
private fun LineNavigation(
    state: InvoiceReviewUiState,
    onPreviousLine: () -> Unit,
    onNextLine: () -> Unit
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 8.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        TextButton(
            modifier = Modifier.weight(1f).heightIn(min = 48.dp),
            onClick = onPreviousLine,
            enabled = state.currentLineIndex > 0
        ) {
            Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = null)
            Spacer(Modifier.width(6.dp))
            Text(stringResource(R.string.previous))
        }
        Column(
            modifier = Modifier.weight(1f),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text(
                text = stringResource(R.string.line_number, state.currentLineIndex + 1),
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold
            )
            Text(
                text = stringResource(
                    R.string.item_reference,
                    isolateForDisplay(state.currentLine.itemReference)
                ),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
        TextButton(
            modifier = Modifier.weight(1f).heightIn(min = 48.dp),
            onClick = onNextLine,
            enabled = state.currentLineIndex < state.lines.lastIndex
        ) {
            Text(stringResource(R.string.next))
            Spacer(Modifier.width(6.dp))
            Icon(Icons.AutoMirrored.Filled.ArrowForward, contentDescription = null)
        }
    }
    HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
}

@Composable
private fun OcrEvidenceHeader(state: InvoiceReviewUiState, onToggleOcr: () -> Unit) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 20.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = stringResource(
                    R.string.item_reference,
                    isolateForDisplay(state.currentLine.itemReference)
                ),
                style = MaterialTheme.typography.bodyLarge,
                fontWeight = FontWeight.Medium
            )
            Text(
                text = stringResource(R.string.synthetic_example),
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
        TextButton(onClick = onToggleOcr, modifier = Modifier.heightIn(min = 48.dp)) {
            Text(
                stringResource(
                    if (state.showOcrEvidence) R.string.hide_ocr else R.string.view_ocr
                )
            )
            Spacer(Modifier.width(8.dp))
            Icon(Icons.AutoMirrored.Filled.ArrowForward, contentDescription = null)
        }
    }
    AnimatedVisibility(visible = state.showOcrEvidence) {
        Surface(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 20.dp, vertical = 4.dp),
            shape = MaterialTheme.shapes.medium,
            color = MaterialTheme.colorScheme.tertiaryContainer
        ) {
            Row(
                modifier = Modifier.padding(16.dp),
                horizontalArrangement = Arrangement.spacedBy(12.dp),
                verticalAlignment = Alignment.Top
            ) {
                Icon(
                    imageVector = Icons.Filled.Warning,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.onTertiaryContainer
                )
                Column {
                    Text(
                        text = stringResource(R.string.synthetic_ocr_evidence),
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.SemiBold
                    )
                    Text(
                        text = stringResource(R.string.ocr_evidence_description),
                        style = MaterialTheme.typography.bodyMedium
                    )
                }
            }
        }
    }
}

@Composable
private fun CommercialEvidenceEditor(
    line: InvoiceLineDraft,
    onCommercialChange: (CommercialField, String) -> Unit
) {
    val validation = InvoiceReviewRules.validate(line)
    Column(modifier = Modifier.fillMaxWidth()) {
        SectionHeading(stringResource(R.string.prices_and_discounts))
        EvidenceColumnHeader()
        CommercialEvidenceRow(
            label = stringResource(R.string.purchase_unit_price),
            sourceValue = moneyLabel(line.evidence.purchaseUnitPrice),
            confirmedValue = line.confirmed.purchaseUnitPrice,
            isPercentage = false,
            isError = !isValidMoney(line.confirmed.purchaseUnitPrice),
            onValueChange = { onCommercialChange(CommercialField.PurchaseUnitPrice, it) }
        )
        CommercialEvidenceRow(
            label = stringResource(R.string.discount_one),
            sourceValue = isolateForDisplay(line.evidence.discountOne),
            confirmedValue = line.confirmed.discountOne,
            isPercentage = true,
            isError = !isValidPercentage(line.confirmed.discountOne),
            onValueChange = { onCommercialChange(CommercialField.DiscountOne, it) }
        )
        CommercialEvidenceRow(
            label = stringResource(R.string.discount_two),
            sourceValue = isolateForDisplay(line.evidence.discountTwo),
            confirmedValue = line.confirmed.discountTwo,
            isPercentage = true,
            isError = !isValidPercentage(line.confirmed.discountTwo),
            onValueChange = { onCommercialChange(CommercialField.DiscountTwo, it) }
        )
        CommercialEvidenceRow(
            label = stringResource(R.string.selling_price_per_box),
            sourceValue = moneyLabel(line.evidence.sellingUnitPrice),
            confirmedValue = line.confirmed.sellingUnitPrice,
            isPercentage = false,
            isError = !isValidMoney(line.confirmed.sellingUnitPrice),
            onValueChange = { onCommercialChange(CommercialField.SellingUnitPrice, it) },
            showDivider = false
        )
        if (!validation.commercialValid) {
            Text(
                text = stringResource(R.string.invalid_commercial),
                modifier = Modifier.padding(horizontal = 20.dp, vertical = 8.dp),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.error
            )
        }
    }
}

@Composable
private fun EvidenceColumnHeader() {
    Row(modifier = Modifier.fillMaxWidth()) {
        Box(
            modifier = Modifier
                .weight(1f)
                .background(MaterialTheme.colorScheme.surfaceVariant)
                .padding(horizontal = 20.dp, vertical = 12.dp)
        ) {
            Text(
                text = stringResource(R.string.ocr_source),
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
        Box(
            modifier = Modifier
                .weight(1f)
                .background(MaterialTheme.colorScheme.secondaryContainer)
                .padding(horizontal = 20.dp, vertical = 12.dp)
        ) {
            Text(
                text = stringResource(R.string.confirmed),
                style = MaterialTheme.typography.labelLarge,
                color = MaterialTheme.colorScheme.onSecondaryContainer
            )
        }
    }
    HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
}

@Composable
private fun CommercialEvidenceRow(
    label: String,
    sourceValue: String,
    confirmedValue: String,
    isPercentage: Boolean,
    isError: Boolean,
    onValueChange: (String) -> Unit,
    showDivider: Boolean = true
) {
    val confirmedDescription = stringResource(R.string.confirmed_value_description, label)
    BoxWithConstraints(modifier = Modifier.fillMaxWidth()) {
        val stackFields = maxWidth < 400.dp
        if (stackFields) {
            Column(modifier = Modifier.fillMaxWidth()) {
                SourceValue(
                    label = label,
                    sourceValue = sourceValue,
                    modifier = Modifier.fillMaxWidth()
                )
                ConfirmedValueField(
                    value = confirmedValue,
                    isPercentage = isPercentage,
                    isError = isError,
                    contentDescription = confirmedDescription,
                    onValueChange = onValueChange,
                    modifier = Modifier
                        .fillMaxWidth()
                        .background(MaterialTheme.colorScheme.secondaryContainer)
                        .padding(horizontal = 20.dp, vertical = 10.dp)
                )
            }
        } else {
            Row(modifier = Modifier.fillMaxWidth()) {
                SourceValue(
                    label = label,
                    sourceValue = sourceValue,
                    modifier = Modifier.weight(1f)
                )
                ConfirmedValueField(
                    value = confirmedValue,
                    isPercentage = isPercentage,
                    isError = isError,
                    contentDescription = confirmedDescription,
                    onValueChange = onValueChange,
                    modifier = Modifier
                        .weight(1f)
                        .background(MaterialTheme.colorScheme.secondaryContainer)
                        .padding(horizontal = 14.dp, vertical = 10.dp)
                )
            }
        }
    }
    if (showDivider) {
        HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
    }
}

@Composable
private fun SourceValue(
    label: String,
    sourceValue: String,
    modifier: Modifier
) {
    Row(
        modifier = modifier
            .background(MaterialTheme.colorScheme.surface)
            .padding(horizontal = 20.dp, vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        Column {
            Text(
                text = label,
                style = MaterialTheme.typography.bodyMedium,
                fontWeight = FontWeight.Medium
            )
            Text(
                text = sourceValue,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
    }
}

@Composable
private fun ConfirmedValueField(
    value: String,
    isPercentage: Boolean,
    isError: Boolean,
    contentDescription: String,
    onValueChange: (String) -> Unit,
    modifier: Modifier
) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        modifier = modifier
            .heightIn(min = 64.dp)
            .semantics { this.contentDescription = contentDescription },
        singleLine = true,
        isError = isError,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal),
        prefix = if (isPercentage) null else {
            { Text(stringResource(R.string.currency_egp)) }
        },
        suffix = if (isPercentage) {
            { Text("%") }
        } else {
            null
        },
        trailingIcon = {
            Icon(
                imageVector = Icons.Filled.Edit,
                contentDescription = null,
                tint = if (isError) {
                    MaterialTheme.colorScheme.error
                } else {
                    MaterialTheme.colorScheme.primary
                }
            )
        }
    )
}

@Composable
private fun ExpiryEditor(
    line: InvoiceLineDraft,
    onQuantityChange: (Int, String) -> Unit,
    onDateClick: (Int) -> Unit,
    onSplitExpiry: (Int) -> Unit,
    onRemoveExpiry: (Int) -> Unit,
    onAddExpiry: () -> Unit
) {
    val validation = InvoiceReviewRules.validate(line)
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(bottom = 12.dp)
    ) {
        SectionHeading(stringResource(R.string.quantity_and_expiry))
        line.expiries.forEachIndexed { index, expiry ->
            ExpiryRow(
                expiry = expiry,
                expiryIndex = index,
                showSplit = index == 0,
                showRemove = line.expiries.size > 1 && index > 0,
                onQuantityChange = { onQuantityChange(index, it) },
                onDateClick = { onDateClick(index) },
                onSplit = { onSplitExpiry(index) },
                onRemove = { onRemoveExpiry(index) }
            )
            if (index < line.expiries.lastIndex) {
                HorizontalDivider(
                    modifier = Modifier.padding(horizontal = 20.dp),
                    color = MaterialTheme.colorScheme.outlineVariant
                )
            }
        }
        OutlinedButton(
            onClick = onAddExpiry,
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 20.dp, vertical = 12.dp)
                .heightIn(min = 52.dp)
        ) {
            Icon(Icons.Filled.Add, contentDescription = null)
            Spacer(Modifier.width(8.dp))
            Text(stringResource(R.string.add_another_expiry))
        }
        Text(
            text = when {
                validation.expiryValid -> stringResource(
                    R.string.assigned_quantity,
                    displayDecimal(validation.assignedQuantity),
                    displayDecimal(validation.requiredQuantity),
                    boxUnit(validation.requiredQuantity)
                )
                !validation.expiryDatesComplete -> stringResource(R.string.expiry_date_missing)
                else -> stringResource(
                    R.string.quantity_mismatch,
                    displayDecimal(validation.requiredQuantity),
                    boxUnit(validation.requiredQuantity)
                )
            },
            modifier = Modifier.padding(horizontal = 20.dp),
            style = MaterialTheme.typography.bodySmall,
            color = if (validation.expiryValid) {
                MaterialTheme.colorScheme.primary
            } else {
                MaterialTheme.colorScheme.error
            }
        )
    }
}

@Composable
private fun ExpiryRow(
    expiry: ExpiryDraft,
    expiryIndex: Int,
    showSplit: Boolean,
    showRemove: Boolean,
    onQuantityChange: (String) -> Unit,
    onDateClick: () -> Unit,
    onSplit: () -> Unit,
    onRemove: () -> Unit
) {
    val quantity = InvoiceReviewRules.decimalOrNull(expiry.quantity)
    val quantityError = quantity == null || quantity <= BigDecimal.ZERO
    val splitDescription = stringResource(
        R.string.split_expiry_description,
        (expiryIndex + 1).toString()
    )
    val removeDescription = stringResource(
        R.string.remove_expiry_description,
        (expiryIndex + 1).toString()
    )

    BoxWithConstraints(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 20.dp, vertical = 12.dp)
            .clip(MaterialTheme.shapes.medium)
            .background(MaterialTheme.colorScheme.surfaceVariant)
            .padding(14.dp)
    ) {
        val stackFields = maxWidth < 420.dp
        if (stackFields) {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text(
                    text = stringResource(R.string.expiry_number, expiryIndex + 1),
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold
                )
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    QuantityField(
                        value = expiry.quantity,
                        expiryIndex = expiryIndex,
                        isError = quantityError,
                        onValueChange = onQuantityChange,
                        modifier = Modifier.weight(1f)
                    )
                    ExpiryDateButton(
                        date = expiry.expiryDate,
                        expiryIndex = expiryIndex,
                        onClick = onDateClick,
                        modifier = Modifier.weight(1.35f)
                    )
                }
                ExpiryActions(
                    showSplit = showSplit,
                    showRemove = showRemove,
                    splitDescription = splitDescription,
                    removeDescription = removeDescription,
                    onSplit = onSplit,
                    onRemove = onRemove,
                    modifier = Modifier.fillMaxWidth()
                )
            }
        } else {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                Text(
                    text = stringResource(R.string.expiry_number, expiryIndex + 1),
                    modifier = Modifier.width(76.dp),
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold
                )
                QuantityField(
                    value = expiry.quantity,
                    expiryIndex = expiryIndex,
                    isError = quantityError,
                    onValueChange = onQuantityChange,
                    modifier = Modifier.weight(0.8f)
                )
                ExpiryDateButton(
                    date = expiry.expiryDate,
                    expiryIndex = expiryIndex,
                    onClick = onDateClick,
                    modifier = Modifier.weight(1.2f)
                )
                ExpiryActions(
                    showSplit = showSplit,
                    showRemove = showRemove,
                    splitDescription = splitDescription,
                    removeDescription = removeDescription,
                    onSplit = onSplit,
                    onRemove = onRemove,
                    modifier = Modifier.widthIn(min = 104.dp)
                )
            }
        }
    }
}

@Composable
private fun QuantityField(
    value: String,
    expiryIndex: Int,
    isError: Boolean,
    onValueChange: (String) -> Unit,
    modifier: Modifier
) {
    val quantityDescription = stringResource(
        R.string.expiry_quantity_description,
        (expiryIndex + 1).toString()
    )
    val quantity = InvoiceReviewRules.decimalOrNull(value) ?: BigDecimal.ZERO
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        modifier = modifier
            .heightIn(min = 64.dp)
            .semantics { contentDescription = quantityDescription },
        label = { Text(stringResource(R.string.quantity)) },
        suffix = { Text(boxUnit(quantity)) },
        singleLine = true,
        isError = isError,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Decimal)
    )
}

@Composable
private fun ExpiryDateButton(
    date: LocalDate?,
    expiryIndex: Int,
    onClick: () -> Unit,
    modifier: Modifier
) {
    val error = date == null
    val dateLabel = date?.format(localizedDateFormatter())
        ?: stringResource(R.string.date_not_set)
    val dateDescription = stringResource(
        R.string.expiry_date_description,
        (expiryIndex + 1).toString(),
        dateLabel
    )
    OutlinedButton(
        onClick = onClick,
        modifier = modifier
            .heightIn(min = 64.dp)
            .semantics { contentDescription = dateDescription },
        shape = MaterialTheme.shapes.small,
        border = BorderStroke(
            width = 1.dp,
            color = if (error) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.outline
        ),
        colors = ButtonDefaults.outlinedButtonColors(
            contentColor = if (error) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurface
        ),
        contentPadding = PaddingValues(horizontal = 12.dp, vertical = 8.dp)
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = stringResource(R.string.expiry_date),
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Text(
                text = dateLabel,
                style = MaterialTheme.typography.bodyMedium,
                maxLines = 1
            )
        }
        Icon(Icons.Filled.Edit, contentDescription = null)
    }
}

@Composable
private fun ExpiryActions(
    showSplit: Boolean,
    showRemove: Boolean,
    splitDescription: String,
    removeDescription: String,
    onSplit: () -> Unit,
    onRemove: () -> Unit,
    modifier: Modifier
) {
    Row(
        modifier = modifier,
        horizontalArrangement = Arrangement.End,
        verticalAlignment = Alignment.CenterVertically
    ) {
        if (showSplit) {
            TextButton(
                onClick = onSplit,
                modifier = Modifier
                    .heightIn(min = 48.dp)
                    .semantics { contentDescription = splitDescription }
            ) {
                Text(stringResource(R.string.split))
            }
        }
        if (showRemove) {
            TextButton(
                onClick = onRemove,
                modifier = Modifier
                    .heightIn(min = 48.dp)
                    .semantics { contentDescription = removeDescription },
                colors = ButtonDefaults.textButtonColors(
                    contentColor = MaterialTheme.colorScheme.error
                )
            ) {
                Icon(Icons.Filled.Delete, contentDescription = null)
                Spacer(Modifier.width(4.dp))
                Text(stringResource(R.string.remove))
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ExpiryDateDialog(
    currentDate: LocalDate?,
    onDismiss: () -> Unit,
    onConfirm: (LocalDate) -> Unit
) {
    val initialMillis = currentDate
        ?.atStartOfDay(ZoneOffset.UTC)
        ?.toInstant()
        ?.toEpochMilli()
    val datePickerState = rememberDatePickerState(initialSelectedDateMillis = initialMillis)

    DatePickerDialog(
        onDismissRequest = onDismiss,
        confirmButton = {
            TextButton(
                onClick = {
                    datePickerState.selectedDateMillis?.let { selectedMillis ->
                        onConfirm(
                            Instant.ofEpochMilli(selectedMillis)
                                .atZone(ZoneOffset.UTC)
                                .toLocalDate()
                        )
                    }
                },
                enabled = datePickerState.selectedDateMillis != null
            ) {
                Text(stringResource(R.string.confirm))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(R.string.dismiss))
            }
        }
    ) {
        DatePicker(
            state = datePickerState,
            title = {
                Text(
                    text = stringResource(R.string.choose_expiry_date),
                    modifier = Modifier.padding(start = 24.dp, end = 24.dp, top = 16.dp),
                    style = MaterialTheme.typography.titleLarge
                )
            }
        )
    }
}

@Composable
private fun InvoiceTotalsBar(
    state: InvoiceReviewUiState,
    onToggleTotals: () -> Unit,
    onFinishReview: () -> Unit
) {
    val totals = InvoiceReviewRules.totals(state.lines)
    val expandDescription = stringResource(
        if (state.totalsExpanded) R.string.collapse_totals else R.string.expand_totals
    )

    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .animateContentSize(animationSpec = tween(durationMillis = 220))
            .imePadding()
            .navigationBarsPadding(),
        color = MaterialTheme.colorScheme.surface,
        shadowElevation = 8.dp,
        tonalElevation = 3.dp
    ) {
        Column(modifier = Modifier.fillMaxWidth()) {
            AnimatedVisibility(visible = state.totalsExpanded) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 20.dp, vertical = 14.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    Text(
                        text = stringResource(R.string.invoice_details),
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.SemiBold
                    )
                    TotalDetailRow(R.string.gross_purchase, totals?.grossPurchase)
                    TotalDetailRow(R.string.discount_savings, totals?.discountSavings)
                    TotalDetailRow(R.string.expected_selling_total, totals?.expectedSelling)
                    HorizontalDivider(color = MaterialTheme.colorScheme.outlineVariant)
                }
            }
            BoxWithConstraints(modifier = Modifier.fillMaxWidth()) {
                if (maxWidth < 400.dp) {
                    Column(
                        modifier = Modifier.padding(horizontal = 16.dp, vertical = 12.dp),
                        verticalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            InvoiceNetTotal(totals?.netPurchase, Modifier.weight(1f))
                            IconButton(
                                onClick = onToggleTotals,
                                modifier = Modifier.semantics {
                                    contentDescription = expandDescription
                                }
                            ) {
                                Icon(
                                    if (state.totalsExpanded) {
                                        Icons.Filled.KeyboardArrowDown
                                    } else {
                                        Icons.Filled.KeyboardArrowUp
                                    },
                                    contentDescription = null
                                )
                            }
                        }
                        FinishReviewButton(onFinishReview, Modifier.fillMaxWidth())
                    }
                } else {
                    Row(
                        modifier = Modifier.padding(horizontal = 16.dp, vertical = 12.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(14.dp)
                    ) {
                        InvoiceNetTotal(totals?.netPurchase, Modifier.weight(0.8f))
                        FinishReviewButton(onFinishReview, Modifier.weight(1.2f))
                        IconButton(
                            onClick = onToggleTotals,
                            modifier = Modifier.semantics {
                                contentDescription = expandDescription
                            }
                        ) {
                            Icon(
                                if (state.totalsExpanded) {
                                    Icons.Filled.KeyboardArrowDown
                                } else {
                                    Icons.Filled.KeyboardArrowUp
                                },
                                contentDescription = null
                            )
                        }
                    }
                }
            }
            Text(
                text = stringResource(R.string.prototype_review_note),
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(start = 16.dp, end = 16.dp, bottom = 12.dp),
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center
            )
        }
    }
}

@Composable
private fun InvoiceNetTotal(amount: BigDecimal?, modifier: Modifier) {
    Column(modifier = modifier) {
        Text(
            text = stringResource(R.string.invoice_net_total),
            style = MaterialTheme.typography.labelMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )
        Text(
            text = amount?.let { moneyLabel(displayMoney(it)) }
                ?: stringResource(R.string.total_not_ready),
            style = if (amount == null) {
                MaterialTheme.typography.bodyMedium
            } else {
                MaterialTheme.typography.titleLarge
            },
            fontWeight = if (amount == null) FontWeight.Medium else FontWeight.Bold,
            color = if (amount == null) {
                MaterialTheme.colorScheme.error
            } else {
                MaterialTheme.colorScheme.onSurface
            },
            maxLines = 1
        )
    }
}

@Composable
private fun FinishReviewButton(onClick: () -> Unit, modifier: Modifier) {
    Button(
        onClick = onClick,
        modifier = modifier.heightIn(min = 56.dp),
        shape = MaterialTheme.shapes.small
    ) {
        Text(
            text = stringResource(R.string.finish_review),
            style = MaterialTheme.typography.labelLarge
        )
        Spacer(Modifier.width(8.dp))
        Icon(Icons.AutoMirrored.Filled.ArrowForward, contentDescription = null)
    }
}

@Composable
private fun TotalDetailRow(labelResource: Int, amount: BigDecimal?) {
    Row(modifier = Modifier.fillMaxWidth()) {
        Text(
            text = stringResource(labelResource),
            modifier = Modifier.weight(1f),
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )
        Text(
            text = amount?.let { moneyLabel(displayMoney(it)) }
                ?: stringResource(R.string.not_available),
            style = MaterialTheme.typography.bodyMedium,
            fontWeight = FontWeight.SemiBold
        )
    }
}

@Composable
private fun SectionHeading(text: String) {
    Text(
        text = text,
        modifier = Modifier.padding(start = 20.dp, end = 20.dp, top = 20.dp, bottom = 10.dp),
        style = MaterialTheme.typography.titleLarge,
        fontWeight = FontWeight.SemiBold
    )
}

@Composable
private fun ConstrainedContent(content: @Composable () -> Unit) {
    Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.TopCenter) {
        Box(modifier = Modifier.fillMaxWidth().widthIn(max = 840.dp)) {
            content()
        }
    }
}

@Composable
private fun moneyLabel(amount: String): String =
    "${stringResource(R.string.currency_egp)} ${isolateForDisplay(amount)}"

@Composable
private fun boxUnit(quantity: BigDecimal): String =
    pluralStringResource(R.plurals.unit_box, quantityPluralSelector(quantity))

private fun quantityPluralSelector(quantity: BigDecimal): Int {
    val normalized = quantity.stripTrailingZeros()
    if (normalized.scale() > 0) return 100
    return try {
        normalized.intValueExact()
    } catch (_: ArithmeticException) {
        100
    }
}

private fun localizedDateFormatter(): DateTimeFormatter =
    DateTimeFormatter.ofLocalizedDate(FormatStyle.MEDIUM).withLocale(Locale.getDefault())

private fun isValidMoney(value: String): Boolean =
    InvoiceReviewRules.decimalOrNull(value)?.let { it >= BigDecimal.ZERO } == true

private fun isValidPercentage(value: String): Boolean =
    InvoiceReviewRules.decimalOrNull(value)?.let { it in BigDecimal.ZERO..BigDecimal("100") } == true

private fun displayMoney(value: BigDecimal): String = NumberFormat.getNumberInstance().run {
    minimumFractionDigits = 2
    maximumFractionDigits = 2
    format(value)
}

private fun displayDecimal(value: BigDecimal): String = value.stripTrailingZeros().toPlainString()

private fun isolateForDisplay(value: String): String = "\u2068$value\u2069"

@Preview(name = "Approved phone", widthDp = 432, heightDp = 900, showBackground = true)
@Composable
private fun InvoiceReviewPreview() {
    PharmaAutoTheme(darkTheme = false) {
        InvoiceReviewScreen(
            state = sampleInvoiceReviewState(),
            snackbarHostState = remember { SnackbarHostState() },
            onPreviousLine = {},
            onNextLine = {},
            onToggleOcr = {},
            onCommercialChange = { _, _ -> },
            onQuantityChange = { _, _ -> },
            onExpiryDateChange = { _, _ -> },
            onSplitExpiry = {},
            onAddExpiry = {},
            onRemoveExpiry = {},
            onToggleTotals = {},
            onFinishReview = {}
        )
    }
}

@Preview(
    name = "Arabic compact",
    widthDp = 360,
    heightDp = 800,
    locale = "ar",
    showBackground = true
)
@Composable
private fun InvoiceReviewArabicPreview() {
    PharmaAutoTheme(darkTheme = false) {
        InvoiceReviewScreen(
            state = sampleInvoiceReviewState(),
            snackbarHostState = remember { SnackbarHostState() },
            onPreviousLine = {},
            onNextLine = {},
            onToggleOcr = {},
            onCommercialChange = { _, _ -> },
            onQuantityChange = { _, _ -> },
            onExpiryDateChange = { _, _ -> },
            onSplitExpiry = {},
            onAddExpiry = {},
            onRemoveExpiry = {},
            onToggleTotals = {},
            onFinishReview = {}
        )
    }
}
