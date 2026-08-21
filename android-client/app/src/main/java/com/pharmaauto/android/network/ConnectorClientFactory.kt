package com.pharmaauto.android.network

import android.annotation.SuppressLint
import com.pharmaauto.android.BuildConfig
import com.pharmaauto.android.security.ConnectorProfile
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.cert.X509Certificate
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManager
import javax.net.ssl.X509TrustManager
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.kotlinx.serialization.asConverterFactory

class ConnectorClientFactory {
    private val json = Json {
        ignoreUnknownKeys = false
        explicitNulls = true
        coerceInputValues = false
        isLenient = false
    }

    fun create(profile: ConnectorProfile): LocalConnectorApi {
        val trustManager = CertificateHashTrustManager(profile.certificateSha256)
        val sslContext = SSLContext.getInstance("TLS").apply {
            init(null, arrayOf<TrustManager>(trustManager), SecureRandom())
        }
        val logging = HttpLoggingInterceptor().apply {
            level = if (BuildConfig.DEBUG) {
                HttpLoggingInterceptor.Level.BASIC
            } else {
                HttpLoggingInterceptor.Level.NONE
            }
            redactHeader("Authorization")
            redactHeader("X-Chunk-SHA256")
            redactHeader("X-Page-SHA256")
        }
        val client = OkHttpClient.Builder()
            .sslSocketFactory(sslContext.socketFactory, trustManager)
            .connectTimeout(java.time.Duration.ofSeconds(12))
            .readTimeout(java.time.Duration.ofMinutes(3))
            .writeTimeout(java.time.Duration.ofMinutes(3))
            .callTimeout(java.time.Duration.ofMinutes(4))
            .addInterceptor(logging)
            .build()
        return Retrofit.Builder()
            .baseUrl(profile.baseUrl)
            .client(client)
            .addConverterFactory(json.asConverterFactory("application/json".toMediaType()))
            .build()
            .create(LocalConnectorApi::class.java)
    }
}

@SuppressLint("CustomX509TrustManager")
private class CertificateHashTrustManager(expectedSha256: String) : X509TrustManager {
    private val expected = expectedSha256.uppercase()

    override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) {
        throw java.security.cert.CertificateException("Android never acts as a TLS client certificate server.")
    }

    override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
        val leaf = chain?.firstOrNull()
            ?: throw java.security.cert.CertificateException("Connector certificate is missing.")
        val actual = MessageDigest.getInstance("SHA-256")
            .digest(leaf.encoded)
            .joinToString("") { byte -> "%02X".format(byte) }
        if (!MessageDigest.isEqual(actual.toByteArray(), expected.toByteArray())) {
            throw java.security.cert.CertificateException("Connector certificate pin does not match pairing.")
        }
        leaf.checkValidity()
        if (authType.isNullOrBlank()) {
            throw java.security.cert.CertificateException("Connector TLS authentication type is missing.")
        }
    }

    override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
}
