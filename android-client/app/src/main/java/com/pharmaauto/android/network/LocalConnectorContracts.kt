package com.pharmaauto.android.network

import kotlinx.serialization.Serializable
import retrofit2.http.Body
import retrofit2.http.POST
import retrofit2.http.Path

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
    @POST(
        "api/v1/invoice-revisions/{revisionId}/posting-lines/{postingLineId}/" +
            "commercial-edit-preview"
    )
    suspend fun previewCommercialEdit(
        @Path("revisionId") revisionId: String,
        @Path("postingLineId") postingLineId: String,
        @Body request: CommercialEditPreviewRequest
    ): CommercialEditPreviewResponse
}
