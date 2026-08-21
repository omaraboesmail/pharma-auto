package com.pharmaauto.android

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import android.content.Intent
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.pharmaauto.android.ui.PharmaAutoApp
import com.pharmaauto.android.ui.PharmaAutoTheme
import dagger.hilt.android.AndroidEntryPoint

@AndroidEntryPoint
class MainActivity : ComponentActivity() {
    private var pairingPayload by mutableStateOf<String?>(null)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        pairingPayload = intent?.data?.toString()
        enableEdgeToEdge()
        setContent {
            PharmaAutoTheme {
                PharmaAutoApp(
                    incomingPairingPayload = pairingPayload,
                    onPairingPayloadConsumed = { pairingPayload = null }
                )
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        pairingPayload = intent.data?.toString()
    }
}
