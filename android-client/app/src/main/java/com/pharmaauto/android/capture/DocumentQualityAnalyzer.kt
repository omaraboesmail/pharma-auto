package com.pharmaauto.android.capture

import android.content.ContentResolver
import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.pdf.PdfRenderer
import android.net.Uri
import android.os.ParcelFileDescriptor
import androidx.core.content.FileProvider
import androidx.core.graphics.createBitmap
import androidx.exifinterface.media.ExifInterface
import java.security.MessageDigest
import java.io.ByteArrayOutputStream
import java.io.File
import kotlin.math.max
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

data class AnalyzedPage(
    val pageId: String,
    val uri: Uri,
    val mimeType: String,
    val sha256: String,
    val length: Long,
    val width: Int?,
    val height: Int?,
    val qualityFlags: List<String>
)

class DocumentQualityAnalyzer(private val context: Context) {
    private val resolver: ContentResolver = context.contentResolver

    suspend fun analyze(
        uri: Uri,
        claimedMimeType: String?,
        maximumPages: Int
    ): List<AnalyzedPage> = withContext(
        Dispatchers.IO
    ) {
        require(maximumPages in 1..100) { "No more invoice pages can be added." }
        val bytes = resolver.openInputStream(uri)?.use { input ->
            input.readAtMost(MaximumPageBytes)
        } ?: error("The selected page cannot be opened.")
        require(bytes.isNotEmpty() && bytes.size <= MaximumPageBytes) {
            "Each page must be between 1 byte and 20 MiB."
        }

        val actualMimeType = detectMime(bytes)
        require(claimedMimeType == null || claimedMimeType == actualMimeType) {
            "The selected file content does not match its MIME type."
        }
        if (actualMimeType == "application/pdf") {
            validatePdf(bytes)
            return@withContext renderPdfPages(bytes, maximumPages)
        }
        listOf(analyzeImage(uri, actualMimeType, bytes))
    }

    private fun analyzeImage(uri: Uri, actualMimeType: String, bytes: ByteArray): AnalyzedPage {
        val bounds = decodeBounds(bytes)
        val (width, height) = bounds
        require(width > 0 && height > 0 && width.toLong() * height <= MaximumPixels) {
            "Image dimensions are invalid or exceed 50 megapixels."
        }
        val flags = buildList {
            addAll(visualQualityFlags(bytes, bounds))
            if (actualMimeType == "image/jpeg" && isRotated(uri)) {
                add("ROTATED_PAGE")
            }
        }.distinct()

        return AnalyzedPage(
            pageId = java.util.UUID.randomUUID().toString(),
            uri = uri,
            mimeType = actualMimeType,
            sha256 = MessageDigest.getInstance("SHA-256")
                .digest(bytes)
                .joinToString("") { value -> "%02x".format(value) },
            length = bytes.size.toLong(),
            width = bounds.first,
            height = bounds.second,
            qualityFlags = flags
        )
    }

    private fun renderPdfPages(bytes: ByteArray, maximumPages: Int): List<AnalyzedPage> {
        val cacheFile = File(context.cacheDir, "pdf-${java.util.UUID.randomUUID()}.pdf")
        val createdFiles = mutableListOf<File>()
        try {
            cacheFile.writeBytes(bytes)
            ParcelFileDescriptor.open(cacheFile, ParcelFileDescriptor.MODE_READ_ONLY).use { descriptor ->
                PdfRenderer(descriptor).use { renderer ->
                    require(renderer.pageCount in 1..maximumPages) {
                        "The PDF has ${renderer.pageCount} pages; only $maximumPages more can be added."
                    }
                    return (0 until renderer.pageCount).map { pageIndex ->
                        renderer.openPage(pageIndex).use { page ->
                            val scale = minOf(
                                1f,
                                MaximumRenderedPdfDimension /
                                    max(page.width, page.height).coerceAtLeast(1).toFloat()
                            )
                            val width = max(1, (page.width * scale).toInt())
                            val height = max(1, (page.height * scale).toInt())
                            val bitmap = createBitmap(width, height, Bitmap.Config.ARGB_8888)
                            bitmap.eraseColor(android.graphics.Color.WHITE)
                            page.render(
                                bitmap,
                                null,
                                android.graphics.Matrix().apply { postScale(scale, scale) },
                                PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY
                            )
                            val jpeg = ByteArrayOutputStream().use { output ->
                                check(bitmap.compress(Bitmap.CompressFormat.JPEG, 92, output))
                                output.toByteArray()
                            }
                            bitmap.recycle()
                            val outputFile = File(
                                File(context.filesDir, "invoice-captures").apply { mkdirs() },
                                "pdf-${java.util.UUID.randomUUID()}-page-${pageIndex + 1}.jpg"
                            )
                            outputFile.writeBytes(jpeg)
                            createdFiles.add(outputFile)
                            val outputUri = FileProvider.getUriForFile(
                                context,
                                "${context.packageName}.files",
                                outputFile
                            )
                            analyzeImage(outputUri, "image/jpeg", jpeg)
                        }
                    }
                }
            }
        } catch (exception: Exception) {
            createdFiles.forEach(File::delete)
            throw exception
        } finally {
            cacheFile.delete()
        }
    }

    private fun visualQualityFlags(bytes: ByteArray, bounds: Pair<Int, Int>): List<String> {
        val sample = max(1, max(bounds.first, bounds.second) / 512)
        val bitmap = BitmapFactory.decodeByteArray(
            bytes,
            0,
            bytes.size,
            BitmapFactory.Options().apply { inSampleSize = sample }
        ) ?: return listOf("LOW_IMAGE_QUALITY")
        return bitmap.usePixels { pixels, width, height ->
            var adjacentEnergy = 0.0
            var adjacentCount = 0
            var glare = 0
            var edgeInk = 0
            for (y in 0 until height) {
                for (x in 0 until width) {
                    val gray = gray(pixels[y * width + x])
                    if (gray >= 248) glare++
                    if ((x == 0 || y == 0 || x == width - 1 || y == height - 1) && gray < 80) {
                        edgeInk++
                    }
                    if (x + 1 < width) {
                        val difference = gray - gray(pixels[y * width + x + 1])
                        adjacentEnergy += difference * difference
                        adjacentCount++
                    }
                    if (y + 1 < height) {
                        val difference = gray - gray(pixels[(y + 1) * width + x])
                        adjacentEnergy += difference * difference
                        adjacentCount++
                    }
                }
            }
            val sharpness = if (adjacentCount == 0) 0.0 else adjacentEnergy / adjacentCount
            val glareRatio = glare.toDouble() / (width * height).coerceAtLeast(1)
            val edgeLength = (width * 2 + height * 2 - 4).coerceAtLeast(1)
            buildList {
                if (sharpness < 70.0) add("BLUR_RISK")
                if (glareRatio > 0.22) add("GLARE_RISK")
                if (edgeInk.toDouble() / edgeLength > 0.08) add("CROPPING_RISK")
            }
        }
    }

    private fun isRotated(uri: Uri): Boolean = resolver.openInputStream(uri)?.use { input ->
        ExifInterface(input).rotationDegrees in setOf(90, 270)
    } ?: false

    private fun decodeBounds(bytes: ByteArray): Pair<Int, Int> {
        val options = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size, options)
        return options.outWidth to options.outHeight
    }

    private fun detectMime(bytes: ByteArray): String = when {
        bytes.size >= 8 && bytes.copyOfRange(0, 8).contentEquals(PngSignature) -> "image/png"
        bytes.size >= 4 && bytes[0] == 0xff.toByte() && bytes[1] == 0xd8.toByte() &&
            bytes[bytes.lastIndex - 1] == 0xff.toByte() && bytes.last() == 0xd9.toByte() ->
            "image/jpeg"
        bytes.size >= 8 && bytes.copyOfRange(0, 5).toString(Charsets.US_ASCII) == "%PDF-" ->
            "application/pdf"
        else -> error("Only JPEG, PNG, and PDF pages are supported.")
    }

    private fun validatePdf(bytes: ByteArray) {
        val text = bytes.toString(Charsets.ISO_8859_1)
        require(text.contains("%%EOF")) { "PDF end marker is missing." }
        val prohibited = listOf(
            "/Encrypt",
            "/EmbeddedFiles",
            "/JavaScript",
            "/JS",
            "/Launch",
            "/OpenAction",
            "/RichMedia"
        ).firstOrNull { marker -> text.contains(marker, ignoreCase = true) }
        require(prohibited == null) { "PDF contains unsupported active or embedded content." }
    }

    private inline fun <T> Bitmap.usePixels(block: (IntArray, Int, Int) -> T): T = try {
        val pixels = IntArray(width * height)
        getPixels(pixels, 0, width, 0, 0, width, height)
        block(pixels, width, height)
    } finally {
        recycle()
    }

    private fun gray(color: Int): Int {
        val red = color shr 16 and 0xff
        val green = color shr 8 and 0xff
        val blue = color and 0xff
        return (red * 299 + green * 587 + blue * 114) / 1000
    }

    companion object {
        private const val MaximumPageBytes = 20 * 1024 * 1024
        private const val MaximumPixels = 50_000_000L
        private const val MaximumRenderedPdfDimension = 2400f
        private val PngSignature = byteArrayOf(137.toByte(), 80, 78, 71, 13, 10, 26, 10)
    }
}
