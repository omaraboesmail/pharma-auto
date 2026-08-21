package com.pharmaauto.android.data

import android.content.Context
import androidx.room.Dao
import androidx.room.Database
import androidx.room.Entity
import androidx.room.Index
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.Transaction
import androidx.room.Update
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase
import kotlinx.coroutines.flow.Flow

@Entity(
    tableName = "invoice_drafts",
    indices = [Index(value = ["remoteJobId"], unique = true)]
)
data class InvoiceDraftEntity(
    @androidx.room.PrimaryKey val draftId: String,
    val connectorId: String,
    val remoteJobId: String?,
    val state: String,
    val expectedPageCount: Int,
    val uploadedPageCount: Int,
    val currentRevisionId: String?,
    val revisionJson: String?,
    val failureCode: String?,
    val createdAtEpochMillis: Long,
    val updatedAtEpochMillis: Long
)

@Entity(
    tableName = "invoice_pages",
    indices = [Index(value = ["draftId", "position"])]
)
data class InvoicePageEntity(
    @androidx.room.PrimaryKey val pageId: String,
    val draftId: String,
    val position: Int,
    val contentUri: String,
    val mimeType: String,
    val sha256: String,
    val length: Long,
    val qualityFlagsJson: String,
    val uploaded: Boolean
)

data class DraftWithPages(
    @androidx.room.Embedded val draft: InvoiceDraftEntity,
    @androidx.room.Relation(
        parentColumn = "draftId",
        entityColumn = "draftId"
    )
    val pages: List<InvoicePageEntity>
)

@Dao
interface InvoiceDraftDao {
    @Query("SELECT * FROM invoice_drafts ORDER BY updatedAtEpochMillis DESC")
    fun observeDrafts(): Flow<List<InvoiceDraftEntity>>

    @Transaction
    @Query("SELECT * FROM invoice_drafts WHERE draftId = :draftId")
    suspend fun getDraftWithPages(draftId: String): DraftWithPages?

    @Query("SELECT * FROM invoice_drafts WHERE draftId = :draftId")
    suspend fun getDraft(draftId: String): InvoiceDraftEntity?

    @Insert(onConflict = OnConflictStrategy.ABORT)
    suspend fun insertDraft(draft: InvoiceDraftEntity)

    @Insert(onConflict = OnConflictStrategy.ABORT)
    suspend fun insertPages(pages: List<InvoicePageEntity>)

    @Update
    suspend fun updateDraft(draft: InvoiceDraftEntity)

    @Query(
        """
        UPDATE invoice_drafts
        SET remoteJobId = :remoteJobId,
            state = :state,
            updatedAtEpochMillis = :updatedAt
        WHERE draftId = :draftId
        """
    )
    suspend fun attachRemoteJob(
        draftId: String,
        remoteJobId: String,
        state: String,
        updatedAt: Long
    )

    @Query(
        """
        UPDATE invoice_pages
        SET uploaded = 1
        WHERE draftId = :draftId AND pageId = :pageId
        """
    )
    suspend fun markPageUploaded(draftId: String, pageId: String)

    @Query(
        """
        UPDATE invoice_drafts
        SET state = :state,
            uploadedPageCount = :uploadedPageCount,
            failureCode = :failureCode,
            updatedAtEpochMillis = :updatedAt
        WHERE draftId = :draftId
        """
    )
    suspend fun updateProgress(
        draftId: String,
        state: String,
        uploadedPageCount: Int,
        failureCode: String?,
        updatedAt: Long
    )

    @Query(
        """
        UPDATE invoice_drafts
        SET state = :state,
            currentRevisionId = :revisionId,
            revisionJson = :revisionJson,
            failureCode = NULL,
            updatedAtEpochMillis = :updatedAt
        WHERE draftId = :draftId
        """
    )
    suspend fun saveRevision(
        draftId: String,
        state: String,
        revisionId: String,
        revisionJson: String,
        updatedAt: Long
    )

    @Query(
        """
        UPDATE invoice_drafts
        SET state = :state,
            failureCode = :failureCode,
            updatedAtEpochMillis = :updatedAt
        WHERE draftId = :draftId
        """
    )
    suspend fun setState(
        draftId: String,
        state: String,
        failureCode: String?,
        updatedAt: Long
    )

    @Query(
        """
        UPDATE invoice_drafts
        SET remoteJobId = NULL,
            state = 'LOCAL_DRAFT',
            uploadedPageCount = 0,
            currentRevisionId = NULL,
            revisionJson = NULL,
            failureCode = NULL,
            updatedAtEpochMillis = :updatedAt
        WHERE draftId = :draftId
        """
    )
    suspend fun resetDraftForRetry(draftId: String, updatedAt: Long)

    @Query("UPDATE invoice_pages SET uploaded = 0 WHERE draftId = :draftId")
    suspend fun resetPagesForRetry(draftId: String)

    @Transaction
    suspend fun resetForRetry(draftId: String, updatedAt: Long) {
        resetDraftForRetry(draftId, updatedAt)
        resetPagesForRetry(draftId)
    }
}

@Database(
    entities = [InvoiceDraftEntity::class, InvoicePageEntity::class],
    version = 2,
    exportSchema = true
)
abstract class PharmaAutoDatabase : RoomDatabase() {
    abstract fun invoiceDraftDao(): InvoiceDraftDao

    companion object {
        @Volatile
        private var instance: PharmaAutoDatabase? = null

        fun get(context: Context): PharmaAutoDatabase = instance ?: synchronized(this) {
            instance ?: Room.databaseBuilder(
                context.applicationContext,
                PharmaAutoDatabase::class.java,
                "pharma-auto.db"
            )
                .addMigrations(MigrationOneToTwo)
                .build()
                .also { instance = it }
        }

        private val MigrationOneToTwo = object : Migration(1, 2) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL(
                    "ALTER TABLE invoice_drafts ADD COLUMN connectorId TEXT NOT NULL DEFAULT ''"
                )
            }
        }
    }
}
