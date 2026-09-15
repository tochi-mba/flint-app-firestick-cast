package com.rextechnologies.flint.receiver

import android.util.Log
import com.rextechnologies.flint.receiver.browser.BrowserCockpitPublisher
import com.rextechnologies.flint.receiver.browser.BrowserCoordinator
import com.rextechnologies.flint.receiver.browser.BrowserProfileSession
import com.rextechnologies.flint.receiver.browser.BrowserState
import com.rextechnologies.flint.receiver.browser.BrowserTabSession
import com.rextechnologies.flint.receiver.browser.BrowserViewSettings
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceSession
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.update

/**
 * Everything the television tells the desktop about itself.
 *
 * Gathered here because these publishes share one rule that is easy to break separately: a cockpit
 * family is enabled on Windows only once a snapshot of it has actually arrived, so a family that
 * never publishes is a panel that never turns on — not a panel that shows an error. That failure is
 * silent on both ends, which is why the whole set is kept where it can be read at once.
 *
 * Every publish is wrapped: a snapshot rejected by the wire rules must never take down the browser
 * session that produced it. Idle snapshots carry epoch 0 and are not legal on the wire, so the
 * publisher skips rather than throws.
 */
internal class ReceiverBrowserPublishers(
    private val cockpitPublisher: BrowserCockpitPublisher,
    private val coordinator: BrowserCoordinator,
    private val uiState: MutableStateFlow<ReceiverUiState>,
    private val tabs: BrowserTabSession,
    private val viewSettings: BrowserViewSettings,
    private val profiles: BrowserProfileSession,
    private val editingFocused: () -> Boolean,
    private val workspaceSession: BrowserWorkspaceSession?,
    private val negotiatedProtocolVersion: () -> Int,
    private val publishNetwork: (String) -> Unit,
    private val onWorkspacePublished: (BrowserWorkspaceSession) -> Unit,
) {
    /** Projects the page and the tab strip onto the television's own surface. */
    fun browserUi(page: BrowserState) {
        uiState.update { current ->
            current.copy(
                browser = page.toSurfaceUi().copy(
                    tabs = tabs.state.tabs,
                    activeTabId = tabs.state.activeId,
                    // The dialog belongs to the surface, not to the page snapshot: a state update
                    // arriving mid-dialog must not dismiss it.
                    dialog = current.browser.dialog,
                ),
            )
        }
    }

    /** Publishes view settings to the television surface and to the host together. */
    fun viewState() {
        uiState.update { it.copy(browserView = viewSettings.state) }
        viewStateToHost()
    }

    fun tabStateToHost() {
        runCatching { cockpitPublisher.publishTabs(coordinator.snapshot(), tabs.state) }
            .onFailure { Log.w(TAG, "Browser tab state rejected by wire rules", it) }
    }

    fun viewStateToHost() {
        runCatching {
            cockpitPublisher.publishView(
                coordinator.snapshot(),
                viewSettings.state,
                uiState.value.browserFullscreen,
                editingFocused(),
            )
        }.onFailure { Log.w(TAG, "Browser view state rejected by wire rules", it) }
    }

    fun profileState() {
        val snapshot = profiles.snapshot()
        uiState.update {
            it.copy(browserProfiles = snapshot.profiles, browserLibrary = snapshot.library)
        }
        runCatching { cockpitPublisher.publishProfiles(coordinator.snapshot(), snapshot.profiles) }
            .onFailure { Log.w(TAG, "Browser profile state rejected by wire rules", it) }
    }

    fun libraryStateToHost() {
        runCatching { cockpitPublisher.publishLibrary(coordinator.snapshot(), profiles.snapshot()) }
            .onFailure { Log.w(TAG, "Browser library state rejected by wire rules", it) }
    }

    /** Publishes the active workspace only for v3 hosts that negotiated workspace support. */
    fun workspaceStateToHost() {
        if (negotiatedProtocolVersion() < ReceiverBrowserController.WORKSPACE_PROTOCOL_MINIMUM) {
            return
        }
        val session = workspaceSession ?: return
        runCatching {
            val ok = cockpitPublisher.publishWorkspace(
                page = coordinator.snapshot(),
                state = session.state,
                capacity = session.capacity,
            )
            if (ok && negotiatedProtocolVersion() >= 4) {
                cockpitPublisher.publishGeometry(coordinator.snapshot(), session.state, uiState.value.browserWorkspaceVisible)
            }
            if (ok) {
                Log.i(
                    TAG,
                    "Browser workspace published rev=${cockpitPublisher.currentWorkspaceRevision} " +
                        "layout=${session.state.layout} panes=${session.state.panes.size}",
                )
            } else {
                Log.w(
                    TAG,
                    "Browser workspace publish skipped epoch=${coordinator.snapshot().epoch} " +
                        "panes=${session.state.panes.size}",
                )
            }
        }.onFailure { Log.w(TAG, "Browser workspace state rejected by wire rules", it) }
        onWorkspacePublished(session)
    }

    /**
     * The whole cockpit at once, on session start.
     *
     * Network ships with the rest deliberately. Without an initial snapshot Windows never learns
     * the Network family exists, and the VPN paste box stays locked with nothing explaining why.
     */
    fun cockpitState() {
        // Establish profile ownership before publishing its page collections.
        profileState()
        tabStateToHost()
        viewStateToHost()
        libraryStateToHost()
        publishNetwork(profiles.snapshot().profiles.activeTvProfileId)
        workspaceStateToHost()
    }

    private companion object {
        const val TAG = "FlintBrowserPublish"
    }
}
