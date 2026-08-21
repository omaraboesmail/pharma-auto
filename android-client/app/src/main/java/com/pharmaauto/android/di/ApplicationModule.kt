package com.pharmaauto.android.di

import android.content.Context
import com.pharmaauto.android.data.PharmaAutoDatabase
import com.pharmaauto.android.data.PharmaAutoRepository
import com.pharmaauto.android.network.ConnectorClientFactory
import com.pharmaauto.android.network.ConnectorSessionRepository
import com.pharmaauto.android.security.ConnectorProfileStore
import com.pharmaauto.android.security.DeviceKeyManager
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

@Module
@InstallIn(SingletonComponent::class)
object ApplicationModule {
    @Provides
    @Singleton
    fun provideDatabase(@ApplicationContext context: Context): PharmaAutoDatabase =
        PharmaAutoDatabase.get(context)

    @Provides
    @Singleton
    fun provideSessions(@ApplicationContext context: Context): ConnectorSessionRepository =
        ConnectorSessionRepository(
            ConnectorProfileStore(context),
            DeviceKeyManager(),
            ConnectorClientFactory()
        )

    @Provides
    @Singleton
    fun provideRepository(
        @ApplicationContext context: Context,
        sessions: ConnectorSessionRepository,
        database: PharmaAutoDatabase
    ): PharmaAutoRepository = PharmaAutoRepository(context, sessions, database)
}
