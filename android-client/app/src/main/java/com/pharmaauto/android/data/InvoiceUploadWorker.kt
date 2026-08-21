package com.pharmaauto.android.data

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import androidx.work.workDataOf
import androidx.core.net.toUri
import com.pharmaauto.android.PharmaAutoApplication
import com.pharmaauto.android.capture.readAtMost
import com.pharmaauto.android.network.CreateInvoiceJobRequest
import com.pharmaauto.android.network.PairingRequiredException
import java.io.IOException
import java.security.MessageDigest
import kotlin.math.ceil
import kotlinx.coroutines.delay
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody
import retrofit2.HttpException

class InvoiceUploadWorker(
    appContext: Context,
    workerParameters: WorkerParameters
) : CoroutineWorker(appContext, workerParameters) {
    private val repository = (appContext.applicationContext as PharmaAutoApplication).repository

    override suspend fun doWork(): Result {
        val draftId = inputData.getString(DraftIdKey) ?: return Result.failure()
        val draft = repository.draft(draftId) ?: return Result.failure()
        val profile = repository.sessions.pairedProfile()
        if (profile == null || draft.draft.connectorId != profile.connectorId) {
            repository.draftDao.setState(
                draftId,
                "PAIRING_REQUIRED",
                "PAIRING_REQUIRED",
                System.currentTimeMillis()
            )
            return Result.failure()
        }

        return try {
            val api = repository.sessions.api()
            var remoteJobId = draft.draft.remoteJobId
            if (remoteJobId == null) {
                val created = authenticated { authorization ->
                    api.createInvoiceJob(
                        authorization,
                        CreateInvoiceJobRequest(draft.draft.expectedPageCount)
                    )
                }
                remoteJobId = created.jobId
                repository.draftDao.attachRemoteJob(
                    draftId,
                    remoteJobId,
                    created.state,
                    System.currentTimeMillis()
                )
            }

            for (page in draft.pages.sortedBy { it.position }) {
                if (page.uploaded) continue
                val bytes = applicationContext.contentResolver
                    .openInputStream(page.contentUri.toUri())
                    ?.use { stream -> stream.readAtMost(MaximumPageBytes) }
                    ?: throw IOException("Captured page is no longer available.")
                require(bytes.size <= MaximumPageBytes) { "Captured page exceeds 20 MiB." }
                val actualHash = sha256(bytes)
                require(actualHash == page.sha256) { "Captured page changed after local review." }
                val chunkCount = ceil(bytes.size / ChunkBytes.toDouble()).toInt().coerceAtLeast(1)
                repeat(chunkCount) { chunkIndex ->
                    val start = chunkIndex * ChunkBytes
                    val end = minOf(bytes.size, start + ChunkBytes)
                    val chunk = bytes.copyOfRange(start, end)
                    authenticated { authorization ->
                        api.uploadPageChunk(
                            authorization = authorization,
                            chunkCount = chunkCount,
                            chunkSha256 = sha256(chunk),
                            pageSha256 = page.sha256,
                            pageMimeType = page.mimeType,
                            jobId = remoteJobId,
                            page = page.position,
                            chunkIndex = chunkIndex,
                            body = chunk.toRequestBody(page.mimeType.toMediaType())
                        )
                    }
                }
                repository.draftDao.markPageUploaded(draftId, page.pageId)
                repository.draftDao.updateProgress(
                    draftId,
                    "UPLOADING",
                    page.position,
                    null,
                    System.currentTimeMillis()
                )
                setProgress(
                    workDataOf(
                        "uploadedPages" to page.position,
                        "pageCount" to draft.draft.expectedPageCount
                    )
                )
            }

            authenticated { authorization -> api.submitInvoiceJob(authorization, remoteJobId) }
            repository.draftDao.updateProgress(
                draftId,
                "PROCESSING",
                draft.draft.expectedPageCount,
                null,
                System.currentTimeMillis()
            )
            repeat(30) {
                val job = authenticated { authorization ->
                    api.getInvoiceJob(authorization, remoteJobId)
                }
                repository.draftDao.updateProgress(
                    draftId,
                    job.state,
                    job.uploadedPageCount,
                    job.failureCode,
                    System.currentTimeMillis()
                )
                when (job.state) {
                    "AWAITING_USER_REVIEW" -> {
                        val revisionId = job.currentRevisionId
                            ?: error("Connector did not return a review revision.")
                        val revisionJson = authenticated { authorization ->
                            api.getInvoiceRevision(authorization, revisionId).string()
                        }
                        repository.draftDao.saveRevision(
                            draftId,
                            job.state,
                            revisionId,
                            revisionJson,
                            System.currentTimeMillis()
                        )
                        return Result.success()
                    }
                    "OCR_FAILED", "MATCHING_FAILED", "REJECTED" -> return Result.failure()
                }
                delay(2_000)
            }
            Result.retry()
        } catch (exception: HttpException) {
            handleHttpFailure(draftId, exception)
        } catch (_: PairingRequiredException) {
            repository.draftDao.setState(
                draftId,
                "PAIRING_REQUIRED",
                "PAIRING_REQUIRED",
                System.currentTimeMillis()
            )
            Result.failure()
        } catch (_: IOException) {
            Result.retry()
        } catch (exception: IllegalArgumentException) {
            repository.draftDao.setState(
                draftId,
                "REJECTED",
                exception.message?.take(128) ?: "LOCAL_VALIDATION",
                System.currentTimeMillis()
            )
            Result.failure()
        }
    }

    private suspend fun handleHttpFailure(draftId: String, exception: HttpException): Result {
        return if (exception.code() == 429 || exception.code() >= 500) {
            Result.retry()
        } else {
            repository.draftDao.setState(
                draftId,
                "REJECTED",
                "CONNECTOR_HTTP_${exception.code()}",
                System.currentTimeMillis()
            )
            Result.failure()
        }
    }

    private suspend fun <T> authenticated(block: suspend (String) -> T): T {
        return try {
            block(repository.sessions.authorization())
        } catch (exception: HttpException) {
            if (exception.code() != 401) throw exception
            block(repository.sessions.authorization(forceRefresh = true))
        }
    }

    private fun sha256(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256")
        .digest(bytes)
        .joinToString("") { value -> "%02x".format(value) }

    companion object {
        const val DraftIdKey = "draftId"
        private const val ChunkBytes = 4 * 1024 * 1024
        private const val MaximumPageBytes = 20 * 1024 * 1024
    }
}
