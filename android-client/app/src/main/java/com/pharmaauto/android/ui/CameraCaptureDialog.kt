package com.pharmaauto.android.ui

import android.net.Uri
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageCapture
import androidx.camera.core.ImageCaptureException
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.core.content.ContextCompat
import androidx.core.content.FileProvider
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.pharmaauto.android.R
import java.io.File
import java.util.UUID

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun CameraCaptureDialog(
    onCaptured: (Uri) -> Unit,
    onDismiss: () -> Unit
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val previewView = remember {
        PreviewView(context).apply {
            implementationMode = PreviewView.ImplementationMode.COMPATIBLE
            scaleType = PreviewView.ScaleType.FIT_CENTER
        }
    }
    val imageCapture = remember {
        ImageCapture.Builder()
            .setCaptureMode(ImageCapture.CAPTURE_MODE_MAXIMIZE_QUALITY)
            .build()
    }
    var cameraProvider by remember { mutableStateOf<ProcessCameraProvider?>(null) }
    var error by remember { mutableStateOf<String?>(null) }
    var saving by remember { mutableStateOf(false) }

    DisposableEffect(context) {
        val future = ProcessCameraProvider.getInstance(context)
        val listener = Runnable {
            runCatching { future.get() }
                .onSuccess { cameraProvider = it }
                .onFailure { error = it.message }
        }
        future.addListener(listener, ContextCompat.getMainExecutor(context))
        onDispose { cameraProvider?.unbindAll() }
    }
    DisposableEffect(cameraProvider, lifecycleOwner, previewView) {
        val provider = cameraProvider
        if (provider != null) {
            runCatching {
                provider.unbindAll()
                val preview = Preview.Builder().build().also { useCase ->
                    useCase.surfaceProvider = previewView.surfaceProvider
                }
                provider.bindToLifecycle(
                    lifecycleOwner,
                    CameraSelector.DEFAULT_BACK_CAMERA,
                    preview,
                    imageCapture
                )
            }.onFailure { error = it.message }
        }
        onDispose { provider?.unbindAll() }
    }

    Dialog(
        onDismissRequest = { if (!saving) onDismiss() },
        properties = DialogProperties(
            usePlatformDefaultWidth = false,
            decorFitsSystemWindows = false
        )
    ) {
        Scaffold(
            modifier = Modifier.fillMaxSize(),
            contentWindowInsets = WindowInsets.safeDrawing,
            containerColor = Color.Black,
            topBar = {
                TopAppBar(
                    title = { Text(stringResource(R.string.capture_invoice_page)) },
                    navigationIcon = {
                        IconButton(onClick = onDismiss, enabled = !saving) {
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
            bottomBar = {
                Surface(color = MaterialTheme.colorScheme.surface, tonalElevation = 4.dp) {
                    Column(
                        Modifier.fillMaxWidth().padding(16.dp),
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.spacedBy(10.dp)
                    ) {
                        Text(
                            stringResource(R.string.camera_quality_guidance),
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )
                        error?.let { message ->
                            Text(
                                message,
                                color = MaterialTheme.colorScheme.error,
                                maxLines = 2
                            )
                        }
                        Button(
                            enabled = cameraProvider != null && !saving,
                            onClick = {
                                saving = true
                                error = null
                                val directory = File(
                                    context.filesDir,
                                    "invoice-captures"
                                ).apply { mkdirs() }
                                val file = File(directory, "capture-${UUID.randomUUID()}.jpg")
                                val output = ImageCapture.OutputFileOptions.Builder(file).build()
                                imageCapture.takePicture(
                                    output,
                                    ContextCompat.getMainExecutor(context),
                                    object : ImageCapture.OnImageSavedCallback {
                                        override fun onImageSaved(
                                            outputFileResults: ImageCapture.OutputFileResults
                                        ) {
                                            saving = false
                                            onCaptured(
                                                FileProvider.getUriForFile(
                                                    context,
                                                    "${context.packageName}.files",
                                                    file
                                                )
                                            )
                                        }

                                        override fun onError(exception: ImageCaptureException) {
                                            saving = false
                                            error = exception.message
                                            file.delete()
                                        }
                                    }
                                )
                            }
                        ) {
                            if (saving) {
                                CircularProgressIndicator(
                                    modifier = Modifier.width(22.dp),
                                    strokeWidth = 2.dp,
                                    color = MaterialTheme.colorScheme.onPrimary
                                )
                            } else {
                                Text(stringResource(R.string.capture_page_now))
                            }
                        }
                    }
                }
            }
        ) { padding ->
            Box(Modifier.fillMaxSize().padding(padding)) {
                AndroidView(
                    factory = { previewView },
                    modifier = Modifier.fillMaxSize()
                )
                Surface(
                    modifier = Modifier
                        .fillMaxSize()
                        .padding(horizontal = 18.dp, vertical = 28.dp),
                    color = Color.Transparent,
                    border = BorderStroke(2.dp, Color.White.copy(alpha = 0.8f)),
                    shape = MaterialTheme.shapes.medium
                ) {}
            }
        }
    }
}
