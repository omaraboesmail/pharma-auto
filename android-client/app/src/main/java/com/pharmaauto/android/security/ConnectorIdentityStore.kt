package com.pharmaauto.android.security

import android.content.Context
import android.net.Uri
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.Signature
import java.security.spec.ECGenParameterSpec
import java.util.Base64
import androidx.core.content.edit
import androidx.core.net.toUri
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

@Serializable
data class ConnectorProfile(
    val connectorId: String,
    val deviceId: String,
    val pharmacyDisplayName: String,
    val baseUrl: String,
    val certificateSha256: String
)

data class PairingQrPayload(
    val sessionId: String,
    val connectorId: String,
    val baseUrl: String,
    val certificateSha256: String,
    val oneTimeSecret: String
)

class ConnectorProfileStore(context: Context) {
    private val preferences = context.getSharedPreferences(
        "connector-profile-v1",
        Context.MODE_PRIVATE
    )
    private val json = Json { ignoreUnknownKeys = false }

    fun load(): ConnectorProfile? = preferences.getString(ProfileKey, null)?.let { encoded ->
        runCatching { json.decodeFromString<ConnectorProfile>(encoded) }.getOrNull()
    }

    fun save(profile: ConnectorProfile) {
        preferences.edit {
            putString(ProfileKey, json.encodeToString(ConnectorProfile.serializer(), profile))
        }
    }

    fun clear() {
        preferences.edit { remove(ProfileKey) }
    }

    companion object {
        private const val ProfileKey = "profile"
    }
}

object PairingQrParser {
    fun parse(value: String): PairingQrPayload {
        val uri = value.trim().toUri()
        require(uri.scheme == "pharmaauto" && uri.host == "pair") {
            "This is not a Pharma Auto pairing code."
        }
        require(uri.getQueryParameter("v") == "1") {
            "The pairing code version is unsupported."
        }
        val sessionId = uri.required("session")
        val connectorId = uri.required("connector")
        val baseUrl = uri.required("baseUrl").trimEnd('/') + "/"
        val certificateSha256 = uri.required("certificateSha256").uppercase()
        val secret = uri.required("secret")
        require(baseUrl.toUri().scheme == "https") {
            "Connector pairing requires HTTPS."
        }
        require(certificateSha256.matches(Regex("^[A-F0-9]{64}$"))) {
            "Connector certificate fingerprint is invalid."
        }
        require(secret.length in 32..128) {
            "One-time pairing secret is invalid."
        }
        return PairingQrPayload(
            sessionId,
            connectorId,
            baseUrl,
            certificateSha256,
            secret
        )
    }

    private fun Uri.required(name: String): String =
        getQueryParameter(name)?.takeIf { it.isNotBlank() }
            ?: throw IllegalArgumentException("Pairing code is missing $name.")
}

class DeviceKeyManager {
    private val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }

    fun publicKeySubjectPublicKeyInfoBase64(): String {
        ensureKey()
        val certificate = keyStore.getCertificate(KeyAlias)
            ?: error("Android Keystore device certificate is unavailable.")
        return Base64.getEncoder().encodeToString(certificate.publicKey.encoded)
    }

    fun signBase64(value: String): String {
        ensureKey()
        val privateKey = keyStore.getKey(KeyAlias, null)
            ?: error("Android Keystore device key is unavailable.")
        val signature = Signature.getInstance("SHA256withECDSA")
        signature.initSign(privateKey as java.security.PrivateKey)
        signature.update(value.toByteArray(Charsets.UTF_8))
        return Base64.getEncoder().encodeToString(
            derToP1363(signature.sign(), 32)
        )
    }

    private fun ensureKey() {
        if (keyStore.containsAlias(KeyAlias)) return
        val generator = KeyPairGenerator.getInstance(
            KeyProperties.KEY_ALGORITHM_EC,
            "AndroidKeyStore"
        )
        generator.initialize(
            KeyGenParameterSpec.Builder(
                KeyAlias,
                KeyProperties.PURPOSE_SIGN
            )
                .setAlgorithmParameterSpec(ECGenParameterSpec("secp256r1"))
                .setDigests(KeyProperties.DIGEST_SHA256)
                .setUserAuthenticationRequired(false)
                .build()
        )
        generator.generateKeyPair()
    }

    private fun derToP1363(der: ByteArray, fieldSize: Int): ByteArray {
        require(der.size >= 8 && der[0] == 0x30.toByte()) { "Invalid ECDSA signature." }
        var offset = 1
        val sequenceLength = readLength(der, offset)
        offset += sequenceLength.second
        require(sequenceLength.first == der.size - offset) { "Invalid ECDSA signature length." }
        require(der[offset++] == 0x02.toByte()) { "Invalid ECDSA R value." }
        val rLength = readLength(der, offset)
        offset += rLength.second
        val r = der.copyOfRange(offset, offset + rLength.first)
        offset += rLength.first
        require(der[offset++] == 0x02.toByte()) { "Invalid ECDSA S value." }
        val sLength = readLength(der, offset)
        offset += sLength.second
        val s = der.copyOfRange(offset, offset + sLength.first)
        return unsignedFixed(r, fieldSize) + unsignedFixed(s, fieldSize)
    }

    private fun readLength(bytes: ByteArray, offset: Int): Pair<Int, Int> {
        val first = bytes[offset].toInt() and 0xff
        if (first < 0x80) return first to 1
        val count = first and 0x7f
        require(count in 1..2) { "Unsupported DER length." }
        var value = 0
        repeat(count) { index -> value = (value shl 8) or (bytes[offset + 1 + index].toInt() and 0xff) }
        return value to (count + 1)
    }

    private fun unsignedFixed(value: ByteArray, size: Int): ByteArray {
        val unsigned = value.dropWhile { it == 0.toByte() }.toByteArray()
        require(unsigned.size <= size) { "ECDSA value exceeds P-256 width." }
        return ByteArray(size - unsigned.size) + unsigned
    }

    companion object {
        private const val KeyAlias = "pharma-auto-connector-device-v1"
    }
}
