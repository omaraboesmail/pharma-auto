package com.pharmaauto.android.ui

import android.content.Context
import android.net.Uri
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.pharmaauto.android.capture.AnalyzedPage
import com.pharmaauto.android.data.InvoiceDraftEntity
import com.pharmaauto.android.data.PharmaAutoRepository
import com.pharmaauto.android.R
import com.pharmaauto.android.security.ConnectorProfile
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

enum class AppScreen {
    Pairing,
    Inbox,
    Capture,
    Review
}

data class PharmaAutoAppUiState(
    val screen: AppScreen,
    val profile: ConnectorProfile?,
    val drafts: List<InvoiceDraftEntity> = emptyList(),
    val capturedPages: List<AnalyzedPage> = emptyList(),
    val selectedDraftId: String? = null,
    val busy: Boolean = false,
    val message: String? = null,
    val error: String? = null
)

@HiltViewModel
class PharmaAutoAppViewModel @Inject constructor(
    @ApplicationContext private val context: Context,
    private val repository: PharmaAutoRepository
) : ViewModel() {
    private val initialProfile = repository.sessions.pairedProfile()
    private val mutableState = MutableStateFlow(
        PharmaAutoAppUiState(
            screen = if (initialProfile == null) AppScreen.Pairing else AppScreen.Inbox,
            profile = initialProfile
        )
    )

    val uiState: StateFlow<PharmaAutoAppUiState> = mutableState.asStateFlow()
    private var latestDrafts: List<InvoiceDraftEntity> = emptyList()
    private val captureMutex = Mutex()

    init {
        viewModelScope.launch {
            repository.observeDrafts().collect { drafts ->
                latestDrafts = drafts
                mutableState.update { state ->
                    state.copy(drafts = drafts.forConnector(state.profile?.connectorId))
                }
            }
        }
    }

    fun acceptPairingPayload(payload: String) {
        if (payload.isBlank() || mutableState.value.busy) return
        viewModelScope.launch {
            mutableState.update { it.copy(busy = true, error = null, message = null) }
            runCatching { repository.sessions.claimPairing(payload) }
                .onSuccess { profile ->
                    mutableState.update {
                        it.copy(
                            screen = AppScreen.Inbox,
                            profile = profile,
                            drafts = latestDrafts.forConnector(profile.connectorId),
                            busy = false,
                            message = context.getString(
                                R.string.paired_with_pharmacy,
                                profile.pharmacyDisplayName
                            )
                        )
                    }
                }
                .onFailure {
                    mutableState.update {
                        it.copy(
                            busy = false,
                            error = context.getString(R.string.pairing_failed)
                        )
                    }
                }
        }
    }

    fun openCapture() {
        mutableState.update {
            it.copy(
                screen = AppScreen.Capture,
                capturedPages = emptyList(),
                message = null,
                error = null
            )
        }
    }

    fun forgetPairing() {
        repository.sessions.forgetPairing()
        mutableState.update {
            it.copy(
                screen = AppScreen.Pairing,
                profile = null,
                drafts = emptyList(),
                selectedDraftId = null,
                capturedPages = emptyList(),
                message = null,
                error = null
            )
        }
    }

    fun addCapturedPage(uri: Uri, mimeType: String?) {
        viewModelScope.launch {
            captureMutex.withLock {
                mutableState.update { it.copy(busy = true, error = null) }
                val remaining = 100 - mutableState.value.capturedPages.size
                runCatching { repository.analyzePage(uri, mimeType, remaining) }
                .onSuccess { newPages ->
                    mutableState.update { state ->
                        if (state.capturedPages.size + newPages.size > 100) {
                            newPages.forEach { page -> repository.deleteLocalCapture(page.uri) }
                            state.copy(
                                busy = false,
                                error = context.getString(R.string.invoice_page_limit)
                            )
                        } else {
                            state.copy(
                                busy = false,
                                capturedPages = state.capturedPages + newPages,
                                message = context.resources.getQuantityString(
                                    R.plurals.invoice_pages_added,
                                    newPages.size,
                                    newPages.size
                                )
                            )
                        }
                    }
                }
                .onFailure {
                    mutableState.update {
                        it.copy(
                            busy = false,
                            error = context.getString(R.string.selected_page_not_usable)
                        )
                        }
                    }
                }
            }
        }

    fun movePage(index: Int, direction: Int) {
        mutableState.update { state ->
            val target = index + direction
            if (index !in state.capturedPages.indices || target !in state.capturedPages.indices) {
                return@update state
            }
            val reordered = state.capturedPages.toMutableList().apply {
                add(target, removeAt(index))
            }
            state.copy(capturedPages = reordered, message = null)
        }
    }

    fun removePage(index: Int) {
        mutableState.update { state ->
            if (index !in state.capturedPages.indices) state else {
                repository.deleteLocalCapture(state.capturedPages[index].uri)
                state.copy(
                    capturedPages = state.capturedPages.filterIndexed { candidate, _ ->
                        candidate != index
                    },
                    message = null
                )
            }
        }
    }

    fun submitCapture() {
        val pages = mutableState.value.capturedPages
        if (pages.isEmpty() || mutableState.value.busy) return
        viewModelScope.launch {
            mutableState.update { it.copy(busy = true, error = null, message = null) }
            runCatching { repository.createDraft(pages) }
                .onSuccess { draftId ->
                    repository.enqueueUpload(draftId)
                    mutableState.update {
                        it.copy(
                            screen = AppScreen.Inbox,
                            capturedPages = emptyList(),
                            busy = false,
                            message = context.getString(R.string.invoice_queued_connector)
                        )
                    }
                }
                .onFailure {
                    mutableState.update {
                        it.copy(
                            busy = false,
                            error = context.getString(R.string.invoice_queue_failed)
                        )
                    }
                }
        }
    }

    fun openDraft(draftId: String) {
        val draft = mutableState.value.drafts.firstOrNull { it.draftId == draftId } ?: return
        if (draft.state != "AWAITING_USER_REVIEW" || draft.revisionJson == null) {
            mutableState.update {
                it.copy(
                    message = context.getString(R.string.invoice_not_ready_review),
                    error = null
                )
            }
            return
        }
        mutableState.update {
            it.copy(screen = AppScreen.Review, selectedDraftId = draftId, message = null, error = null)
        }
    }

    fun retryDraft(draftId: String) {
        if (mutableState.value.busy) return
        viewModelScope.launch {
            mutableState.update { it.copy(busy = true, message = null, error = null) }
            runCatching { repository.retryUpload(draftId) }
                .onSuccess {
                    mutableState.update {
                        it.copy(
                            busy = false,
                            message = context.getString(R.string.invoice_retry_queued)
                        )
                    }
                }
                .onFailure {
                    mutableState.update {
                        it.copy(
                            busy = false,
                            error = context.getString(R.string.invoice_retry_failed)
                        )
                    }
                }
        }
    }

    fun backToInbox() {
        mutableState.update {
            it.copy(screen = AppScreen.Inbox, selectedDraftId = null, message = null, error = null)
        }
    }

    fun consumeNotice() {
        mutableState.update { it.copy(message = null, error = null) }
    }

    private fun List<InvoiceDraftEntity>.forConnector(connectorId: String?): List<InvoiceDraftEntity> =
        if (connectorId == null) emptyList() else filter { draft ->
            draft.connectorId == connectorId
        }
}
