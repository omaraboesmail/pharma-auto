package com.pharmaauto.android.ui

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Matrix
import android.graphics.pdf.PdfRenderer
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.core.graphics.createBitmap
import androidx.core.net.toUri
import com.pharmaauto.android.R
import com.pharmaauto.android.capture.readAtMost
import java.io.File
import java.security.MessageDigest
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlin.math.max

data class SourcePageUi(
    val position: Int,
    val uri: String,
    val mimeType: String,
    val sha256: String
)

data class NormalizedBox(
    val x: Float,
    val y: Float,
    val width: Float,
    val height: Float
)

data class EvidenceRegion(
    val page: Int,
    val box: NormalizedBox?
)

data class SourceEvidenceTarget(
    val page: SourcePageUi,
    val region: EvidenceRegion?
)

private data class LoadedEvidence(
    val bitmap: Bitmap?,
    val failed: Boolean
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SourceEvidenceDialog(
    target: SourceEvidenceTarget,
    onDismiss: () -> Unit
) {
    val context = LocalContext.current
    val loaded by produceState(
        initialValue = LoadedEvidence(null, false),
        key1 = target.page.uri,
        key2 = target.page.mimeType
    ) {
        value = runCatching {
            LoadedEvidence(loadEvidenceBitmap(context, target.page), false)
        }.getOrElse { LoadedEvidence(null, true) }
    }
    Dialog(
        onDismissRequest = onDismiss,
        properties = DialogProperties(
            usePlatformDefaultWidth = false,
            decorFitsSystemWindows = false
        )
    ) {
        Scaffold(
            modifier = Modifier.fillMaxSize(),
            contentWindowInsets = WindowInsets.safeDrawing,
            topBar = {
                TopAppBar(
                    title = {
                        Text(
                            stringResource(R.string.source_evidence) +
                                " • " + target.page.position
                        )
                    },
                    navigationIcon = {
                        IconButton(onClick = onDismiss) {
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
            }
        ) { padding ->
            Box(
                modifier = Modifier.fillMaxSize().padding(padding),
                contentAlignment = Alignment.Center
            ) {
                when {
                    loaded.failed -> Text(
                        stringResource(R.string.source_page_unavailable),
                        color = MaterialTheme.colorScheme.error
                    )
                    loaded.bitmap == null -> CircularProgressIndicator()
                    else -> EvidenceBitmap(
                        bitmap = requireNotNull(loaded.bitmap),
                        box = target.region?.box
                    )
                }
            }
        }
    }
}

@Composable
private fun EvidenceBitmap(bitmap: Bitmap, box: NormalizedBox?) {
    Box(Modifier.fillMaxSize()) {
        Image(
            bitmap = bitmap.asImageBitmap(),
            contentDescription = stringResource(R.string.source_evidence),
            modifier = Modifier.fillMaxSize(),
            contentScale = ContentScale.Fit
        )
        if (box != null) {
            Canvas(Modifier.fillMaxSize()) {
                val bitmapAspect = bitmap.width.toFloat() / bitmap.height.coerceAtLeast(1)
                val containerAspect = size.width / size.height.coerceAtLeast(1f)
                val renderedWidth: Float
                val renderedHeight: Float
                if (containerAspect > bitmapAspect) {
                    renderedHeight = size.height
                    renderedWidth = renderedHeight * bitmapAspect
                } else {
                    renderedWidth = size.width
                    renderedHeight = renderedWidth / bitmapAspect
                }
                val left = (size.width - renderedWidth) / 2f
                val top = (size.height - renderedHeight) / 2f
                drawRect(
                    color = Color(0xFFFFB300),
                    topLeft = Offset(
                        left + box.x.coerceIn(0f, 1f) * renderedWidth,
                        top + box.y.coerceIn(0f, 1f) * renderedHeight
                    ),
                    size = Size(
                        box.width.coerceIn(0f, 1f) * renderedWidth,
                        box.height.coerceIn(0f, 1f) * renderedHeight
                    ),
                    style = Stroke(width = 4.dp.toPx())
                )
            }
        }
    }
}

private suspend fun loadEvidenceBitmap(
    context: android.content.Context,
    page: SourcePageUi
): Bitmap = withContext(Dispatchers.IO) {
    val uri = page.uri.toUri()
    val bytes = context.contentResolver.openInputStream(uri)?.use { stream ->
        stream.readAtMost(20 * 1024 * 1024)
    } ?: error("Source page is unavailable.")
    val actualHash = MessageDigest.getInstance("SHA-256")
        .digest(bytes)
        .joinToString("") { value -> "%02x".format(value) }
    require(actualHash == page.sha256) { "Source page changed after capture." }
    if (page.mimeType == "application/pdf") {
        val temporary = File(context.cacheDir, "evidence-${java.util.UUID.randomUUID()}.pdf")
        try {
            temporary.writeBytes(bytes)
            android.os.ParcelFileDescriptor.open(
                temporary,
                android.os.ParcelFileDescriptor.MODE_READ_ONLY
            ).use { descriptor ->
            PdfRenderer(descriptor).use { renderer ->
                require(renderer.pageCount > 0)
                renderer.openPage(0).use { pdfPage ->
                    val scale = minOf(1f, 1600f / max(pdfPage.width, pdfPage.height))
                    val width = max(1, (pdfPage.width * scale).toInt())
                    val height = max(1, (pdfPage.height * scale).toInt())
                    createBitmap(width, height, Bitmap.Config.ARGB_8888).also { bitmap ->
                        bitmap.eraseColor(android.graphics.Color.WHITE)
                        pdfPage.render(
                            bitmap,
                            null,
                            Matrix().apply { postScale(scale, scale) },
                            PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY
                        )
                    }
                }
            }
            }
        } finally {
            temporary.delete()
        }
    } else {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
        require(bounds.outWidth > 0 && bounds.outHeight > 0)
        var sample = 1
        while (max(bounds.outWidth, bounds.outHeight) / sample > 1800) sample *= 2
        BitmapFactory.decodeByteArray(
            bytes,
            0,
            bytes.size,
            BitmapFactory.Options().apply { inSampleSize = sample }
        ) ?: error("Source image could not be decoded.")
    }
}
