package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.protocol.BinaryData
import com.rextechnologies.flint.protocol.wire.BrowserDarkMode
import com.rextechnologies.flint.protocol.wire.BrowserFaviconMessage
import com.rextechnologies.flint.protocol.wire.BrowserInteractionMode as WireInteractionMode
import com.rextechnologies.flint.protocol.wire.BrowserLoadState
import com.rextechnologies.flint.protocol.wire.BrowserNetworkStateMessage
import com.rextechnologies.flint.protocol.wire.BrowserProfileEntry
import com.rextechnologies.flint.protocol.wire.BrowserProfileSource as WireProfileSource
import com.rextechnologies.flint.protocol.wire.BrowserProfileStateMessage
import com.rextechnologies.flint.protocol.wire.BrowserSearchEngine as WireSearchEngine
import com.rextechnologies.flint.protocol.wire.BrowserTabStateEntry
import com.rextechnologies.flint.protocol.wire.BrowserTabStateMessage
import com.rextechnologies.flint.protocol.wire.BrowserUserAgentMode as WireUserAgentMode
import com.rextechnologies.flint.protocol.wire.BrowserViewStateMessage
import com.rextechnologies.flint.protocol.wire.BrowserVpnProvider as WireVpnProvider
import com.rextechnologies.flint.protocol.wire.BrowserVpnSessionState
import com.rextechnologies.flint.protocol.wire.BrowserWireLimits
import com.rextechnologies.flint.protocol.wire.BrowserWorkspacePaneStateEntry
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceStateMessage
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceWireInteractionMode
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceWireLayout
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceWireMuteApplication
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceWireObservedPlayback
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceWirePaneResidency
import com.rextechnologies.flint.protocol.wire.WireMessage
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceCapacity
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceInteractionMode
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceLayout
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceMediaState
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceMuteApplication
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspacePage
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspacePane
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspacePlaybackObservation
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceRendererResidency
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceState
import java.nio.charset.StandardCharsets

/** Builds monotonic full-replacement cockpit snapshots and hands them to the TLS-only writer. */
class BrowserCockpitPublisher(
    private val send: (WireMessage) -> Boolean,
) {
    private var tabRevision = 0L
    private var viewRevision = 0L
    private var libraryRevision = 0L
    private var profileRevision = 0L
    private var networkRevision = 0L
    private var workspaceRevision = 0L

    val currentWorkspaceRevision: Long
        get() = workspaceRevision

    fun publishTabs(page: BrowserState, tabs: BrowserTabsState, allowEmpty: Boolean = false): Boolean {
        val epoch = page.epoch ?: return false
        if (epoch <= 0) return false
        // Empty strips are suppressed by default so progress republishes do not wipe the host.
        // Mosaic entry must send an explicit empty snapshot so Windows drops the tab chrome.
        if (tabs.tabs.isEmpty() && !allowEmpty) return false
        tabRevision += 1
        return send(
            BrowserTabStateMessage(
                epoch = epoch,
                revision = tabRevision,
                activeTabId = if (tabs.tabs.isEmpty()) 0L else tabs.activeId,
                tabs = tabs.tabs.map { tab ->
                    BrowserTabStateEntry(
                        tabId = tab.id,
                        loadState = when {
                            tab.url.isBlank() -> BrowserLoadState.IDLE
                            tab.loading -> BrowserLoadState.LOADING
                            else -> BrowserLoadState.LOADED
                        },
                        progress = tab.progress.coerceIn(0, 100),
                        canGoBack = tab.canGoBack,
                        canGoForward = tab.canGoForward,
                        frozen = !tab.live,
                        faviconId = tab.faviconId,
                        url = tab.url,
                        title = tab.title,
                    )
                },
            ),
        )
    }

    fun publishView(
        page: BrowserState,
        view: BrowserViewState,
        fullscreen: Boolean,
        editingFocused: Boolean,
    ): Boolean {
        val epoch = page.epoch ?: return false
        if (epoch <= 0) return false
        viewRevision += 1
        return send(
            BrowserViewStateMessage(
                epoch = epoch,
                revision = viewRevision,
                zoomPercent = view.zoomPercent,
                userAgentMode = when (view.userAgentMode) {
                    BrowserUserAgentMode.DESKTOP -> WireUserAgentMode.DESKTOP
                    BrowserUserAgentMode.MOBILE -> WireUserAgentMode.MOBILE
                    BrowserUserAgentMode.TV -> WireUserAgentMode.TV
                },
                darkMode = if (view.darkModeEnabled) BrowserDarkMode.DARK else BrowserDarkMode.LIGHT,
                inputMode = when (view.inputMode) {
                    BrowserInteractionMode.FOCUS -> WireInteractionMode.FOCUS
                    BrowserInteractionMode.CURSOR -> WireInteractionMode.CURSOR
                },
                fullscreen = fullscreen,
                mediaPlaying = false,
                editingFocused = editingFocused,
                findActive = view.find.active,
                findCurrent = view.find.currentMatch,
                findTotal = view.find.totalMatches,
                searchEngine = when (view.searchEngine.id) {
                    "google" -> WireSearchEngine.GOOGLE
                    "bing" -> WireSearchEngine.BING
                    "duckduckgo" -> WireSearchEngine.DUCKDUCKGO
                    else -> WireSearchEngine.CUSTOM
                },
            ),
        )
    }

    fun publishLibrary(page: BrowserState, snapshot: BrowserProfileSessionSnapshot): Boolean {
        val epoch = page.epoch ?: return false
        if (epoch <= 0 || snapshot.profiles.activeSource != BrowserProfileSource.TV) return false
        libraryRevision += 1
        return send(snapshot.library.toWireMessage(epoch, libraryRevision))
    }

    fun publishProfiles(page: BrowserState, profiles: BrowserProfilesUiState): Boolean {
        val epoch = page.epoch ?: return false
        if (epoch <= 0 || profiles.tvProfiles.isEmpty()) return false
        profileRevision += 1
        return send(
            BrowserProfileStateMessage(
                epoch = epoch,
                revision = profileRevision,
                activeSource = if (profiles.activeSource == BrowserProfileSource.TV) {
                    WireProfileSource.TV
                } else {
                    WireProfileSource.DEVICE
                },
                activeProfileId = if (profiles.activeSource == BrowserProfileSource.TV) {
                    profiles.activeTvProfileId
                } else {
                    ""
                },
                deviceName = profiles.deviceName.orEmpty(),
                profiles = profiles.tvProfiles.map { BrowserProfileEntry(it.id, it.name) },
            ),
        )
    }

    /**
     * Publishes a network/VPN snapshot that never echoes [ProfileNetworkSettings.configText].
     * Config presence is a boolean only.
     */
    fun publishNetwork(
        page: BrowserState,
        profileId: String,
        settings: ProfileNetworkSettings,
        capability: VpnCapability,
        session: BrowserVpnState,
    ): Boolean {
        val epoch = page.epoch ?: return false
        if (epoch <= 0) return false
        networkRevision += 1
        val (sessionState, sessionDetail) = when (session) {
            BrowserVpnState.Idle -> BrowserVpnSessionState.IDLE to ""
            BrowserVpnState.NeedsConsent -> BrowserVpnSessionState.NEEDS_CONSENT to ""
            BrowserVpnState.Connecting -> BrowserVpnSessionState.CONNECTING to ""
            BrowserVpnState.TunnelUpUnverified -> BrowserVpnSessionState.CONNECTING to "Verifying VPN route"
            BrowserVpnState.Connected -> BrowserVpnSessionState.CONNECTED to ""
            is BrowserVpnState.Failed -> BrowserVpnSessionState.FAILED to boundDetail(session.message)
            BrowserVpnState.Unavailable -> BrowserVpnSessionState.UNAVAILABLE to ""
        }
        return send(
            BrowserNetworkStateMessage(
                epoch = epoch,
                revision = networkRevision,
                profileId = profileId,
                vpnEnabled = settings.vpnEnabled,
                provider = when (settings.provider) {
                    VpnProvider.WIREGUARD -> WireVpnProvider.WIREGUARD
                    VpnProvider.NONE -> WireVpnProvider.NONE
                },
                autoConnectOnBrowserStart = settings.autoConnectOnBrowserStart,
                requireVpnBeforeBrowse = settings.requireVpnBeforeBrowse,
                configPresent = settings.configText.isNotBlank(),
                capabilityPreparable = capability.preparable,
                capabilityReason = boundDetail(capability.reason),
                sessionState = sessionState,
                sessionDetail = sessionDetail,
            ),
        )
    }

    /** Publishes a bounded receiver-owned workspace snapshot for protocol v3 hosts. */
    fun publishWorkspace(
        page: BrowserState,
        state: BrowserWorkspaceState,
        capacity: BrowserWorkspaceCapacity,
    ): Boolean {
        val epoch = page.epoch ?: return false
        // An empty snapshot is the close notification. Suppressing it leaves host controls stale.
        if (epoch <= 0) return false
        workspaceRevision += 1
        return send(
            BrowserWorkspaceStateMessage(
                epoch = epoch,
                revision = workspaceRevision,
                layout = state.layout.toWireLayout(),
                focusedPaneId = state.focusedPaneId,
                interactionMode = state.interactionMode.toWireInteractionMode(),
                pageFullscreenPaneId = state.pageFullscreenPaneId ?: 0L,
                theaterPaneId = state.theaterPaneId ?: 0L,
                maxLiveRenderers = capacity.maxLiveRenderers,
                maxOpenPanes = capacity.maxOpenPanes,
                panes = state.panes.sortedBy(BrowserWorkspacePane::slot).map { pane ->
                    BrowserWorkspacePaneStateEntry(
                        paneId = pane.id,
                        slot = pane.slot,
                        residency = pane.rendererResidency.toWireResidency(),
                        // Wire forbids blank URLs on LIVE/FAILED panes. A just-opened mosaic page
                        // often has no address yet — publish about:blank so the host still learns
                        // pane count/layout (otherwise Split View leaves Windows stuck on tabs).
                        url = pane.page.url.ifBlank { "about:blank" },
                        title = pane.page.title,
                        loading = pane.page.loading,
                        progress = pane.page.progressPercent.coerceIn(0, 100),
                        canGoBack = pane.page.canGoBack,
                        canGoForward = pane.page.canGoForward,
                        desiredMuted = pane.media.desiredMuted,
                        muteApplication = pane.media.muteApplication.toWireMuteApplication(),
                        observedPlayback = pane.media.observedPlayback.toWireObservedPlayback(),
                    )
                },
            ),
        )
    }

    fun publishGeometry(page: BrowserState, state: BrowserWorkspaceState, visible: Boolean): Boolean {
        val epoch = page.epoch ?: return false
        if (epoch <= 0 || workspaceRevision <= 0) return false
        return send(com.rextechnologies.flint.protocol.wire.BrowserWorkspaceGeometryMessage(
            epoch, workspaceRevision, state.split.column.tenThousandths, state.split.row.tenThousandths, if (visible) 2 else 1,
        ))
    }

    fun publishFavicon(page: BrowserState, favicon: CachedBrowserFavicon): Boolean {
        val epoch = page.epoch ?: return false
        if (epoch <= 0) return false
        return send(
            BrowserFaviconMessage(
                epoch = epoch,
                faviconId = favicon.id,
                width = favicon.width,
                height = favicon.height,
                png = BinaryData.of(favicon.pngBytes),
            ),
        )
    }

    private fun boundDetail(value: String): String {
        val encoded = value.toByteArray(StandardCharsets.UTF_8)
        if (encoded.size <= BrowserWireLimits.MAX_DETAIL_BYTES) return value
        var end = BrowserWireLimits.MAX_DETAIL_BYTES
        while (end > 0 && (encoded[end - 1].toInt() and 0xC0) == 0x80) end -= 1
        if (end > 0 && (encoded[end - 1].toInt() and 0xC0) == 0xC0) end -= 1
        return String(encoded, 0, end, StandardCharsets.UTF_8)
    }

    private fun BrowserWorkspaceLayout.toWireLayout(): BrowserWorkspaceWireLayout = when (this) {
        BrowserWorkspaceLayout.SINGLE -> BrowserWorkspaceWireLayout.SINGLE
        BrowserWorkspaceLayout.SPLIT_HORIZONTAL -> BrowserWorkspaceWireLayout.TWO_COLUMNS
        BrowserWorkspaceLayout.SPLIT_VERTICAL -> BrowserWorkspaceWireLayout.TWO_ROWS
        BrowserWorkspaceLayout.GRID_2X2 -> BrowserWorkspaceWireLayout.FOUR_GRID
    }

    private fun BrowserWorkspaceInteractionMode.toWireInteractionMode(): BrowserWorkspaceWireInteractionMode =
        when (this) {
            BrowserWorkspaceInteractionMode.WORKSPACE_CHROME ->
                BrowserWorkspaceWireInteractionMode.WORKSPACE_CHROME
            BrowserWorkspaceInteractionMode.PAGE -> BrowserWorkspaceWireInteractionMode.PAGE
        }

    private fun BrowserWorkspaceRendererResidency.toWireResidency(): BrowserWorkspaceWirePaneResidency =
        when (this) {
            BrowserWorkspaceRendererResidency.LIVE -> BrowserWorkspaceWirePaneResidency.LIVE
            BrowserWorkspaceRendererResidency.SUSPENDED -> BrowserWorkspaceWirePaneResidency.SUSPENDED
            BrowserWorkspaceRendererResidency.FAILED -> BrowserWorkspaceWirePaneResidency.FAILED
        }

    private fun BrowserWorkspaceMuteApplication.toWireMuteApplication(): BrowserWorkspaceWireMuteApplication =
        when (this) {
            BrowserWorkspaceMuteApplication.NOT_REQUESTED -> BrowserWorkspaceWireMuteApplication.NOT_REQUESTED
            BrowserWorkspaceMuteApplication.PENDING_RENDERER ->
                BrowserWorkspaceWireMuteApplication.PENDING_RENDERER
            BrowserWorkspaceMuteApplication.REQUESTED -> BrowserWorkspaceWireMuteApplication.REQUESTED
            BrowserWorkspaceMuteApplication.APPLIED_TO_RENDERER ->
                BrowserWorkspaceWireMuteApplication.APPLIED_TO_RENDERER
            BrowserWorkspaceMuteApplication.UNSUPPORTED -> BrowserWorkspaceWireMuteApplication.UNSUPPORTED
            BrowserWorkspaceMuteApplication.FAILED -> BrowserWorkspaceWireMuteApplication.FAILED
        }

    private fun BrowserWorkspacePlaybackObservation.toWireObservedPlayback(): BrowserWorkspaceWireObservedPlayback =
        when (this) {
            BrowserWorkspacePlaybackObservation.UNKNOWN -> BrowserWorkspaceWireObservedPlayback.UNKNOWN
            BrowserWorkspacePlaybackObservation.PLAYING -> BrowserWorkspaceWireObservedPlayback.PLAYING
            BrowserWorkspacePlaybackObservation.PAUSED -> BrowserWorkspaceWireObservedPlayback.PAUSED
            BrowserWorkspacePlaybackObservation.ENDED -> BrowserWorkspaceWireObservedPlayback.ENDED
            BrowserWorkspacePlaybackObservation.UNAVAILABLE -> BrowserWorkspaceWireObservedPlayback.UNAVAILABLE
        }
}
