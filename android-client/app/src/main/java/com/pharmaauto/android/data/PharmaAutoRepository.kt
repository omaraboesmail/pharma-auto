package com.pharmaauto.android.data

import android.content.Context
import android.net.Uri
import androidx.work.Constraints
import androidx.work.Data
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import androidx.core.net.toUri
import com.pharmaauto.android.capture.AnalyzedPage
import com.pharmaauto.android.capture.DocumentQualityAnalyzer
import com.pharmaauto.android.network.ConnectorSessionRepository
import java.util.UUID
import kotlinx.coroutines.flow.Flow
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

class PharmaAutoRepository(
    private val context: Context,
    val sessions: ConnectorSessionRepository,
    private val database: PharmaAutoDatabase = PharmaAutoDatabase.get(context)
) {
    private val dao = database.invoiceDraftDao()
    private val analyzer = DocumentQualityAnalyzer(context)
    private val json = Json

    fun observeDrafts(): Flow<List<InvoiceDraftEntity>> = dao.observeDrafts()

    suspend fun analyzePage(
        uri: Uri,
        mimeType: String?,
        maximumPages: Int
    ): List<AnalyzedPage> = analyzer.analyze(uri, mimeType, maximumPages)

    fun deleteLocalCapture(uri: Uri) {
        if (uri.authority == "${context.packageName}.files") {
            runCatching { context.contentResolver.delete(uri, null, null) }
        }
    }

    suspend fun createDraft(pages: List<AnalyzedPage>): String {
        require(pages.isNotEmpty() && pages.size <= 100) {
            "An invoice must contain 1..100 pages."
        }
        val now = System.currentTimeMillis()
        val draftId = UUID.randomUUID().toString()
        val connectorId = sessions.pairedProfile()?.connectorId
            ?: error("Android device is not paired.")
        dao.insertDraft(
            InvoiceDraftEntity(
                draftId = draftId,
                connectorId = connectorId,
                remoteJobId = null,
                state = "LOCAL_DRAFT",
                expectedPageCount = pages.size,
                uploadedPageCount = 0,
                currentRevisionId = null,
                revisionJson = null,
                failureCode = null,
                createdAtEpochMillis = now,
                updatedAtEpochMillis = now
            )
        )
        dao.insertPages(pages.mapIndexed { index, page ->
            InvoicePageEntity(
                pageId = page.pageId,
                draftId = draftId,
                position = index + 1,
                contentUri = page.uri.toString(),
                mimeType = page.mimeType,
                sha256 = page.sha256,
                length = page.length,
                qualityFlagsJson = json.encodeToString(page.qualityFlags),
                uploaded = false
            )
        })
        return draftId
    }

    fun enqueueUpload(draftId: String) {
        val request = OneTimeWorkRequestBuilder<InvoiceUploadWorker>()
            .setInputData(Data.Builder().putString(InvoiceUploadWorker.DraftIdKey, draftId).build())
            .setConstraints(
                Constraints.Builder()
                    .setRequiredNetworkType(NetworkType.CONNECTED)
                    .build()
            )
            .addTag("invoice-upload")
            .build()
        WorkManager.getInstance(context).enqueueUniqueWork(
            "invoice-upload-$draftId",
            ExistingWorkPolicy.KEEP,
            request
        )
    }

    suspend fun retryUpload(draftId: String) {
        val draft = dao.getDraft(draftId) ?: error("Invoice draft does not exist.")
        val connectorId = sessions.pairedProfile()?.connectorId
            ?: error("Android device is not paired.")
        require(draft.connectorId == connectorId) {
            "Invoice draft belongs to a different local Connector."
        }
        dao.resetForRetry(draftId, System.currentTimeMillis())
        enqueueUpload(draftId)
    }

    suspend fun draft(draftId: String): DraftWithPages? = dao.getDraftWithPages(draftId)

    suspend fun purgeLocalCaptureFiles(draftId: String) {
        dao.getDraftWithPages(draftId)?.pages.orEmpty().forEach { page ->
            deleteLocalCapture(page.contentUri.toUri())
        }
    }

    val draftDao: InvoiceDraftDao
        get() = dao
}
