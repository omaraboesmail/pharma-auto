package com.pharmaauto.android.network

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonElement

@Serializable
data class PairingClaimRequest(
    val sessionId: String,
    val oneTimeSecret: String,
    val deviceDisplayName: String,
    val publicKeySubjectPublicKeyInfoBase64: String
)

@Serializable
data class PairingClaimResponse(
    val deviceId: String,
    val connectorId: String,
    val pharmacyDisplayName: String,
    val baseUrl: String,
    val certificateSha256: String
)

@Serializable
data class CreateChallengeRequest(val deviceId: String)

@Serializable
data class AccessChallengeResponse(
    val challengeId: String,
    val deviceId: String,
    val nonce: String,
    val expiresAt: String,
    val consumedAt: String? = null
)

@Serializable
data class ExchangeTokenRequest(
    val deviceId: String,
    val challengeId: String,
    val signatureBase64: String
)

@Serializable
data class AccessTokenResponse(
    val accessToken: String,
    val expiresAt: String,
    val deviceId: String
)

@Serializable
data class CreateInvoiceJobRequest(val pageCount: Int)

@Serializable
data class InvoiceJobResponse(
    val schemaVersion: String,
    val jobId: String,
    val state: String,
    val deviceId: String,
    val pageCount: Int,
    val uploadedPageCount: Int,
    val currentRevisionId: String? = null,
    val createdAt: String,
    val updatedAt: String,
    val failureCode: String? = null,
    val geniusWritePerformed: Boolean
)

@Serializable
data class UploadPageStatusResponse(
    val page: Int,
    val complete: Boolean,
    val chunkCount: Int,
    val receivedChunks: List<Int>,
    val sha256: String? = null,
    val mimeType: String? = null
)

@Serializable
data class SaveRevisionRequest(
    val revision: JsonElement,
    val reason: String
)

@Serializable
data class SavedRevisionResponse(
    val revisionId: String,
    val revisionNumber: Int,
    val status: String,
    val geniusWritePerformed: Boolean
)

@Serializable
data class ConfirmRevisionResponse(
    val revisionId: String,
    val state: String,
    val commitAvailable: Boolean,
    val geniusWritePerformed: Boolean
)

@Serializable
data class CatalogSearchResponse(
    val candidates: List<LocalItemCandidateContract>,
    val finalLocalIdentitySelected: Boolean,
    val geniusWritePerformed: Boolean
)

@Serializable
data class LocalItemCandidateContract(
    val schemaVersion: String,
    val localItemReference: String,
    val displayLabel: String,
    val rawLabel: String? = null,
    val rawLabelHash: String? = null,
    val labelSource: String,
    val displayDirection: String,
    val qualityFlags: List<String>,
    val identifiers: CatalogIdentifiersContract,
    val attributes: CatalogAttributesContract,
    val reasonCodes: List<String>,
    val hardMismatches: List<String>,
    val requiresManualConfirmation: Boolean
)

@Serializable
data class CatalogAttributesContract(
    val activeIngredient: String? = null,
    val strength: String? = null,
    val dosageForm: String? = null,
    val pack: String? = null
)

@Serializable
data class CatalogIdentifiersContract(
    val itemCode: String? = null,
    val secondaryCode: String? = null,
    val internationalCode: String? = null,
    val barcodes: List<String>,
    @SerialName("vendorItemCodes") val vendorItemCodes: List<String>
)

@Serializable
data class VendorSearchResponse(
    val candidates: List<LocalVendorCandidateContract>,
    val finalLocalIdentitySelected: Boolean,
    val geniusWritePerformed: Boolean
)

@Serializable
data class LocalVendorCandidateContract(
    val localVendorReference: String,
    val displayName: String,
    val code: String? = null,
    val reasonCodes: List<String>,
    val requiresManualConfirmation: Boolean
)
