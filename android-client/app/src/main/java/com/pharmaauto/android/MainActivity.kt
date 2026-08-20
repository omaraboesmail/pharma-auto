package com.pharmaauto.android

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import com.pharmaauto.android.ui.InvoiceReviewRoute
import com.pharmaauto.android.ui.PharmaAutoTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            PharmaAutoTheme {
                InvoiceReviewRoute()
            }
        }
    }
}
