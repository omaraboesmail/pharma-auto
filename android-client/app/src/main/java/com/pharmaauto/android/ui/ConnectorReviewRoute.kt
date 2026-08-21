package com.pharmaauto.android.ui

import android.app.Application
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.ArrowForward
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDirection
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.compose.viewModel
import com.pharmaauto.android.PharmaAutoApplication
import com.pharmaauto.android.R
import com.pharmaauto.android.data.PharmaAutoRepository
import com.pharmaauto.android.network.SaveRevisionRequest
import com.pharmaauto.android.network.LocalItemCandidateContract
import com.pharmaauto.android.network.LocalVendorCandidateContract
import java.math.BigDecimal
import java.math.RoundingMode
import java.time.LocalDate
import java.util.UUID
import kotlinx.coroutines.launch
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import retrofit2.HttpException

private enum class ConnectorReviewNotice {
    SelectVendor,
    SelectItem,
    InvalidCommercial,
    InvalidExpiry,
    InvalidRevision,
    SearchFailed,
    ConfirmationFailed
}

private data class VendorCandidateUi(
    val reference: String,
    val displayName: String,
    val code: String?,
    val reasonCodes: List<String>
)

private data class ItemCandidateUi(
    val reference: String,
    val displayLabel: String,
    val rawLabel: String?,
    val displayDirection: String,
    val qualityFlags: List<String>,
    val reasonCodes: List<String>,
    val hardMismatches: List<String>
)

private data class PostingLineUi(
    val postingLineId: String,
    val splitIndex: Int,
    val postingSequence: Int,
    val quantity: String,
    val expiryDate: String,
    val batch: String,
    val purchaseUnitPrice: String,
    val discountOne: String,
    val discountTwo: String,
    val sellingUnitPrice: String,
    val originalPurchaseUnitPrice: String,
    val originalDiscountOne: String,
    val originalDiscountTwo: String,
    val originalSellingUnitPrice: String
)

private data class SourceLineUi(
    val sourceLineId: String,
    val sequence: Int,
    val description: String,
    val vendorItemCode: String?,
    val strength: String?,
    val dosageForm: String?,
    val evidenceRegion: EvidenceRegion?,
    val requiredQuantity: String,
    val selectedLocalItemReference: String?,
    val candidates: List<ItemCandidateUi>,
    val postingLines: List<PostingLineUi>
)

private data class ConnectorReviewUiState(
    val revisionId: String,
    val vendorEvidence: String,
    val selectedLocalVendorReference: String?,
    val vendorCandidates: List<VendorCandidateUi>,
    val lines: List<SourceLineUi>,
    val sourcePages: List<SourcePageUi> = emptyList(),
    val vendorSearchQuery: String = "",
    val vendorSearchResults: List<VendorCandidateUi> = emptyList(),
    val itemSearchQuery: String = "",
    val itemSearchResults: List<ItemCandidateUi> = emptyList(),
    val searchBusy: Boolean = false,
    val currentLineIndex: Int = 0,
    val busy: Boolean = false,
    val notice: ConnectorReviewNotice? = null,
    val errorDetail: String? = null,
    val completed: Boolean = false
) {
    val currentLine: SourceLineUi?
        get() = lines.getOrNull(currentLineIndex)
}

@Composable
fun ConnectorReviewRoute(
    draftId: String,
    revisionJson: String,
    onBack: () -> Unit
) {
    val application = LocalContext.current.applicationContext as PharmaAutoApplication
    val factory = remember(draftId, revisionJson) {
        ConnectorReviewViewModel.factory(
            application = application,
            draftId = draftId,
            revisionJson = revisionJson,
            repository = application.repository
        )
    }
    val viewModel: ConnectorReviewViewModel = viewModel(
        key = "connector-review-$draftId",
        factory = factory
    )
    val state = viewModel.uiState
    val snackbar = remember { SnackbarHostState() }
    val notice = state.notice?.let { current ->
        stringResource(
            when (current) {
                ConnectorReviewNotice.SelectVendor -> R.string.select_local_vendor_first
                ConnectorReviewNotice.SelectItem -> R.string.select_local_item_for_every_line
                ConnectorReviewNotice.InvalidCommercial -> R.string.invalid_commercial
                ConnectorReviewNotice.InvalidExpiry -> R.string.invalid_expiry
                ConnectorReviewNotice.InvalidRevision -> R.string.invalid_connector_revision
                ConnectorReviewNotice.SearchFailed -> R.string.catalog_search_failed
                ConnectorReviewNotice.ConfirmationFailed -> R.string.review_confirmation_failed
            }
        )
    }
    LaunchedEffect(state.notice, state.errorDetail) {
        val message = listOfNotNull(notice, state.errorDetail).joinToString(" ")
        if (message.isNotBlank()) {
            snackbar.showSnackbar(message)
            viewModel.consumeNotice()
        }
    }
    LaunchedEffect(state.completed) {
        if (state.completed) onBack()
    }

    ConnectorReviewScreen(
        state = state,
        snackbar = snackbar,
        onBack = onBack,
        onSelectVendor = viewModel::selectVendor,
        onVendorSearchQueryChange = viewModel::updateVendorSearchQuery,
        onSearchVendors = viewModel::searchVendors,
        onSelectItem = viewModel::selectItem,
        onItemSearchQueryChange = viewModel::updateItemSearchQuery,
        onSearchItems = viewModel::searchItems,
        onPrevious = viewModel::previousLine,
        onNext = viewModel::nextLine,
        onPostingChange = viewModel::updatePostingLine,
        onApplyCommercialToAll = viewModel::applyCommercialToAll,
        onAddExpiry = viewModel::addExpiry,
        onRemoveExpiry = viewModel::removeExpiry,
        onConfirm = viewModel::confirmReview
    )
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun ConnectorReviewScreen(
    state: ConnectorReviewUiState,
    snackbar: SnackbarHostState,
    onBack: () -> Unit,
    onSelectVendor: (String) -> Unit,
    onVendorSearchQueryChange: (String) -> Unit,
    onSearchVendors: () -> Unit,
    onSelectItem: (String) -> Unit,
    onItemSearchQueryChange: (String) -> Unit,
    onSearchItems: () -> Unit,
    onPrevious: () -> Unit,
    onNext: () -> Unit,
    onPostingChange: (Int, PostingField, String) -> Unit,
    onApplyCommercialToAll: (Int) -> Unit,
    onAddExpiry: () -> Unit,
    onRemoveExpiry: (Int) -> Unit,
    onConfirm: () -> Unit
) {
    var evidenceTarget by remember { mutableStateOf<SourceEvidenceTarget?>(null) }
    Scaffold(
        contentWindowInsets = WindowInsets.safeDrawing,
        topBar = {
            TopAppBar(
                title = { Text(stringResource(R.string.invoice_review)) },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(
                            Icons.AutoMirrored.Filled.ArrowBack,
                            contentDescription = stringResource(R.string.back)
                        )
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.surface
                )
            )
        },
        snackbarHost = { SnackbarHost(snackbar) },
        bottomBar = {
            Surface(shadowElevation = 8.dp, tonalElevation = 3.dp) {
                Button(
                    onClick = onConfirm,
                    enabled = !state.busy && !state.searchBusy && state.lines.isNotEmpty(),
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(16.dp)
                        .heightIn(min = 56.dp)
                ) {
                    if (state.busy) {
                        CircularProgressIndicator(
                            modifier = Modifier.padding(end = 12.dp),
                            strokeWidth = 2.dp,
                            color = MaterialTheme.colorScheme.onPrimary
                        )
                    }
                    Text(stringResource(R.string.confirm_review_read_only))
                    Spacer(Modifier.width(8.dp))
                    Icon(Icons.AutoMirrored.Filled.ArrowForward, contentDescription = null)
                }
            }
        }
    ) { padding ->
        LazyColumn(
            modifier = Modifier.fillMaxSize().padding(padding),
            contentPadding = PaddingValues(bottom = 24.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp)
        ) {
            item {
                Surface(
                    modifier = Modifier.fillMaxWidth(),
                    color = MaterialTheme.colorScheme.primaryContainer
                ) {
                    Column(
                        Modifier.padding(20.dp),
                        verticalArrangement = Arrangement.spacedBy(6.dp)
                    ) {
                        Text(
                            stringResource(R.string.phase_one_confirmation_only),
                            fontWeight = FontWeight.Bold
                        )
                        Text(
                            stringResource(R.string.phase_one_no_genius_write),
                            color = MaterialTheme.colorScheme.onPrimaryContainer
                        )
                    }
                }
            }
            item {
                CandidateSectionTitle(
                    title = stringResource(R.string.local_vendor),
                    evidence = state.vendorEvidence
                )
                CatalogSearchControls(
                    query = state.vendorSearchQuery,
                    label = stringResource(R.string.search_full_local_vendor_catalog),
                    busy = state.searchBusy,
                    onQueryChange = onVendorSearchQueryChange,
                    onSearch = onSearchVendors
                )
                val visibleVendorCandidates = (
                    state.vendorCandidates + state.vendorSearchResults
                    ).distinctBy(VendorCandidateUi::reference)
                if (visibleVendorCandidates.isEmpty()) {
                    MissingCandidateNotice(stringResource(R.string.no_vendor_candidates))
                } else {
                    visibleVendorCandidates.forEach { candidate ->
                        VendorCandidateRow(
                            candidate = candidate,
                            selected = candidate.reference == state.selectedLocalVendorReference,
                            onSelect = { onSelectVendor(candidate.reference) }
                        )
                    }
                }
            }
            item { HorizontalDivider(Modifier.padding(horizontal = 20.dp)) }
            state.currentLine?.let { line ->
                item {
                    Row(
                        modifier = Modifier.fillMaxWidth().padding(horizontal = 20.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        OutlinedButton(
                            onClick = onPrevious,
                            enabled = state.currentLineIndex > 0 && !state.busy,
                            modifier = Modifier.weight(1f)
                        ) {
                            Text(stringResource(R.string.previous))
                        }
                        Text(
                            stringResource(
                                R.string.line_position,
                                state.currentLineIndex + 1,
                                state.lines.size
                            ),
                            fontWeight = FontWeight.SemiBold
                        )
                        OutlinedButton(
                            onClick = onNext,
                            enabled = state.currentLineIndex < state.lines.lastIndex && !state.busy,
                            modifier = Modifier.weight(1f)
                        ) {
                            Text(stringResource(R.string.next))
                        }
                    }
                }
                item {
                    Column(
                        Modifier.fillMaxWidth().padding(horizontal = 20.dp),
                        verticalArrangement = Arrangement.spacedBy(5.dp)
                    ) {
                        Text(line.description, style = MaterialTheme.typography.titleLarge)
                        Text(
                            stringResource(R.string.source_quantity_boxes, line.requiredQuantity),
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                        line.vendorItemCode?.let { code ->
                            Text(
                                stringResource(R.string.vendor_item_code, code),
                                color = MaterialTheme.colorScheme.onSurfaceVariant
                            )
                        }
                        val evidencePage = line.evidenceRegion?.page
                        val sourcePage = state.sourcePages.firstOrNull { page ->
                            page.position == evidencePage
                        }
                        if (sourcePage != null) {
                            OutlinedButton(
                                onClick = {
                                    evidenceTarget = SourceEvidenceTarget(
                                        sourcePage,
                                        line.evidenceRegion
                                    )
                                }
                            ) {
                                Text(stringResource(R.string.view_source_page, sourcePage.position))
                            }
                        }
                    }
                }
                item {
                    CandidateSectionTitle(
                        title = stringResource(R.string.local_item_match),
                        evidence = stringResource(R.string.manual_confirmation_required)
                    )
                    CatalogSearchControls(
                        query = state.itemSearchQuery,
                        label = stringResource(R.string.search_full_local_item_catalog),
                        busy = state.searchBusy,
                        onQueryChange = onItemSearchQueryChange,
                        onSearch = onSearchItems
                    )
                    val visibleItemCandidates = (
                        line.candidates + state.itemSearchResults
                        ).distinctBy(ItemCandidateUi::reference)
                    if (visibleItemCandidates.isEmpty()) {
                        MissingCandidateNotice(stringResource(R.string.no_item_candidates))
                    } else {
                        visibleItemCandidates.forEach { candidate ->
                            ItemCandidateRow(
                                candidate = candidate,
                                selected = candidate.reference == line.selectedLocalItemReference,
                                onSelect = { onSelectItem(candidate.reference) }
                            )
                        }
                    }
                }
                item {
                    Text(
                        stringResource(R.string.prices_and_discounts),
                        modifier = Modifier.padding(horizontal = 20.dp),
                        style = MaterialTheme.typography.titleLarge,
                        fontWeight = FontWeight.SemiBold
                    )
                    Text(
                        stringResource(R.string.approved_commercial_semantics),
                        modifier = Modifier.padding(horizontal = 20.dp, vertical = 6.dp),
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
                line.postingLines.forEachIndexed { index, posting ->
                    item(key = posting.postingLineId) {
                        PostingLineEditor(
                            index = index,
                            count = line.postingLines.size,
                            posting = posting,
                            enabled = !state.busy,
                            onChange = { field, value -> onPostingChange(index, field, value) },
                            onApplyCommercialToAll = { onApplyCommercialToAll(index) },
                            onRemove = { onRemoveExpiry(index) }
                        )
                    }
                }
                item {
                    TextButton(
                        onClick = onAddExpiry,
                        enabled = !state.busy,
                        modifier = Modifier.padding(horizontal = 12.dp)
                    ) {
                        Icon(Icons.Default.Add, contentDescription = null)
                        Spacer(Modifier.width(6.dp))
                        Text(stringResource(R.string.add_another_expiry))
                    }
                }
                item { InvoiceTotalsSummary(state.lines) }
            }
            if (state.lines.isEmpty()) {
                item { MissingCandidateNotice(stringResource(R.string.invalid_connector_revision)) }
            }
        }
    }
    evidenceTarget?.let { target ->
        SourceEvidenceDialog(
            target = target,
            onDismiss = { evidenceTarget = null }
        )
    }
}

@Composable
private fun CatalogSearchControls(
    query: String,
    label: String,
    busy: Boolean,
    onQueryChange: (String) -> Unit,
    onSearch: () -> Unit
) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp)
    ) {
        OutlinedTextField(
            value = query,
            onValueChange = onQueryChange,
            modifier = Modifier.weight(1f),
            label = { Text(label) },
            enabled = !busy,
            singleLine = true
        )
        Button(onClick = onSearch, enabled = query.isNotBlank() && !busy) {
            Text(stringResource(R.string.search))
        }
    }
}

@Composable
private fun CandidateSectionTitle(title: String, evidence: String) {
    Column(
        Modifier.fillMaxWidth().padding(horizontal = 20.dp),
        verticalArrangement = Arrangement.spacedBy(4.dp)
    ) {
        Text(title, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold)
        Text(
            evidence.ifBlank { stringResource(R.string.not_available) },
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )
    }
}

@Composable
private fun VendorCandidateRow(
    candidate: VendorCandidateUi,
    selected: Boolean,
    onSelect: () -> Unit
) {
    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 20.dp, vertical = 4.dp)
            .selectable(
                selected = selected,
                role = Role.RadioButton,
                onClick = onSelect
            ),
        shape = MaterialTheme.shapes.small,
        color = if (selected) {
            MaterialTheme.colorScheme.primaryContainer
        } else {
            MaterialTheme.colorScheme.surface
        },
        border = BorderStroke(
            1.dp,
            if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.outlineVariant
        )
    ) {
        Row(
            Modifier.padding(12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            RadioButton(selected = selected, onClick = null)
            Column(Modifier.weight(1f)) {
                Text(candidate.displayName, fontWeight = FontWeight.SemiBold)
                Text(
                    listOfNotNull(candidate.code, candidate.reference).joinToString(" • "),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                if (candidate.reasonCodes.isNotEmpty()) {
                    Text(
                        candidate.reasonCodes.joinToString(" • "),
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }
        }
    }
}

@Composable
private fun ItemCandidateRow(
    candidate: ItemCandidateUi,
    selected: Boolean,
    onSelect: () -> Unit
) {
    val blocked = candidate.hardMismatches.isNotEmpty()
    Surface(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = 20.dp, vertical = 4.dp)
            .selectable(
                selected = selected,
                enabled = !blocked,
                role = Role.RadioButton,
                onClick = onSelect
            ),
        shape = MaterialTheme.shapes.small,
        color = if (selected) {
            MaterialTheme.colorScheme.primaryContainer
        } else {
            MaterialTheme.colorScheme.surface
        },
        border = BorderStroke(
            1.dp,
            when {
                blocked -> MaterialTheme.colorScheme.error
                selected -> MaterialTheme.colorScheme.primary
                else -> MaterialTheme.colorScheme.outlineVariant
            }
        )
    ) {
        Row(
            Modifier.padding(12.dp),
            verticalAlignment = Alignment.Top
        ) {
            RadioButton(selected = selected, onClick = null, enabled = !blocked)
            Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                val labelDirection = when (candidate.displayDirection) {
                    "RTL" -> TextDirection.Rtl
                    "LTR" -> TextDirection.Ltr
                    else -> TextDirection.Content
                }
                Text(
                    candidate.displayLabel,
                    fontWeight = FontWeight.SemiBold,
                    style = MaterialTheme.typography.bodyLarge.copy(
                        textDirection = labelDirection
                    ),
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis
                )
                Text(
                    candidate.reference,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
                candidate.rawLabel?.takeIf { it != candidate.displayLabel }?.let { raw ->
                    Text(
                        stringResource(R.string.unverified_raw_genius_label, raw),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis
                    )
                }
                if (candidate.qualityFlags.isNotEmpty()) {
                    Text(
                        candidate.qualityFlags.joinToString(" • "),
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.tertiary
                    )
                }
                if (blocked) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Icon(
                            Icons.Default.Warning,
                            contentDescription = null,
                            tint = MaterialTheme.colorScheme.error
                        )
                        Spacer(Modifier.width(5.dp))
                        Text(
                            stringResource(
                                R.string.hard_mismatch_blocked,
                                candidate.hardMismatches.joinToString(", ")
                            ),
                            color = MaterialTheme.colorScheme.error,
                            style = MaterialTheme.typography.bodySmall
                        )
                    }
                } else if (candidate.reasonCodes.isNotEmpty()) {
                    Text(
                        candidate.reasonCodes.joinToString(" • "),
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }
        }
    }
}

private enum class PostingField {
    Quantity,
    ExpiryDate,
    Batch,
    PurchaseUnitPrice,
    DiscountOne,
    DiscountTwo,
    SellingUnitPrice
}

@Composable
private fun PostingLineEditor(
    index: Int,
    count: Int,
    posting: PostingLineUi,
    enabled: Boolean,
    onChange: (PostingField, String) -> Unit,
    onApplyCommercialToAll: () -> Unit,
    onRemove: () -> Unit
) {
    Surface(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 4.dp),
        shape = MaterialTheme.shapes.medium,
        color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.45f)
    ) {
        Column(
            Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp)
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    stringResource(R.string.expiry_number, index + 1),
                    modifier = Modifier.weight(1f),
                    fontWeight = FontWeight.SemiBold
                )
                if (count > 1) {
                    IconButton(onClick = onRemove, enabled = enabled) {
                        Icon(
                            Icons.Default.Delete,
                            contentDescription = stringResource(R.string.remove_expiry_description, index + 1),
                            tint = MaterialTheme.colorScheme.error
                        )
                    }
                }
            }
            OutlinedTextField(
                value = posting.quantity,
                onValueChange = { onChange(PostingField.Quantity, it) },
                modifier = Modifier.fillMaxWidth(),
                label = { Text(stringResource(R.string.quantity_boxes)) },
                enabled = enabled,
                singleLine = true
            )
            OutlinedTextField(
                value = posting.expiryDate,
                onValueChange = { onChange(PostingField.ExpiryDate, it) },
                modifier = Modifier.fillMaxWidth(),
                label = { Text(stringResource(R.string.expiry_date_iso)) },
                supportingText = { Text(stringResource(R.string.expiry_date_format_hint)) },
                enabled = enabled,
                singleLine = true
            )
            OutlinedTextField(
                value = posting.batch,
                onValueChange = { onChange(PostingField.Batch, it) },
                modifier = Modifier.fillMaxWidth(),
                label = { Text(stringResource(R.string.batch_optional)) },
                enabled = enabled,
                singleLine = true
            )
            HorizontalDivider()
            Text(
                stringResource(
                    R.string.ocr_original_commercial_values,
                    posting.originalPurchaseUnitPrice,
                    posting.originalDiscountOne,
                    posting.originalDiscountTwo,
                    posting.originalSellingUnitPrice
                ),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            OutlinedTextField(
                value = posting.purchaseUnitPrice,
                onValueChange = { onChange(PostingField.PurchaseUnitPrice, it) },
                modifier = Modifier.fillMaxWidth(),
                label = { Text(stringResource(R.string.purchase_unit_price_egp)) },
                enabled = enabled,
                singleLine = true
            )
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedTextField(
                    value = posting.discountOne,
                    onValueChange = { onChange(PostingField.DiscountOne, it) },
                    modifier = Modifier.weight(1f),
                    label = { Text(stringResource(R.string.discount_one_percent)) },
                    enabled = enabled,
                    singleLine = true
                )
                OutlinedTextField(
                    value = posting.discountTwo,
                    onValueChange = { onChange(PostingField.DiscountTwo, it) },
                    modifier = Modifier.weight(1f),
                    label = { Text(stringResource(R.string.discount_two_percent)) },
                    enabled = enabled,
                    singleLine = true
                )
            }
            OutlinedTextField(
                value = posting.sellingUnitPrice,
                onValueChange = { onChange(PostingField.SellingUnitPrice, it) },
                modifier = Modifier.fillMaxWidth(),
                label = { Text(stringResource(R.string.selling_price_box_tax_inclusive)) },
                supportingText = { Text(stringResource(R.string.new_stock_only_preserve_existing)) },
                enabled = enabled,
                singleLine = true
            )
            if (count > 1) {
                TextButton(onClick = onApplyCommercialToAll, enabled = enabled) {
                    Text(
                        androidx.compose.ui.res.pluralStringResource(
                            R.plurals.apply_commercial_to_all_splits,
                            count,
                            count
                        )
                    )
                }
            }
        }
    }
}

private data class CalculatedReviewTotals(
    val grossPurchase: BigDecimal,
    val netPurchase: BigDecimal,
    val expectedSelling: BigDecimal
)

@Composable
private fun InvoiceTotalsSummary(lines: List<SourceLineUi>) {
    val totals = calculateTotals(lines)
    Surface(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 8.dp),
        shape = MaterialTheme.shapes.medium,
        color = MaterialTheme.colorScheme.secondaryContainer
    ) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(7.dp)) {
            Text(stringResource(R.string.invoice_details), fontWeight = FontWeight.Bold)
            TotalRow(R.string.gross_purchase, totals?.grossPurchase)
            TotalRow(R.string.invoice_net_total, totals?.netPurchase)
            TotalRow(R.string.expected_selling_total, totals?.expectedSelling)
        }
    }
}

@Composable
private fun TotalRow(labelResource: Int, value: BigDecimal?) {
    Row(Modifier.fillMaxWidth()) {
        Text(
            stringResource(labelResource),
            modifier = Modifier.weight(1f),
            color = MaterialTheme.colorScheme.onSecondaryContainer
        )
        Text(
            value?.let { amount ->
                stringResource(
                    R.string.amount_egp,
                    amount.setScale(2, RoundingMode.HALF_UP).toPlainString()
                )
            } ?: stringResource(R.string.total_not_ready),
            fontWeight = FontWeight.SemiBold,
            color = MaterialTheme.colorScheme.onSecondaryContainer
        )
    }
}

@Composable
private fun MissingCandidateNotice(message: String) {
    Surface(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 8.dp),
        color = MaterialTheme.colorScheme.errorContainer,
        shape = MaterialTheme.shapes.small
    ) {
        Text(
            message,
            modifier = Modifier.padding(14.dp),
            color = MaterialTheme.colorScheme.onErrorContainer
        )
    }
}

private class ConnectorReviewViewModel(
    application: Application,
    private val draftId: String,
    revisionJson: String,
    private val repository: PharmaAutoRepository
) : AndroidViewModel(application) {
    private val parsedReview = runCatching { parseReview(revisionJson) }
    private val sourceRoot: JsonObject? = parsedReview.getOrNull()?.first
    var uiState by mutableStateOf(
        parsedReview.map { parsed -> parsed.second }
            .getOrElse { exception ->
                ConnectorReviewUiState(
                    revisionId = "",
                    vendorEvidence = "",
                    selectedLocalVendorReference = null,
                    vendorCandidates = emptyList(),
                    lines = emptyList(),
                    notice = ConnectorReviewNotice.InvalidRevision,
                    errorDetail = exception.message?.take(160)
                )
            }
    )
        private set

    init {
        viewModelScope.launch {
            val pages = repository.draft(draftId)?.pages
                ?.sortedBy { page -> page.position }
                ?.map { page ->
                    SourcePageUi(
                        position = page.position,
                        uri = page.contentUri,
                        mimeType = page.mimeType,
                        sha256 = page.sha256
                    )
                }
                .orEmpty()
            uiState = uiState.copy(sourcePages = pages)
        }
    }

    fun selectVendor(reference: String) {
        if (!uiState.busy) {
            uiState = uiState.copy(selectedLocalVendorReference = reference, notice = null)
        }
    }

    fun selectItem(reference: String) {
        updateCurrentLine { line -> line.copy(selectedLocalItemReference = reference) }
    }

    fun updateVendorSearchQuery(value: String) {
        if (!uiState.busy && !uiState.searchBusy) {
            uiState = uiState.copy(vendorSearchQuery = value.take(200))
        }
    }

    fun updateItemSearchQuery(value: String) {
        if (!uiState.busy && !uiState.searchBusy) {
            uiState = uiState.copy(itemSearchQuery = value.take(200))
        }
    }

    fun searchVendors() {
        val query = uiState.vendorSearchQuery.trim()
        if (query.isEmpty() || uiState.busy || uiState.searchBusy) return
        viewModelScope.launch {
            uiState = uiState.copy(searchBusy = true, notice = null, errorDetail = null)
            runCatching {
                val api = repository.sessions.api()
                val response = authenticated { authorization ->
                    api.searchVendors(authorization, query, 25)
                }
                check(!response.finalLocalIdentitySelected && !response.geniusWritePerformed)
                response.candidates.map(LocalVendorCandidateContract::toUi)
            }.onSuccess { candidates ->
                uiState = uiState.copy(
                    searchBusy = false,
                    vendorSearchResults = candidates
                )
            }.onFailure { exception ->
                uiState = uiState.copy(
                    searchBusy = false,
                    notice = ConnectorReviewNotice.SearchFailed,
                    errorDetail = exception.message?.take(160)
                )
            }
        }
    }

    fun searchItems() {
        val query = uiState.itemSearchQuery.trim()
        val line = uiState.currentLine ?: return
        if (query.isEmpty() || uiState.busy || uiState.searchBusy) return
        viewModelScope.launch {
            uiState = uiState.copy(searchBusy = true, notice = null, errorDetail = null)
            runCatching {
                val api = repository.sessions.api()
                val response = authenticated { authorization ->
                    api.searchItems(
                        authorization = authorization,
                        query = query,
                        vendorItemCode = line.vendorItemCode,
                        strength = line.strength,
                        dosageForm = line.dosageForm,
                        limit = 25
                    )
                }
                check(!response.finalLocalIdentitySelected && !response.geniusWritePerformed)
                response.candidates.map(LocalItemCandidateContract::toUi)
            }.onSuccess { candidates ->
                uiState = uiState.copy(
                    searchBusy = false,
                    itemSearchResults = candidates
                )
            }.onFailure { exception ->
                uiState = uiState.copy(
                    searchBusy = false,
                    notice = ConnectorReviewNotice.SearchFailed,
                    errorDetail = exception.message?.take(160)
                )
            }
        }
    }

    fun previousLine() {
        if (!uiState.busy && uiState.currentLineIndex > 0) {
            uiState = uiState.copy(
                currentLineIndex = uiState.currentLineIndex - 1,
                itemSearchQuery = "",
                itemSearchResults = emptyList(),
                notice = null
            )
        }
    }

    fun nextLine() {
        val current = uiState.currentLine ?: return
        val validation = validateLine(current)
        if (validation != null) {
            uiState = uiState.copy(notice = validation)
            return
        }
        if (uiState.currentLineIndex < uiState.lines.lastIndex) {
            uiState = uiState.copy(
                currentLineIndex = uiState.currentLineIndex + 1,
                itemSearchQuery = "",
                itemSearchResults = emptyList(),
                notice = null
            )
        }
    }

    fun updatePostingLine(index: Int, field: PostingField, value: String) {
        updateCurrentLine { line ->
            line.copy(postingLines = line.postingLines.mapIndexed { candidate, posting ->
                if (candidate != index) posting else when (field) {
                    PostingField.Quantity -> posting.copy(quantity = normalizeDecimal(value))
                    PostingField.ExpiryDate -> posting.copy(expiryDate = value.take(10))
                    PostingField.Batch -> posting.copy(batch = value.take(128))
                    PostingField.PurchaseUnitPrice ->
                        posting.copy(purchaseUnitPrice = normalizeDecimal(value))
                    PostingField.DiscountOne -> posting.copy(discountOne = normalizeDecimal(value))
                    PostingField.DiscountTwo -> posting.copy(discountTwo = normalizeDecimal(value))
                    PostingField.SellingUnitPrice ->
                        posting.copy(sellingUnitPrice = normalizeDecimal(value))
                }
            })
        }
    }

    fun addExpiry() {
        updateCurrentLine { line ->
            val template = line.postingLines.firstOrNull() ?: return@updateCurrentLine line
            val next = line.postingLines.size + 1
            line.copy(
                postingLines = line.postingLines + template.copy(
                    postingLineId = UUID.randomUUID().toString(),
                    splitIndex = next,
                    postingSequence = line.postingLines.maxOf { it.postingSequence } + 1,
                    quantity = "0",
                    expiryDate = "",
                    batch = ""
                )
            )
        }
    }

    fun applyCommercialToAll(sourceIndex: Int) {
        updateCurrentLine { line ->
            val source = line.postingLines.getOrNull(sourceIndex) ?: return@updateCurrentLine line
            line.copy(postingLines = line.postingLines.map { posting ->
                posting.copy(
                    purchaseUnitPrice = source.purchaseUnitPrice,
                    discountOne = source.discountOne,
                    discountTwo = source.discountTwo,
                    sellingUnitPrice = source.sellingUnitPrice
                )
            })
        }
    }

    fun removeExpiry(index: Int) {
        updateCurrentLine { line ->
            if (line.postingLines.size <= 1) line else line.copy(
                postingLines = line.postingLines.filterIndexed { candidate, _ -> candidate != index }
            )
        }
    }

    fun confirmReview() {
        if (uiState.busy || uiState.searchBusy) return
        if (uiState.selectedLocalVendorReference.isNullOrBlank()) {
            uiState = uiState.copy(notice = ConnectorReviewNotice.SelectVendor)
            return
        }
        val missingItem = uiState.lines.indexOfFirst { it.selectedLocalItemReference.isNullOrBlank() }
        if (missingItem >= 0) {
            uiState = uiState.copy(
                currentLineIndex = missingItem,
                notice = ConnectorReviewNotice.SelectItem
            )
            return
        }
        val invalid = uiState.lines.mapIndexedNotNull { index, line ->
            validateLine(line)?.let { notice -> index to notice }
        }.firstOrNull()
        if (invalid != null) {
            uiState = uiState.copy(currentLineIndex = invalid.first, notice = invalid.second)
            return
        }
        val root = sourceRoot ?: run {
            uiState = uiState.copy(notice = ConnectorReviewNotice.InvalidRevision)
            return
        }
        val edited = runCatching { buildEditedRevision(root, uiState) }.getOrElse { exception ->
            uiState = uiState.copy(
                notice = ConnectorReviewNotice.InvalidRevision,
                errorDetail = exception.message?.take(160)
            )
            return
        }

        viewModelScope.launch {
            uiState = uiState.copy(busy = true, notice = null, errorDetail = null)
            runCatching {
                val api = repository.sessions.api()
                val saved = authenticated { authorization ->
                    api.saveInvoiceRevision(
                        authorization,
                        uiState.revisionId,
                        SaveRevisionRequest(edited, "ANDROID_OPERATOR_REVIEW")
                    )
                }
                check(!saved.geniusWritePerformed) { "Connector reported an unexpected Genius write." }
                val confirmed = authenticated { authorization ->
                    api.confirmInvoiceRevision(authorization, saved.revisionId)
                }
                check(
                    !confirmed.geniusWritePerformed &&
                        !confirmed.commitAvailable &&
                        confirmed.state == "CONFIRMED"
                ) { "Connector did not preserve the Phase 1 read-only boundary." }
                repository.draftDao.saveRevision(
                    draftId = draftId,
                    state = "CONFIRMED",
                    revisionId = saved.revisionId,
                    revisionJson = edited.toString(),
                    updatedAt = System.currentTimeMillis()
                )
                repository.purgeLocalCaptureFiles(draftId)
            }.onSuccess {
                uiState = uiState.copy(busy = false, completed = true)
            }.onFailure { exception ->
                uiState = uiState.copy(
                    busy = false,
                    notice = ConnectorReviewNotice.ConfirmationFailed,
                    errorDetail = exception.message?.take(160)
                )
            }
        }
    }

    fun consumeNotice() {
        uiState = uiState.copy(notice = null, errorDetail = null)
    }

    private suspend fun <T> authenticated(block: suspend (String) -> T): T = try {
        block(repository.sessions.authorization())
    } catch (exception: HttpException) {
        if (exception.code() != 401) throw exception
        block(repository.sessions.authorization(forceRefresh = true))
    }

    private inline fun updateCurrentLine(transform: (SourceLineUi) -> SourceLineUi) {
        if (uiState.busy) return
        val current = uiState.currentLineIndex
        uiState = uiState.copy(
            lines = uiState.lines.mapIndexed { index, line ->
                if (index == current) transform(line) else line
            },
            notice = null
        )
    }

    companion object {
        fun factory(
            application: Application,
            draftId: String,
            revisionJson: String,
            repository: PharmaAutoRepository
        ): ViewModelProvider.Factory = object : ViewModelProvider.Factory {
            @Suppress("UNCHECKED_CAST")
            override fun <T : ViewModel> create(modelClass: Class<T>): T =
                ConnectorReviewViewModel(
                    application,
                    draftId,
                    revisionJson,
                    repository
                ) as T
        }
    }
}

private fun parseReview(json: String): Pair<JsonObject, ConnectorReviewUiState> {
    val root = Json.parseToJsonElement(json).jsonObject
    check(root.string("status") == "AWAITING_USER_REVIEW") {
        "Revision is not awaiting review."
    }
    check(root["geniusWritePerformed"]?.jsonPrimitive?.booleanOrNull == false) {
        "Revision is not read-only."
    }
    val vendorEvidence = root.objectOrNull("vendorEvidence")?.string("normalizedValue").orEmpty()
    val vendors = root.arrayOrEmpty("vendorCandidates").map { element ->
        val candidate = element.jsonObject
        VendorCandidateUi(
            reference = candidate.string("localVendorReference"),
            displayName = candidate.string("displayName"),
            code = candidate.nullableString("code"),
            reasonCodes = candidate.stringArray("reasonCodes")
        )
    }.filter { it.reference.isNotBlank() }
    val lines = root.arrayOrEmpty("sourceLines").mapIndexed { index, element ->
        val source = element.jsonObject
        val postingLines = source.arrayOrEmpty("postingLines").mapIndexed { postingIndex, postingNode ->
            val posting = postingNode.jsonObject
            val commercial = posting.objectOrNull("commercialValues") ?: JsonObject(emptyMap())
            val discounts = commercial.arrayOrEmpty("discounts")
            val original = posting.objectOrNull("originalOcrCommercialValues")
                ?: JsonObject(emptyMap())
            PostingLineUi(
                postingLineId = posting.string("postingLineId").ifBlank { UUID.randomUUID().toString() },
                splitIndex = posting.intOrNull("splitIndex") ?: postingIndex + 1,
                postingSequence = posting.intOrNull("postingSequence") ?: postingIndex + 1,
                quantity = posting.string("quantity"),
                expiryDate = posting.nullableString("expiryDate").orEmpty(),
                batch = posting.nullableString("batch").orEmpty(),
                purchaseUnitPrice = commercial.string("purchaseUnitPrice"),
                discountOne = discounts.getOrNull(0)?.jsonObject?.string("percentage").orEmpty(),
                discountTwo = discounts.getOrNull(1)?.jsonObject?.string("percentage").orEmpty(),
                sellingUnitPrice = commercial.string("sellingUnitPrice"),
                originalPurchaseUnitPrice = original.evidenceValue("purchaseUnitPrice"),
                originalDiscountOne = original.evidenceValue("discount1Percentage"),
                originalDiscountTwo = original.evidenceValue("discount2Percentage"),
                originalSellingUnitPrice = original.evidenceValue("sellingUnitPrice")
            )
        }
        check(postingLines.isNotEmpty()) { "Source line ${index + 1} has no posting lines." }
        val description = source.objectOrNull("descriptionEvidence")
            ?.string("normalizedValue")
            .orEmpty()
        val descriptionEvidence = source.objectOrNull("descriptionEvidence")
        val required = postingLines.fold(BigDecimal.ZERO) { total, posting ->
            total.add(decimalOrZero(posting.quantity))
        }.stripTrailingZeros().toPlainString()
        SourceLineUi(
            sourceLineId = source.string("sourceLineId"),
            sequence = source.intOrNull("sequence") ?: index + 1,
            description = description.ifBlank { source.string("sourceLineId") },
            vendorItemCode = source.objectOrNull("vendorItemCodeEvidence")
                ?.nullableString("normalizedValue"),
            strength = extractStrength(description),
            dosageForm = extractDosageForm(description),
            evidenceRegion = descriptionEvidence?.toEvidenceRegion(),
            requiredQuantity = required,
            selectedLocalItemReference = source.nullableString("selectedLocalItemReference"),
            candidates = source.arrayOrEmpty("localCandidates").mapNotNull { candidateNode ->
                val candidate = candidateNode.jsonObject
                candidate.string("localItemReference").takeIf(String::isNotBlank)?.let { reference ->
                    ItemCandidateUi(
                        reference = reference,
                        displayLabel = candidate.string("displayLabel"),
                        rawLabel = candidate.nullableString("rawLabel"),
                        displayDirection = candidate.string("displayDirection"),
                        qualityFlags = candidate.stringArray("qualityFlags"),
                        reasonCodes = candidate.stringArray("reasonCodes"),
                        hardMismatches = candidate.stringArray("hardMismatches")
                    )
                }
            },
            postingLines = postingLines
        )
    }
    check(lines.isNotEmpty()) { "Revision contains no source lines." }
    return root to ConnectorReviewUiState(
        revisionId = root.string("revisionId"),
        vendorEvidence = vendorEvidence,
        selectedLocalVendorReference = root.nullableString("selectedLocalVendorReference"),
        vendorCandidates = vendors,
        lines = lines
    )
}

private fun buildEditedRevision(root: JsonObject, state: ConnectorReviewUiState): JsonObject {
    val editedRoot = root.toMutableMap()
    editedRoot["selectedLocalVendorReference"] = JsonPrimitive(
        requireNotNull(state.selectedLocalVendorReference)
    )
    editedRoot["requiresManualVendorConfirmation"] = JsonPrimitive(false)
    val originalSources = root.arrayOrEmpty("sourceLines")
    editedRoot["sourceLines"] = JsonArray(state.lines.mapIndexed { sourceIndex, line ->
        val original = originalSources[sourceIndex].jsonObject
        val editedSource = original.toMutableMap()
        editedSource["selectedLocalItemReference"] = JsonPrimitive(
            requireNotNull(line.selectedLocalItemReference)
        )
        editedSource["requiresManualItemConfirmation"] = JsonPrimitive(false)
        val originals = original.arrayOrEmpty("postingLines").associateBy { posting ->
            posting.jsonObject.string("postingLineId")
        }
        val template = original.arrayOrEmpty("postingLines").first().jsonObject
        editedSource["postingLines"] = JsonArray(line.postingLines.map { posting ->
            patchPostingLine(originals[posting.postingLineId]?.jsonObject ?: template, posting)
        })
        JsonObject(editedSource)
    })
    editedRoot["geniusWritePerformed"] = JsonPrimitive(false)
    return JsonObject(editedRoot)
}

private fun patchPostingLine(original: JsonObject, posting: PostingLineUi): JsonObject {
    val edited = original.toMutableMap()
    edited["postingLineId"] = JsonPrimitive(posting.postingLineId)
    edited["splitIndex"] = JsonPrimitive(posting.splitIndex)
    edited["postingSequence"] = JsonPrimitive(posting.postingSequence)
    edited["quantity"] = JsonPrimitive(posting.quantity)
    edited["expiryDate"] = JsonPrimitive(posting.expiryDate)
    edited["batch"] = posting.batch.takeIf(String::isNotBlank)?.let(::JsonPrimitive) ?: JsonNull
    val originalCommercial = original.objectOrNull("commercialValues") ?: JsonObject(emptyMap())
    val commercial = originalCommercial.toMutableMap()
    commercial["currency"] = JsonPrimitive("EGP")
    commercial["purchaseUnitPrice"] = JsonPrimitive(posting.purchaseUnitPrice)
    commercial["discounts"] = JsonArray(listOf(
        JsonObject(mapOf(
            "sequence" to JsonPrimitive(1),
            "kind" to JsonPrimitive("PERCENTAGE"),
            "percentage" to JsonPrimitive(posting.discountOne),
            "applicationBasis" to JsonPrimitive("PURCHASE_UNIT_PRICE"),
            "affectsPurchaseUnitPrice" to JsonPrimitive(true)
        )),
        JsonObject(mapOf(
            "sequence" to JsonPrimitive(2),
            "kind" to JsonPrimitive("PERCENTAGE"),
            "percentage" to JsonPrimitive(posting.discountTwo),
            "applicationBasis" to JsonPrimitive("REMAINING_LINE_SUBTOTAL"),
            "affectsPurchaseUnitPrice" to JsonPrimitive(false)
        ))
    ))
    commercial["sellingUnit"] = JsonPrimitive("BOX")
    commercial["sellingUnitPrice"] = JsonPrimitive(posting.sellingUnitPrice)
    commercial["sellingPriceTaxTreatment"] = JsonPrimitive("INCLUSIVE")
    commercial["sellingPriceScope"] = JsonPrimitive("NEW_STOCK_ONLY")
    commercial["existingStockPriceBehavior"] = JsonPrimitive("PRESERVE")
    commercial["unsupportedScopeBehavior"] = JsonPrimitive("BLOCK_COMMIT")
    edited["commercialValues"] = JsonObject(commercial)
    return JsonObject(edited)
}

private fun validateLine(line: SourceLineUi): ConnectorReviewNotice? {
    if (line.selectedLocalItemReference.isNullOrBlank()) return ConnectorReviewNotice.SelectItem
    val commercialValid = line.postingLines.all { posting ->
        nonNegative(posting.purchaseUnitPrice) &&
            percentage(posting.discountOne) &&
            percentage(posting.discountTwo) &&
            nonNegative(posting.sellingUnitPrice)
    }
    if (!commercialValid) return ConnectorReviewNotice.InvalidCommercial
    val assigned = line.postingLines.fold(BigDecimal.ZERO) { total, posting ->
        total.add(decimalOrZero(posting.quantity))
    }
    val expiryValid = line.postingLines.all { posting ->
        positive(posting.quantity) && runCatching { LocalDate.parse(posting.expiryDate) }.isSuccess
    } && assigned.compareTo(decimalOrZero(line.requiredQuantity)) == 0
    return if (expiryValid) null else ConnectorReviewNotice.InvalidExpiry
}

private fun calculateTotals(lines: List<SourceLineUi>): CalculatedReviewTotals? {
    var gross = BigDecimal.ZERO
    var net = BigDecimal.ZERO
    var selling = BigDecimal.ZERO
    for (posting in lines.flatMap(SourceLineUi::postingLines)) {
        val quantity = normalizeDecimal(posting.quantity).toBigDecimalOrNull() ?: return null
        val purchase = normalizeDecimal(posting.purchaseUnitPrice).toBigDecimalOrNull() ?: return null
        val discountOne = normalizeDecimal(posting.discountOne).toBigDecimalOrNull() ?: return null
        val discountTwo = normalizeDecimal(posting.discountTwo).toBigDecimalOrNull() ?: return null
        val sellingPrice = normalizeDecimal(posting.sellingUnitPrice).toBigDecimalOrNull() ?: return null
        if (quantity < BigDecimal.ZERO || purchase < BigDecimal.ZERO ||
            discountOne !in BigDecimal.ZERO..BigDecimal("100") ||
            discountTwo !in BigDecimal.ZERO..BigDecimal("100") ||
            sellingPrice < BigDecimal.ZERO
        ) return null
        val lineGross = quantity.multiply(purchase)
        val afterFirst = lineGross.multiply(
            BigDecimal("100").subtract(discountOne).divide(BigDecimal("100"))
        )
        val lineNet = afterFirst.multiply(
            BigDecimal("100").subtract(discountTwo).divide(BigDecimal("100"))
        )
        gross = gross.add(lineGross)
        net = net.add(lineNet)
        selling = selling.add(quantity.multiply(sellingPrice))
    }
    return CalculatedReviewTotals(gross, net, selling)
}

private fun normalizeDecimal(input: String): String = InvoiceReviewRules.normalizeDecimalInput(input)
private fun decimalOrZero(value: String): BigDecimal = normalizeDecimal(value).toBigDecimalOrNull()
    ?: BigDecimal.ZERO
private fun nonNegative(value: String): Boolean = normalizeDecimal(value).toBigDecimalOrNull()
    ?.let { it >= BigDecimal.ZERO } == true
private fun positive(value: String): Boolean = normalizeDecimal(value).toBigDecimalOrNull()
    ?.let { it > BigDecimal.ZERO } == true
private fun percentage(value: String): Boolean = normalizeDecimal(value).toBigDecimalOrNull()
    ?.let { it in BigDecimal.ZERO..BigDecimal("100") } == true

private fun extractStrength(description: String): String? {
    val match = Regex("""(?i)(\d+(?:[.,]\d+)?)\s*(mg|ml|مجم|مل)\b""")
        .find(description) ?: return null
    return "${match.groupValues[1].replace(',', '.')} ${match.groupValues[2]}"
}

private fun extractDosageForm(description: String): String? = when {
    description.contains("CAP", ignoreCase = true) -> "CAPSULE"
    description.contains("TAB", ignoreCase = true) -> "TABLET"
    description.contains("SYRUP", ignoreCase = true) || description.contains("شراب") -> "SYRUP"
    else -> null
}

private fun JsonObject.string(name: String): String = this[name]?.jsonPrimitive?.contentOrNull.orEmpty()
private fun JsonObject.nullableString(name: String): String? = this[name]
    ?.takeUnless { it is JsonNull }
    ?.jsonPrimitive
    ?.contentOrNull
private fun JsonObject.objectOrNull(name: String): JsonObject? = this[name] as? JsonObject
private fun JsonObject.arrayOrEmpty(name: String): JsonArray = this[name] as? JsonArray ?: JsonArray(emptyList())
private fun JsonObject.stringArray(name: String): List<String> = arrayOrEmpty(name).mapNotNull { element ->
    element.jsonPrimitive.contentOrNull
}
private fun JsonObject.intOrNull(name: String): Int? = this[name]?.jsonPrimitive?.contentOrNull?.toIntOrNull()
private fun JsonObject.evidenceValue(name: String): String {
    val evidence = objectOrNull(name) ?: return "—"
    return evidence.nullableString("rawValue")
        ?: evidence.nullableString("normalizedValue")
        ?: "—"
}

private fun JsonObject.toEvidenceRegion(): EvidenceRegion? {
    val page = intOrNull("page") ?: return null
    val box = objectOrNull("boundingBox") ?: return EvidenceRegion(page, null)
    val x = box["x"]?.jsonPrimitive?.contentOrNull?.toFloatOrNull() ?: return null
    val y = box["y"]?.jsonPrimitive?.contentOrNull?.toFloatOrNull() ?: return null
    val width = box["width"]?.jsonPrimitive?.contentOrNull?.toFloatOrNull() ?: return null
    val height = box["height"]?.jsonPrimitive?.contentOrNull?.toFloatOrNull() ?: return null
    return EvidenceRegion(page, NormalizedBox(x, y, width, height))
}

private fun LocalVendorCandidateContract.toUi(): VendorCandidateUi = VendorCandidateUi(
    reference = localVendorReference,
    displayName = displayName,
    code = code,
    reasonCodes = reasonCodes
)

private fun LocalItemCandidateContract.toUi(): ItemCandidateUi = ItemCandidateUi(
    reference = localItemReference,
    displayLabel = displayLabel,
    rawLabel = rawLabel,
    displayDirection = displayDirection,
    qualityFlags = qualityFlags,
    reasonCodes = reasonCodes,
    hardMismatches = hardMismatches
)
