package com.pharmaauto.android.network

import android.os.Build
import com.pharmaauto.android.security.ConnectorProfile
import com.pharmaauto.android.security.ConnectorProfileStore
import com.pharmaauto.android.security.DeviceKeyManager
import com.pharmaauto.android.security.PairingQrParser
import java.time.Instant
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import retrofit2.HttpException

class PairingRequiredException : IllegalStateException(
    "The paired device identity was rejected or revoked."
)

class ConnectorSessionRepository(
    private val profileStore: ConnectorProfileStore,
    private val keyManager: DeviceKeyManager,
    private val clientFactory: ConnectorClientFactory
) {
    private val tokenMutex = Mutex()
    private var cachedToken: CachedToken? = null

    fun pairedProfile(): ConnectorProfile? = profileStore.load()

    suspend fun claimPairing(pairingPayload: String): ConnectorProfile {
        val pairing = PairingQrParser.parse(pairingPayload)
        val bootstrapProfile = ConnectorProfile(
            connectorId = pairing.connectorId,
            deviceId = "00000000-0000-0000-0000-000000000000",
            pharmacyDisplayName = "Pairing",
            baseUrl = pairing.baseUrl,
            certificateSha256 = pairing.certificateSha256
        )
        val response = clientFactory.create(bootstrapProfile).claimPairing(
            PairingClaimRequest(
                sessionId = pairing.sessionId,
                oneTimeSecret = pairing.oneTimeSecret,
                deviceDisplayName = Build.MODEL.take(120),
                publicKeySubjectPublicKeyInfoBase64 =
                    keyManager.publicKeySubjectPublicKeyInfoBase64()
            )
        )
        require(response.connectorId == pairing.connectorId) {
            "Connector identity changed during pairing."
        }
        require(response.certificateSha256.equals(pairing.certificateSha256, ignoreCase = true)) {
            "Connector certificate changed during pairing."
        }
        require(response.baseUrl.trimEnd('/') == pairing.baseUrl.trimEnd('/')) {
            "Connector URL changed during pairing."
        }
        return ConnectorProfile(
            connectorId = response.connectorId,
            deviceId = response.deviceId,
            pharmacyDisplayName = response.pharmacyDisplayName,
            baseUrl = response.baseUrl.trimEnd('/') + "/",
            certificateSha256 = response.certificateSha256.uppercase()
        ).also { profile ->
            profileStore.save(profile)
            cachedToken = null
        }
    }

    suspend fun authorization(forceRefresh: Boolean = false): String = tokenMutex.withLock {
        val now = Instant.now()
        val current = cachedToken
        if (!forceRefresh && current != null && current.expiresAt.isAfter(now.plusSeconds(30))) {
            return@withLock "Bearer ${current.value}"
        }

        val profile = profileStore.load() ?: error("Android device is not paired.")
        val api = clientFactory.create(profile)
        try {
            val challenge = api.createChallenge(CreateChallengeRequest(profile.deviceId))
            val canonical = listOf(
                "PHARMA_AUTO_DEVICE_AUTH_V1",
                challenge.challengeId,
                challenge.nonce,
                profile.connectorId,
                profile.deviceId
            ).joinToString("\n")
            val token = api.exchangeToken(
                ExchangeTokenRequest(
                    deviceId = profile.deviceId,
                    challengeId = challenge.challengeId,
                    signatureBase64 = keyManager.signBase64(canonical)
                )
            )
            val cached = CachedToken(token.accessToken, Instant.parse(token.expiresAt))
            cachedToken = cached
            "Bearer ${cached.value}"
        } catch (exception: HttpException) {
            if (exception.code() != 401) throw exception
            cachedToken = null
            profileStore.clear()
            throw PairingRequiredException()
        }
    }

    fun api(): LocalConnectorApi {
        val profile = profileStore.load() ?: error("Android device is not paired.")
        return clientFactory.create(profile)
    }

    fun forgetPairing() {
        cachedToken = null
        profileStore.clear()
    }

    private data class CachedToken(val value: String, val expiresAt: Instant)
}
