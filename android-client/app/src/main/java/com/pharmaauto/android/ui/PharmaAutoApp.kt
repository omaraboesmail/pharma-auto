package com.pharmaauto.android.ui

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.KeyboardArrowUp
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
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
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import androidx.lifecycle.viewmodel.compose.viewModel
import com.pharmaauto.android.R
import com.pharmaauto.android.capture.AnalyzedPage
import com.pharmaauto.android.data.InvoiceDraftEntity
import java.text.DateFormat
import java.util.Date

@Composable
fun PharmaAutoApp(
    incomingPairingPayload: String?,
    onPairingPayloadConsumed: () -> Unit,
    viewModel: PharmaAutoAppViewModel = viewModel()
) {
    val state by viewModel.uiState.collectAsState()
    val snackbar = remember { SnackbarHostState() }
    LaunchedEffect(incomingPairingPayload) {
        incomingPairingPayload?.let { payload ->
            viewModel.acceptPairingPayload(payload)
            onPairingPayloadConsumed()
        }
    }
    LaunchedEffect(state.message, state.error) {
        val notice = state.error ?: state.message
        if (notice != null) {
            snackbar.showSnackbar(notice)
            viewModel.consumeNotice()
        }
    }

    Box(Modifier.fillMaxSize()) {
        when (state.screen) {
            AppScreen.Pairing -> PairingScreen(
                snackbar,
                state.busy,
                viewModel::acceptPairingPayload
            )
            AppScreen.Inbox -> InboxScreen(
                snackbar,
                state.profile?.pharmacyDisplayName.orEmpty(),
                state.drafts,
                viewModel::openCapture,
                viewModel::openDraft,
                viewModel::retryDraft,
                viewModel::forgetPairing
            )
            AppScreen.Capture -> CaptureScreen(
                snackbar,
                state.capturedPages,
                state.busy,
                viewModel::backToInbox,
                viewModel::addCapturedPage,
                viewModel::movePage,
                viewModel::removePage,
                viewModel::submitCapture
            )
            AppScreen.Review -> {
                val draft = state.drafts.firstOrNull { it.draftId == state.selectedDraftId }
                if (draft?.revisionJson == null) {
                    LaunchedEffect(Unit) { viewModel.backToInbox() }
                } else {
                    ConnectorReviewRoute(
                        draftId = draft.draftId,
                        revisionJson = draft.revisionJson,
                        onBack = viewModel::backToInbox
                    )
                }
            }
        }
        if (state.busy && state.screen != AppScreen.Pairing && state.screen != AppScreen.Capture) {
            BusyOverlay()
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun PairingScreen(
    snackbar: SnackbarHostState,
    busy: Boolean,
    onPair: (String) -> Unit
) {
    var payload by remember { mutableStateOf("") }
    Scaffold(
        contentWindowInsets = WindowInsets.safeDrawing,
        topBar = { AppTopBar(title = stringResource(R.string.pair_this_device)) },
        snackbarHost = { SnackbarHost(snackbar) }
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .padding(horizontal = 24.dp, vertical = 28.dp),
            verticalArrangement = Arrangement.spacedBy(18.dp)
        ) {
            Text(
                text = stringResource(R.string.connect_to_pharmacy),
                style = MaterialTheme.typography.headlineSmall,
                fontWeight = FontWeight.SemiBold
            )
            Text(
                text = stringResource(R.string.pairing_instructions),
                style = MaterialTheme.typography.bodyLarge,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Surface(
                shape = MaterialTheme.shapes.medium,
                color = MaterialTheme.colorScheme.surfaceVariant
            ) {
                Column(Modifier.padding(18.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text(
                        stringResource(R.string.why_pairing_required),
                        fontWeight = FontWeight.SemiBold
                    )
                    Text(
                        stringResource(R.string.pairing_security_note),
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }
            OutlinedTextField(
                value = payload,
                onValueChange = { payload = it },
                modifier = Modifier.fillMaxWidth(),
                label = { Text(stringResource(R.string.pairing_link)) },
                supportingText = { Text(stringResource(R.string.pairing_link_hint)) },
                minLines = 3,
                enabled = !busy
            )
            Button(
                onClick = { onPair(payload) },
                modifier = Modifier.fillMaxWidth().heightIn(min = 56.dp),
                enabled = payload.isNotBlank() && !busy
            ) {
                if (busy) {
                    CircularProgressIndicator(
                        modifier = Modifier.padding(end = 12.dp).width(22.dp).height(22.dp),
                        strokeWidth = 2.dp,
                        color = MaterialTheme.colorScheme.onPrimary
                    )
                }
                Text(stringResource(R.string.pair_securely))
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun InboxScreen(
    snackbar: SnackbarHostState,
    pharmacyName: String,
    drafts: List<InvoiceDraftEntity>,
    onNewInvoice: () -> Unit,
    onOpenDraft: (String) -> Unit,
    onRetryDraft: (String) -> Unit,
    onForgetPairing: () -> Unit
) {
    Scaffold(
        contentWindowInsets = WindowInsets.safeDrawing,
        topBar = { AppTopBar(title = stringResource(R.string.invoices)) },
        snackbarHost = { SnackbarHost(snackbar) },
        bottomBar = {
            Surface(shadowElevation = 8.dp, tonalElevation = 3.dp) {
                Button(
                    onClick = onNewInvoice,
                    modifier = Modifier.fillMaxWidth().padding(16.dp).heightIn(min = 56.dp)
                ) {
                    Icon(Icons.Filled.Add, contentDescription = null)
                    Spacer(Modifier.width(8.dp))
                    Text(stringResource(R.string.capture_invoice))
                }
            }
        }
    ) { padding ->
        LazyColumn(
            modifier = Modifier.fillMaxSize().padding(padding),
            contentPadding = PaddingValues(bottom = 20.dp)
        ) {
            item {
                Surface(color = MaterialTheme.colorScheme.primaryContainer) {
                    Column(Modifier.fillMaxWidth().padding(20.dp)) {
                        Text(
                            text = pharmacyName,
                            style = MaterialTheme.typography.titleLarge,
                            fontWeight = FontWeight.SemiBold
                        )
                        Text(
                            text = stringResource(R.string.paired_connector_writes_disabled),
                            color = MaterialTheme.colorScheme.onPrimaryContainer
                        )
                        TextButton(onClick = onForgetPairing) {
                            Text(stringResource(R.string.pair_different_connector))
                        }
                    }
                }
            }
            if (drafts.isEmpty()) {
                item {
                    Column(
                        modifier = Modifier.fillMaxWidth().padding(28.dp),
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        Text(
                            stringResource(R.string.no_captured_invoices),
                            style = MaterialTheme.typography.titleMedium
                        )
                        Text(
                            stringResource(R.string.no_captured_invoices_hint),
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
            } else {
                itemsIndexed(drafts, key = { _, draft -> draft.draftId }) { index, draft ->
                    DraftRow(draft, onOpenDraft, onRetryDraft)
                    if (index < drafts.lastIndex) HorizontalDivider()
                }
            }
        }
    }
}

@Composable
private fun DraftRow(
    draft: InvoiceDraftEntity,
    onOpenDraft: (String) -> Unit,
    onRetryDraft: (String) -> Unit
) {
    val ready = draft.state == "AWAITING_USER_REVIEW"
    val retryable = draft.state in setOf("PAIRING_REQUIRED", "OCR_FAILED")
    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 16.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(14.dp)
    ) {
        Surface(
            shape = MaterialTheme.shapes.small,
            color = if (ready) {
                MaterialTheme.colorScheme.primaryContainer
            } else {
                MaterialTheme.colorScheme.surfaceVariant
            }
        ) {
            Text(
                text = "${draft.uploadedPageCount}/${draft.expectedPageCount}",
                modifier = Modifier.padding(horizontal = 12.dp, vertical = 10.dp),
                fontWeight = FontWeight.SemiBold
            )
        }
        Column(Modifier.weight(1f)) {
            Text(humanJobState(draft.state), fontWeight = FontWeight.SemiBold)
            Text(
                DateFormat.getDateTimeInstance(DateFormat.MEDIUM, DateFormat.SHORT)
                    .format(Date(draft.updatedAtEpochMillis)),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            draft.failureCode?.let { failure ->
                Text(failure, color = MaterialTheme.colorScheme.error)
            }
        }
        TextButton(
            onClick = {
                if (ready) onOpenDraft(draft.draftId) else onRetryDraft(draft.draftId)
            },
            enabled = ready || retryable
        ) {
            Text(
                stringResource(
                    when {
                        ready -> R.string.review
                        retryable -> R.string.retry
                        else -> R.string.waiting
                    }
                )
            )
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun CaptureScreen(
    snackbar: SnackbarHostState,
    pages: List<AnalyzedPage>,
    busy: Boolean,
    onBack: () -> Unit,
    onAddPage: (Uri, String?) -> Unit,
    onMovePage: (Int, Int) -> Unit,
    onRemovePage: (Int) -> Unit,
    onSubmit: () -> Unit
) {
    val context = LocalContext.current
    var showCamera by remember { mutableStateOf(false) }
    var cameraPermissionDenied by remember { mutableStateOf(false) }
    val cameraPermissionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        cameraPermissionDenied = !granted
        if (granted) showCamera = true
    }
    val fileLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.OpenMultipleDocuments()
    ) { uris ->
        uris.take(100 - pages.size).forEach { uri ->
            runCatching {
                context.contentResolver.takePersistableUriPermission(
                    uri,
                    Intent.FLAG_GRANT_READ_URI_PERMISSION
                )
            }
            onAddPage(uri, context.contentResolver.getType(uri))
        }
    }

    Scaffold(
        contentWindowInsets = WindowInsets.safeDrawing,
        topBar = { AppTopBar(title = stringResource(R.string.capture_invoice), onBack = onBack) },
        snackbarHost = { SnackbarHost(snackbar) },
        bottomBar = {
            Surface(shadowElevation = 8.dp, tonalElevation = 3.dp) {
                Column(Modifier.fillMaxWidth().padding(16.dp)) {
                    Text(
                        pluralStringResource(
                            R.plurals.pages_in_upload_order,
                            pages.size,
                            pages.size
                        ),
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                    Button(
                        onClick = onSubmit,
                        modifier = Modifier.fillMaxWidth().padding(top = 10.dp).heightIn(min = 56.dp),
                        enabled = pages.isNotEmpty() && !busy
                    ) {
                        Text(stringResource(R.string.validate_and_queue_ocr))
                    }
                }
            }
        }
    ) { padding ->
        LazyColumn(
            modifier = Modifier.fillMaxSize().padding(padding),
            contentPadding = PaddingValues(bottom = 18.dp)
        ) {
            item {
                Row(
                    modifier = Modifier.fillMaxWidth().padding(16.dp),
                    horizontalArrangement = Arrangement.spacedBy(12.dp)
                ) {
                    Button(
                        onClick = {
                            if (ContextCompat.checkSelfPermission(
                                    context,
                                    Manifest.permission.CAMERA
                                ) == PackageManager.PERMISSION_GRANTED
                            ) {
                                showCamera = true
                            } else {
                                cameraPermissionLauncher.launch(Manifest.permission.CAMERA)
                            }
                        },
                        modifier = Modifier.weight(1f).heightIn(min = 52.dp),
                        enabled = pages.size < 100 && !busy
                    ) {
                        Text(stringResource(R.string.take_photo))
                    }
                    OutlinedButton(
                        onClick = {
                            fileLauncher.launch(arrayOf("image/jpeg", "image/png", "application/pdf"))
                        },
                        modifier = Modifier.weight(1f).heightIn(min = 52.dp),
                        enabled = pages.size < 100 && !busy
                    ) {
                        Text(stringResource(R.string.choose_files))
                    }
                }
            }
            if (cameraPermissionDenied) {
                item {
                    Text(
                        stringResource(R.string.camera_permission_denied),
                        modifier = Modifier.padding(horizontal = 20.dp),
                        color = MaterialTheme.colorScheme.error
                    )
                }
            }
            if (pages.isEmpty()) {
                item {
                    Column(
                        Modifier.fillMaxWidth().padding(32.dp),
                        horizontalAlignment = Alignment.CenterHorizontally
                    ) {
                        Text(
                            stringResource(R.string.add_invoice_pages),
                            style = MaterialTheme.typography.titleMedium
                        )
                        Text(
                            stringResource(R.string.capture_quality_hint),
                            modifier = Modifier.padding(top = 8.dp),
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                    }
                }
            } else {
                itemsIndexed(pages, key = { _, page -> page.pageId }) { index, page ->
                    CapturePageRow(index, pages.size, page, onMovePage, onRemovePage)
                    if (index < pages.lastIndex) HorizontalDivider(Modifier.padding(horizontal = 20.dp))
                }
            }
        }
    }
    if (busy) BusyOverlay()
    if (showCamera) {
        CameraCaptureDialog(
            onCaptured = { uri ->
                showCamera = false
                onAddPage(uri, "image/jpeg")
            },
            onDismiss = { showCamera = false }
        )
    }
}

@Composable
private fun CapturePageRow(
    index: Int,
    count: Int,
    page: AnalyzedPage,
    onMovePage: (Int, Int) -> Unit,
    onRemovePage: (Int) -> Unit
) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = 20.dp, vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        Surface(
            shape = MaterialTheme.shapes.small,
            color = MaterialTheme.colorScheme.surfaceVariant
        ) {
            Text(
                "${index + 1}",
                modifier = Modifier.padding(horizontal = 14.dp, vertical = 12.dp),
                fontWeight = FontWeight.Bold
            )
        }
        Column(Modifier.weight(1f)) {
            Text(page.mimeType, fontWeight = FontWeight.SemiBold)
            Text(
                "${page.length / 1024} KB • ${page.sha256.take(10)}…",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            if (page.qualityFlags.isNotEmpty()) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(
                        Icons.Filled.Warning,
                        contentDescription = null,
                        tint = MaterialTheme.colorScheme.tertiary
                    )
                    Text(
                        humanQualityFlags(page.qualityFlags),
                        modifier = Modifier.padding(start = 6.dp),
                        color = MaterialTheme.colorScheme.tertiary,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis
                    )
                }
            }
        }
        Column {
            IconButton(
                onClick = { onMovePage(index, -1) },
                enabled = index > 0
            ) {
                Icon(
                    Icons.Filled.KeyboardArrowUp,
                    contentDescription = stringResource(R.string.move_page_up)
                )
            }
            IconButton(
                onClick = { onMovePage(index, 1) },
                enabled = index < count - 1
            ) {
                Icon(
                    Icons.Filled.KeyboardArrowDown,
                    contentDescription = stringResource(R.string.move_page_down)
                )
            }
        }
        IconButton(onClick = { onRemovePage(index) }) {
            Icon(
                Icons.Filled.Delete,
                contentDescription = stringResource(R.string.remove_page, index + 1),
                tint = MaterialTheme.colorScheme.error
            )
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun AppTopBar(title: String, onBack: (() -> Unit)? = null) {
    TopAppBar(
        title = { Text(title, fontWeight = FontWeight.SemiBold) },
        navigationIcon = {
            if (onBack != null) {
                IconButton(onClick = onBack) {
                    Icon(
                        Icons.AutoMirrored.Filled.ArrowBack,
                        contentDescription = stringResource(R.string.back)
                    )
                }
            }
        },
        colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.surface)
    )
}

@Composable
private fun BusyOverlay() {
    Box(
        modifier = Modifier.fillMaxSize().background(Color.White.copy(alpha = 0.72f)),
        contentAlignment = Alignment.Center
    ) {
        CircularProgressIndicator()
    }
}

@Composable
private fun humanJobState(state: String): String = when (state) {
    "LOCAL_DRAFT" -> stringResource(R.string.job_ready_upload)
    "CAPTURED", "LOCALLY_VALIDATED", "UPLOADING" -> stringResource(R.string.job_uploading)
    "OCR_RESERVED", "OCR_PROCESSING", "PROCESSING" -> stringResource(R.string.job_reading)
    "OCR_VALIDATED", "MATCHING" -> stringResource(R.string.job_matching)
    "AWAITING_USER_REVIEW" -> stringResource(R.string.job_ready_review)
    "CONFIRMED" -> stringResource(R.string.job_confirmed)
    "PAIRING_REQUIRED" -> stringResource(R.string.job_pairing_required)
    "OCR_FAILED" -> stringResource(R.string.job_ocr_retry)
    "MATCHING_FAILED" -> stringResource(R.string.job_matching_attention)
    "REJECTED" -> stringResource(R.string.job_rejected)
    else -> state.replace('_', ' ').lowercase().replaceFirstChar(Char::uppercase)
}

@Composable
private fun humanQualityFlags(flags: List<String>): String {
    val blur = stringResource(R.string.quality_blur)
    val glare = stringResource(R.string.quality_glare)
    val crop = stringResource(R.string.quality_crop)
    val rotated = stringResource(R.string.quality_rotated)
    return flags.joinToString(", ") { flag ->
        when (flag) {
            "BLUR_RISK" -> blur
            "GLARE_RISK" -> glare
            "CROPPING_RISK" -> crop
            "ROTATED_PAGE" -> rotated
            else -> flag.replace('_', ' ').lowercase()
        }
    }
}
