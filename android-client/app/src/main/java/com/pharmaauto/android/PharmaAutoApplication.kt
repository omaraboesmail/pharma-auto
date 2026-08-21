package com.pharmaauto.android

import android.app.Application
import com.pharmaauto.android.data.PharmaAutoRepository
import dagger.hilt.android.HiltAndroidApp
import javax.inject.Inject

@HiltAndroidApp
class PharmaAutoApplication : Application() {
    @Inject
    lateinit var repository: PharmaAutoRepository
}
