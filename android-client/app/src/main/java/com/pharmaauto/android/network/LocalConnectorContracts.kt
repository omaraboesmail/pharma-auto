package com.pharmaauto.android.network

import kotlinx.serialization.Serializable
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.Header
import retrofit2.http.POST
import retrofit2.http.PUT
import retrofit2.http.Path
import retrofit2.http.Query
import okhttp3.RequestBody
import okhttp3.ResponseBody

@Serializable
data class PercentageDiscountContract(
    val sequence: Int,
    val kind: String,
    val percentage: String,
    val applicationBasis: String,
    val affectsPurchaseUnitPrice: Boolean
)

@Serializable
data class CommercialValuesContract(
    val currency: String,
    val purchaseUnit: String,
    val purchaseUnitPrice: String,
    val purchasePriceTaxTreatment: String,
    val discounts: List<PercentageDiscountContract>,
    val sellingUnit: String,
    val sellingUnitPrice: String,
    val sellingPriceTaxTreatment: String,
    val sellingPriceScope: String,
    val existingStockPriceBehavior: String,
    val unsupportedScopeBehavior: String
)

@Serializable
data class CommercialEditPreviewRequest(
    val expectedRevisionId: String,
    val quantity: String,
    val commercialValues: CommercialValuesContract
)

@Serializable
data class CommercialEditPreviewResponse(
    val revisionId: String,
    val postingLineId: String,
    val currency: String,
    val grossPurchaseUnitPrice: String,
    val purchaseUnitPriceAfterDiscount1: String,
    val lineSubtotalAfterDiscount1: String,
    val netLineSubtotalAfterDiscount2: String,
    val sellingUnit: String,
    val sellingUnitPrice: String,
    val sellingPriceTaxTreatment: String,
    val sellingPriceScope: String,
    val existingStockPriceBehavior: String,
    val unsupportedScopeBehavior: String,
    val geniusWritePerformed: Boolean
)

interface LocalConnectorApi {
    @POST("api/v1/pairing/claim")
    suspend fun claimPairing(@Body request: PairingClaimRequest): PairingClaimResponse

    @POST("api/v1/auth/challenges")
    suspend fun createChallenge(
        @Body request: CreateChallengeRequest
    ): AccessChallengeResponse

    @POST("api/v1/auth/tokens")
    suspend fun exchangeToken(@Body request: ExchangeTokenRequest): AccessTokenResponse

    @POST("api/v1/invoice-jobs")
    suspend fun createInvoiceJob(
        @Header("Authorization") authorization: String,
        @Body request: CreateInvoiceJobRequest
    ): InvoiceJobResponse

    @PUT("api/v1/invoice-jobs/{jobId}/pages/{page}/chunks/{chunkIndex}")
    suspend fun uploadPageChunk(
        @Header("Authorization") authorization: String,
        @Header("X-Chunk-Count") chunkCount: Int,
        @Header("X-Chunk-SHA256") chunkSha256: String,
        @Header("X-Page-SHA256") pageSha256: String,
        @Header("X-Page-Mime-Type") pageMimeType: String,
        @Path("jobId") jobId: String,
        @Path("page") page: Int,
        @Path("chunkIndex") chunkIndex: Int,
        @Body body: RequestBody
    ): UploadPageStatusResponse

    @POST("api/v1/invoice-jobs/{jobId}/submit")
    suspend fun submitInvoiceJob(
        @Header("Authorization") authorization: String,
        @Path("jobId") jobId: String
    )

    @GET("api/v1/invoice-jobs/{jobId}")
    suspend fun getInvoiceJob(
        @Header("Authorization") authorization: String,
        @Path("jobId") jobId: String
    ): InvoiceJobResponse

    @GET("api/v1/invoice-revisions/{revisionId}")
    suspend fun getInvoiceRevision(
        @Header("Authorization") authorization: String,
        @Path("revisionId") revisionId: String
    ): ResponseBody

    @POST("api/v1/invoice-revisions/{revisionId}/edits")
    suspend fun saveInvoiceRevision(
        @Header("Authorization") authorization: String,
        @Path("revisionId") revisionId: String,
        @Body request: SaveRevisionRequest
    ): SavedRevisionResponse

    @POST("api/v1/invoice-revisions/{revisionId}/confirm")
    suspend fun confirmInvoiceRevision(
        @Header("Authorization") authorization: String,
        @Path("revisionId") revisionId: String
    ): ConfirmRevisionResponse

    @GET("api/v1/catalog/items/search")
    suspend fun searchItems(
        @Header("Authorization") authorization: String,
        @Query("query") query: String,
        @Query("vendorItemCode") vendorItemCode: String? = null,
        @Query("activeIngredient") activeIngredient: String? = null,
        @Query("strength") strength: String? = null,
        @Query("dosageForm") dosageForm: String? = null,
        @Query("pack") pack: String? = null,
        @Query("limit") limit: Int = 25
    ): CatalogSearchResponse

    @GET("api/v1/catalog/vendors/search")
    suspend fun searchVendors(
        @Header("Authorization") authorization: String,
        @Query("query") query: String,
        @Query("limit") limit: Int = 25
    ): VendorSearchResponse

    @POST(
        "api/v1/invoice-revisions/{revisionId}/posting-lines/{postingLineId}/" +
            "commercial-edit-preview"
    )
    suspend fun previewCommercialEdit(
        @Header("Authorization") authorization: String,
        @Path("revisionId") revisionId: String,
        @Path("postingLineId") postingLineId: String,
        @Body request: CommercialEditPreviewRequest
    ): CommercialEditPreviewResponse
}
