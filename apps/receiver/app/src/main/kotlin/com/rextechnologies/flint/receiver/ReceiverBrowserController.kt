package com.rextechnologies.flint.receiver

import android.graphics.Bitmap
import android.util.Log
import android.view.KeyEvent
import com.rextechnologies.flint.protocol.discovery.PairingCode
import com.rextechnologies.flint.protocol.wire.BrowserCommandAction
import com.rextechnologies.flint.protocol.wire.BrowserCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserDialogReplyMessage
import com.rextechnologies.flint.protocol.wire.BrowserInputMessage
import com.rextechnologies.flint.protocol.wire.BrowserLibraryAction
import com.rextechnologies.flint.protocol.wire.BrowserLibraryCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserLibraryStateMessage
import com.rextechnologies.flint.protocol.wire.BrowserNetworkCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserPointerAction
import com.rextechnologies.flint.protocol.wire.BrowserPreviewState
import com.rextechnologies.flint.protocol.wire.BrowserProfileCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserTabCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserViewCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceCommandAction
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceInputMessage
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceResizeMessage
import com.rextechnologies.flint.protocol.wire.ProtocolVersion
import com.rextechnologies.flint.protocol.wire.SurfaceMode
import com.rextechnologies.flint.receiver.browser.BrowserCockpitPublisher
import com.rextechnologies.flint.receiver.browser.BrowserCommandEffect
import com.rextechnologies.flint.receiver.browser.BrowserCommandRejection
import com.rextechnologies.flint.receiver.browser.BrowserCoordinator
import com.rextechnologies.flint.receiver.browser.BrowserDialogAnswer
import com.rextechnologies.flint.receiver.browser.BrowserFaviconCache
import com.rextechnologies.flint.receiver.browser.BrowserFaviconOffer
import com.rextechnologies.flint.receiver.browser.BrowserFindState
import com.rextechnologies.flint.receiver.browser.BrowserHostBridge
import com.rextechnologies.flint.receiver.browser.BrowserInputMapping
import com.rextechnologies.flint.receiver.browser.BrowserInputRouter
import com.rextechnologies.flint.receiver.browser.BrowserInteractionMode
import com.rextechnologies.flint.receiver.browser.BrowserLibraryStore
import com.rextechnologies.flint.receiver.browser.BrowserNativeInput
import com.rextechnologies.flint.receiver.browser.BrowserNetworkStore
import com.rextechnologies.flint.receiver.browser.BrowserNoticeReducer
import com.rextechnologies.flint.receiver.browser.BrowserNoticeState
import com.rextechnologies.flint.receiver.browser.BrowserPhase
import com.rextechnologies.flint.receiver.browser.BrowserPreviewPublisher
import com.rextechnologies.flint.receiver.browser.BrowserProfileActions
import com.rextechnologies.flint.receiver.browser.BrowserProfileSession
import com.rextechnologies.flint.receiver.browser.BrowserProfileSource
import com.rextechnologies.flint.receiver.browser.BrowserRefusal
import com.rextechnologies.flint.receiver.browser.BrowserRemoteCommandHandler
import com.rextechnologies.flint.receiver.browser.BrowserRemoteWorkspaceCommandHandler
import com.rextechnologies.flint.receiver.browser.BrowserSavedTab
import com.rextechnologies.flint.receiver.browser.BrowserSearchEngine
import com.rextechnologies.flint.receiver.browser.BrowserState
import com.rextechnologies.flint.receiver.browser.BrowserStateEvent
import com.rextechnologies.flint.receiver.browser.BrowserTabSession
import com.rextechnologies.flint.receiver.browser.BrowserTabSurfacePort
import com.rextechnologies.flint.receiver.browser.BrowserTvBrowsingSession
import com.rextechnologies.flint.receiver.browser.BrowserUserAgentMode
import com.rextechnologies.flint.receiver.browser.BrowserViewSettings
import com.rextechnologies.flint.receiver.browser.BrowserVpnConnectionVerifier
import com.rextechnologies.flint.receiver.browser.BrowserVpnCoordinator
import com.rextechnologies.flint.receiver.browser.BrowserVpnState
import com.rextechnologies.flint.receiver.browser.BrowserVpnTunnel
import com.rextechnologies.flint.receiver.browser.BrowserWebViewDriver
import com.rextechnologies.flint.receiver.browser.BrowserWorkspaceStore
import com.rextechnologies.flint.receiver.browser.FixedVpnCapabilityProbe
import com.rextechnologies.flint.receiver.browser.NoOpBrowserVpnTunnel
import com.rextechnologies.flint.receiver.browser.PendingJsDialog
import com.rextechnologies.flint.receiver.browser.ProfileNetworkSettings
import com.rextechnologies.flint.receiver.browser.TabEffect
import com.rextechnologies.flint.receiver.browser.TabTransition
import com.rextechnologies.flint.receiver.browser.TvSessionRestore
import com.rextechnologies.flint.receiver.browser.UnavailableBrowserVpnConnectionVerifier
import com.rextechnologies.flint.receiver.browser.VpnCapability
import com.rextechnologies.flint.receiver.browser.VpnCapabilityProbe
import com.rextechnologies.flint.receiver.browser.VpnConsentHost
import com.rextechnologies.flint.receiver.browser.VpnProvider
import com.rextechnologies.flint.receiver.browser.identity.ReceiverIdentityProvider
import com.rextechnologies.flint.receiver.browser.net.BrowserOutboundMessage
import com.rextechnologies.flint.receiver.browser.net.BrowserSecureSessionListener
import com.rextechnologies.flint.receiver.browser.net.BrowserTlsServer
import com.rextechnologies.flint.receiver.browser.toWireMessage
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceAction
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceProfile
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceSession
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.collect
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import java.net.Inet4Address
import java.net.URI

/**
 * Everything the receiver does for the TV-resident browser, kept out of [ReceiverService].
 *
 * The service owns a listening socket, two decoders, a player, notifications and a discovery
 * responder; the browser adds a second TLS listener, an identity, a coordinator, an input router, a
 * dialog bridge and a preview loop. Holding both in one class pushed it past a thousand lines and
 * made the browser's lifecycle impossible to read next to the cast session's. The service keeps the
 * surface decision — which of player, mirror and browser owns the glass — and delegates the rest
 * here.
 *
 * This class touches UI state but never a `WebView`, an `Activity` or a `Context`: the Activity
 * owns the view and attaches it through [attachWebView], exactly as ADR-0007 requires.
 */
class ReceiverBrowserController(
    private val scope: CoroutineScope,
    private val uiState: MutableStateFlow<ReceiverUiState>,
    private val identityProvider: ReceiverIdentityProvider,
    private val libraryStore: BrowserLibraryStore,
    private val pausePlayer: () -> Unit,
    private val networkStore: BrowserNetworkStore? = null,
    private val workspaceStore: BrowserWorkspaceStore? = null,
    private val workspaceSession: BrowserWorkspaceSession? = null,
    private val vpnProbe: VpnCapabilityProbe = FixedVpnCapabilityProbe(
        VpnCapability(preparable = false, reason = "VPN probe not configured"),
    ),
    private val vpnNeedsConsent: () -> Boolean = { false },
    private val vpnTunnel: BrowserVpnTunnel = NoOpBrowserVpnTunnel(),
    private val vpnVerifier: BrowserVpnConnectionVerifier = UnavailableBrowserVpnConnectionVerifier,
) {
    private var server: BrowserTlsServer? = null
    private var previewPublisher: BrowserPreviewPublisher? = null

    /**
     * Whether the host has asked for preview frames.
     *
     * Tracked here because the state message must say what is actually happening. It was previously
     * hardcoded to `DISABLED` on every outbound state, so a host that had switched preview on was
     * told preview was off in the same breath as receiving its frames.
     */
    private var previewEnabled: Boolean = false
    private var authenticatedBrowserSessionId: Long = 0

    /** Enters a workspace with the selected storage owner after releasing all tab renderers. */
    fun openWorkspaceFromTv(seedUrl: String? = coordinator.snapshot().address?.canonicalUrl) {
        if (uiState.value.browserWorkspaceVisible) return
        ensureVpnForActiveProfile()
        if (!allowBrowsingUnderVpnPolicy()) {
            pendingWorkspaceOpen = true
            return
        }
        pendingWorkspaceOpen = false
        val session = workspaceSession ?: return
        val selection = profiles.snapshot().profiles
        if (selection.activeSource == BrowserProfileSource.CONNECTED_DEVICE &&
            authenticatedBrowserSessionId == 0L
        ) {
            return
        }
        // Keep tab identities and URLs; the departing UI releases their renderers.
        // Keep an existing preview opt-in across the tab → mosaic handoff; reattach the focused
        // pane WebView once the workspace host has a renderer (publishWorkspaceStateToHost).
        hostBridge.onSessionEnded()
        if (session.isOpen()) {
            // Switching modes retains this profile's workspace rather than opening a new one.
        } else if (selection.activeSource == BrowserProfileSource.TV) {
            session.openLocalWorkspace(selection.activeTvProfileId, seedUrl)
        } else {
            session.dispatch(
                BrowserWorkspaceAction.ActivateProfile(
                    BrowserWorkspaceProfile.ConnectedDevice(authenticatedBrowserSessionId, selection.activeName),
                ),
            )
            session.dispatch(BrowserWorkspaceAction.OpenPane(seedUrl))
        }
        uiState.update { it.copy(browserWorkspaceVisible = true, browserFullscreen = false) }
        persistTvBrowsingSession()
        // Mosaic preview still needs epoch/sequence validation for Windows pointer/scroll.
        coordinator.snapshot().epoch?.let(inputRouter::enableForBrowserEpoch)
        if (previewEnabled) {
            setPreviewEnabled(true)
        }
        // Tell Windows the tab strip is gone — otherwise SPLIT VIEW leaves stale tabs + a single
        // preview while the TV already shows mosaic panes.
        runCatching {
            cockpitPublisher.publishTabs(coordinator.snapshot(), tabs.state, allowEmpty = true)
        }.onFailure { Log.w(TAG, "Empty tab strip after mosaic entry rejected by wire rules", it) }
        publishWorkspaceStateToHost()
    }

    fun closeWorkspaceFromTv() {
        pendingWorkspaceOpen = false
        workspaceSession?.saveLocalState()
        uiState.update { it.copy(browserWorkspaceVisible = false, browserFullscreen = false) }
        persistTvBrowsingSession()
        hostBridge.onSessionEnded()
        tabs.activePage()?.let { coordinator.activatePage(it) }
        if (tabs.state.tabs.isEmpty()) applyTabTransition(tabs.start(coordinator.snapshot()))
        publishBrowserUi(tabs.activePage() ?: coordinator.snapshot())
        publishTabStateToHost()
        publishWorkspaceStateToHost()
    }

    /**
     * First OpenPane from Windows while still on tabs: leave the tab strip and open mosaic.
     * Must never leave the host stuck in "WAITING" — always accept/reject loudly and republish.
     */
    private fun enterMosaicFromHostOpenPane(command: BrowserWorkspaceCommandMessage) {
        val page = coordinator.snapshot()
        if (command.epoch != page.epoch) {
            Log.w(
                TAG,
                "Workspace OpenPane entry rejected: epoch mismatch cmd=${command.epoch} page=${page.epoch}",
            )
            publishWorkspaceStateToHost()
            return
        }
        if (command.commandId <= page.lastAcceptedCommandId) {
            Log.w(
                TAG,
                "Workspace OpenPane entry rejected: stale cmdId=${command.commandId} lastAccepted=${page.lastAcceptedCommandId}",
            )
            publishWorkspaceStateToHost()
            return
        }
        if (!remoteCommands.accept(command.epoch, command.commandId, "workspace-open")) {
            publishWorkspaceStateToHost()
            return
        }
        val seed = command.url.takeIf { it.isNotBlank() }
            ?: page.address?.canonicalUrl
        openWorkspaceFromTv(seed)
        if (!uiState.value.browserWorkspaceVisible) {
            Log.w(TAG, "Workspace OpenPane entry failed: mosaic did not become visible")
            showNotice("Split view could not open on this TV. Check VPN or profile, then try again.")
            publishWorkspaceStateToHost()
            return
        }
        Log.i(
            TAG,
            "Workspace mosaic entered from host OpenPane panes=${workspaceSession?.state?.panes?.size ?: 0}",
        )
        publishWorkspaceStateToHost()
    }

    private val noticeReducer = BrowserNoticeReducer()
    private var noticeState = BrowserNoticeState()

    /** Per-tab page snapshots; WebViews remain Activity-owned through [tabSurface]. */
    private val tabs = BrowserTabSession()
    private val viewSettings = BrowserViewSettings()
    private var editingFocused: Boolean = false
    private val faviconCache = BrowserFaviconCache()
    private var activeViewSite: String? = null
    private var tabSurface: BrowserTabSurfacePort? = null
    private var attachedTabId: Long = 0
    private var attachedDriver: BrowserWebViewDriver? = null

    /** N TV-persistent libraries plus, only while authenticated, one memory-only device library. */
    private val profiles = BrowserProfileSession(
        store = libraryStore,
        sendToDevice = { command -> server?.send(BrowserOutboundMessage.Raw(command)) == true },
        onTvProfileDeleted = { profileId ->
            networkStore?.removeForDeletedTvProfile(profileId)
            workspaceStore?.removeForDeletedTvProfile(profileId)
        },
    )
    private val cockpitPublisher = BrowserCockpitPublisher { message ->
        server?.send(BrowserOutboundMessage.Raw(message)) == true
    }

    init {
        val profile = profiles.snapshot()
        uiState.update {
            it.copy(
                browserView = viewSettings.state,
                browserLibrary = profile.library,
                browserProfiles = profile.profiles,
            )
        }
    }

    /** Identifiers for navigation the television started itself, kept apart from the host's. */
    private var lastTvEpoch: Long = 0
    private var tvCommandId: Long = 0
    private var negotiatedProtocolVersion: Int = 0

    val coordinator = BrowserCoordinator(
        publish = { page ->
            tabs.recordActive(page)
            activateViewSite(page)
            publishBrowserUi(page)
            publishStateToHost(page)
        },
    )

    val inputRouter = BrowserInputRouter()

    private val remoteInput = BrowserRemoteInputController(
        coordinator = coordinator,
        inputRouter = inputRouter,
        lastPreviewFrameId = { previewPublisher?.lastPublishedFrameId ?: 0L },
    ) { cursor ->
        uiState.update { it.copy(browserCursor = cursor) }
    }

    private val profileActions = BrowserProfileActions(
        scope = scope,
        profiles = profiles,
        currentPage = coordinator::snapshot,
        resetPage = ::resetForProfileSwitch,
        publishProfile = ::publishProfileState,
        publishCockpit = ::publishCockpitState,
        showNotice = ::showNotice,
        persistSession = ::persistTvBrowsingSession,
    )

    private val publishers: ReceiverBrowserPublishers = ReceiverBrowserPublishers(
        cockpitPublisher = cockpitPublisher,
        coordinator = coordinator,
        uiState = uiState,
        tabs = tabs,
        viewSettings = viewSettings,
        profiles = profiles,
        editingFocused = { editingFocused },
        workspaceSession = workspaceSession,
        negotiatedProtocolVersion = { negotiatedProtocolVersion },
        publishNetwork = { profileId -> vpn.publishNetworkState(profileId) },
        onWorkspacePublished = ::attachWorkspacePreviewDriver,
    )

    private val vpn: ReceiverBrowserVpnController = ReceiverBrowserVpnController(
        scope = scope,
        coordinator = coordinator,
        cockpitPublisher = cockpitPublisher,
        networkStore = networkStore,
        activeProfileId = { profiles.snapshot().profiles.activeTvProfileId },
        showNotice = ::showNotice,
        probe = vpnProbe,
        tunnel = vpnTunnel,
        needsConsent = vpnNeedsConsent,
        verifier = vpnVerifier,
    )

    val vpnState get() = vpn.state

    @Volatile
    private var pendingWorkspaceOpen = false

    init {
        // A workspace open that was waiting on the tunnel resumes the moment it verifies, rather
        // than leaving the viewer to press the button again on a connection that already arrived.
        vpn.onConnected {
            if (pendingWorkspaceOpen && !uiState.value.browserWorkspaceVisible) {
                pendingWorkspaceOpen = false
                openWorkspaceFromTv(null)
            }
        }
        // A profile that requires a tunnel before browsing must lose its pages when the tunnel
        // goes, not just its next navigation. An open page keeps fetching, and those requests
        // would leave over whatever network remains.
        vpn.onRequiredTunnelLost {
            if (uiState.value.browserWorkspaceVisible || coordinator.snapshot().epoch != null) {
                showNotice("VPN connection lost — pages closed because this profile requires VPN.")
                closeFromTv()
            }
        }
    }

    fun attachVpnConsentHost(host: VpnConsentHost?) = vpn.attachConsentHost(host)

    fun networkSettingsForActiveProfile(): ProfileNetworkSettings = vpn.settingsForActiveProfile()

    fun vpnCapability(): VpnCapability = vpn.capability()

    fun toggleVpnEnabledForActiveProfile(): Boolean = vpn.toggleEnabled()

    fun toggleVpnAutoConnectForActiveProfile(): Boolean = vpn.toggleAutoConnect()

    fun toggleRequireVpnBeforeBrowseForActiveProfile(): Boolean = vpn.toggleRequireBeforeBrowse()

    /** Fail-closed gate: when the profile requires VPN, pages must not load until Connected. */
    fun allowBrowsingUnderVpnPolicy(): Boolean = vpn.allowBrowsing()

    fun clearVpnForActiveProfile() = vpn.clearForActiveProfile()

    fun connectVpnForActiveProfile() = vpn.connectForActiveProfile()

    /** Re-run profile-gated auto-connect (browser enter or workspace open). */
    fun ensureVpnForActiveProfile() = vpn.ensureForActiveProfile()

    private val remoteCommands = BrowserRemoteCommandHandler(
        coordinator = coordinator,
        tabs = tabs,
        viewSettings = viewSettings,
        applyTabs = ::applyHostTabTransition,
        reload = { attachedDriver?.reload() },
        setView = ::updateView,
        exitFullscreen = { exitFullscreen() },
        startFind = ::startFind,
        findNext = ::nextFind,
        findPrevious = ::previousFind,
        clearFind = ::clearFind,
        showNotice = ::showNotice,
        publishTabs = ::publishTabStateToHost,
        publishView = ::publishViewStateToHost,
        selectTvProfile = ::selectTvProfile,
        selectDevice = ::selectConnectedDeviceProfile,
        createTvProfile = ::createTvProfile,
        renameTvProfile = ::renameTvProfile,
        deleteTvProfile = ::deleteTvProfile,
        publishProfilesAndLibrary = {
            publishProfileState()
            publishLibraryStateToHost()
        },
        setNetwork = { profileId, settings ->
            networkStore?.put(profileId, settings) == true
        },
        clearNetwork = { profileId -> vpn.clearSettings(profileId) },
        publishNetwork = { profileId -> vpn.publishNetworkState(profileId) },
        activeTvProfileId = { profiles.snapshot().profiles.activeTvProfileId },
        previousNetwork = { profileId -> networkStore?.get(profileId) },
    )

    private val remoteWorkspaceCommands = workspaceSession?.let { session ->
        BrowserRemoteWorkspaceCommandHandler(
            workspaceSession = session,
            publishWorkspace = ::publishWorkspaceStateToHost,
            currentRevision = { cockpitPublisher.currentWorkspaceRevision },
            setWorkspaceMode = { visible -> if (visible) openWorkspaceFromTv() else closeWorkspaceFromTv() },
            accept = remoteCommands::accept,
        )
    }

    private val hostBridge = BrowserHostBridge(
        coordinator = coordinator,
        publishDialog = { dialog ->
            uiState.update { state -> state.copy(browser = state.browser.copy(dialog = dialog)) }
        },
        sendOutbound = { outbound -> server?.send(outbound) == true },
        onClearData = ::clearLibrary,
    )

    /** The negotiated TLS browser port, or 0 when the listener could not start. */
    val port: Int get() = server?.port ?: 0

    val sessionListener: BrowserSecureSessionListener = object : BrowserSecureSessionListener {
        override fun onSessionAuthenticated(sessionId: Long, deviceName: String) {
            onSessionAuthenticated(sessionId, deviceName, ProtocolVersion.CURRENT)
        }

        override fun onSessionAuthenticated(
            sessionId: Long,
            deviceName: String,
            negotiatedProtocolVersion: Int,
        ) {
            this@ReceiverBrowserController.negotiatedProtocolVersion = negotiatedProtocolVersion
            authenticatedBrowserSessionId = sessionId
            Log.i(
                TAG,
                "Secure browser session authenticated sessionId=$sessionId protocol=v$negotiatedProtocolVersion host=$deviceName",
            )
            profiles.connectDevice(sessionId, deviceName)
            // New host session ↔ new command stream. Keep the live browser epoch/surface so orphan
            // tabs stay selectable, but clear lastCommandId so Windows starting at cmdId=1 works.
            // Tab page snapshots must be clamped too — otherwise Select hits activatePage with a
            // watermark still above 0 and the TV shows "expired browser session" (2026-09-08).
            coordinator.resetHostCommandWatermark()
            tabs.clampCommandWatermark(0)
            publishProfileState()
            publishCockpitState()
            // If tabs survived the previous TLS drop, put the browser back on the glass — otherwise
            // Windows shows the strip while the TV idles on "PC CONNECTED".
            if (tabs.state.tabs.isNotEmpty() && coordinator.snapshot().phase != BrowserPhase.IDLE) {
                enterSurface()
                coordinator.snapshot().epoch?.let(inputRouter::enableForBrowserEpoch)
                // Re-attach the live tab's driver so preview/input bind to a real WebView again.
                tabs.state.activeId.takeIf { it != 0L }?.let { activateDriver(it) }
            }
        }

        override fun onCommand(command: BrowserCommandMessage) = handleCommand(command)

        override fun onInput(input: BrowserInputMessage) {
            if (!allowBrowsingUnderVpnPolicy()) return
            if (uiState.value.browserWorkspaceVisible) {
                handleWorkspacePreviewInput(input)
                return
            }
            remoteInput.handle(input)
        }

        override fun onTabCommand(command: BrowserTabCommandMessage) {
            if (!uiState.value.browserWorkspaceVisible && allowBrowsingUnderVpnPolicy()) remoteCommands.handle(command)
        }

        override fun onViewCommand(command: BrowserViewCommandMessage) = remoteCommands.handle(command)

        override fun onLibraryCommand(command: BrowserLibraryCommandMessage) = handleLibraryCommand(command)

        override fun onLibraryState(state: BrowserLibraryStateMessage) {
            val epoch = coordinator.snapshot().epoch ?: return
            val switchingProfile = profiles.snapshot().profiles.switchingToDevice
            if (profiles.acceptDeviceSnapshot(state, epoch)) {
                if (switchingProfile) resetForProfileSwitch()
                publishProfileState()
                publishCockpitState()
            }
        }

        override fun onProfileCommand(command: BrowserProfileCommandMessage) = remoteCommands.handle(command)

        override fun onNetworkCommand(command: BrowserNetworkCommandMessage) = remoteCommands.handle(command)

        override fun onWorkspaceCommand(command: BrowserWorkspaceCommandMessage) {
            if (!allowBrowsingUnderVpnPolicy()) {
                Log.w(TAG, "Workspace command ignored — VPN policy blocks browsing action=${command.action}")
                return
            }
            if (command.action == BrowserWorkspaceCommandAction.OPEN_PANE &&
                !uiState.value.browserWorkspaceVisible
            ) {
                enterMosaicFromHostOpenPane(command)
                return
            }
            remoteWorkspaceCommands?.handle(command)
        }

        override fun onWorkspaceResize(input: BrowserWorkspaceResizeMessage) {
            remoteWorkspaceCommands?.handle(input)
        }

        override fun onWorkspaceInput(input: BrowserWorkspaceInputMessage) {
            if (uiState.value.browserWorkspaceVisible &&
                allowBrowsingUnderVpnPolicy()
            ) {
                remoteWorkspaceCommands?.handle(input)
            }
        }

        override fun onDialogReply(reply: BrowserDialogReplyMessage) {
            val prompt = reply.promptText
            val answer = if (reply.accepted) {
                if (prompt != null) BrowserDialogAnswer.Prompt(prompt) else BrowserDialogAnswer.Confirm
            } else {
                BrowserDialogAnswer.Cancel
            }
            hostBridge.replyFromHost(reply.epoch, reply.dialogId, answer)
        }

        override fun onSessionEnded(sessionId: Long) {
            if (sessionId != authenticatedBrowserSessionId) return
            authenticatedBrowserSessionId = 0
            hostBridge.onSessionEnded()
            setPreviewEnabled(false)
            negotiatedProtocolVersion = 0
            // The controller is optional for TV-owned browsing. If its ephemeral profile was on
            // screen, wipe it and return to the last TV profile without closing the browser.
            if (profiles.disconnectDevice(sessionId) && coordinator.snapshot().phase != BrowserPhase.IDLE) {
                resetForProfileSwitch()
            }
            publishProfileState()
            Log.i(TAG, "Secure browser session ended")
        }

        override fun onSessionEnded() = Unit
    }

    /**
     * Starts the TLS browser listener on [address], publishing its port and fingerprint.
     *
     * Returns the listening port, or 0 when the browser is unavailable on this device. A missing
     * identity is reported rather than swallowed: it decides whether the feature exists at all, and
     * a quiet null made a Keystore failure indistinguishable from a device that never had a browser.
     */
    fun start(address: Inet4Address, pairingCodeProvider: () -> PairingCode): Int {
        close()
        val identity = runCatching { identityProvider.obtain() }
            .onFailure { failure -> Log.w(TAG, "Browser identity unavailable; browser disabled", failure) }
            .getOrNull()
        if (identity == null) {
            Log.w(TAG, "Browser TLS listener not started: no receiver identity")
            publishUnavailable()
            return 0
        }

        val candidate = BrowserTlsServer(
            address = address,
            identity = identity,
            pairingCodeProvider = pairingCodeProvider,
            listener = sessionListener,
        )
        val started = candidate.start()
        if (started.isFailure) {
            Log.w(TAG, "Browser TLS listener unavailable", started.exceptionOrNull())
            candidate.close()
            publishUnavailable()
            return 0
        }

        server = candidate
        previewPublisher = BrowserPreviewPublisher(candidate).also(hostBridge::bindPreviewPublisher)
        uiState.update {
            it.copy(browserPort = candidate.port, browserFingerprint = identity.fingerprint.displayCode)
        }
        return candidate.port
    }

    fun close() {
        server?.close()
        server = null
        previewPublisher = null
        previewEnabled = false
        hostBridge.bindPreviewPublisher(null)
    }

    /**
     * Tells the viewer about something the browser refused to do.
     *
     * Policy denied these long before anything showed them; a blocked link that produces no
     * feedback is indistinguishable from a broken browser.
     */
    fun showRefusal(refusal: BrowserRefusal) {
        noticeState = noticeReducer.show(noticeState, refusal, System.currentTimeMillis())
        publishNotice()
    }

    /**
     * Opens an address the television asked for itself.
     *
     * Until now a browser session could only begin because a host sent OPEN with an epoch it owned,
     * which made the desktop a prerequisite for browsing at all. The television mints its own epoch
     * when there is no live session, so the remote alone is enough to start.
     *
     * Millisecond-based so a TV-minted epoch always beats the receiver's last observed one, the same
     * rule the host follows when it re-opens after a close.
     */
    fun navigateFromTv(url: String) {
        if (!allowBrowsingUnderVpnPolicy()) return
        if (uiState.value.browserWorkspaceVisible) {
            workspaceSession?.state?.focusedPaneId?.let { id ->
                workspaceSession.dispatch(BrowserWorkspaceAction.NavigatePane(id, url))
            }
            return
        }
        val page = coordinator.snapshot()
        val epoch = page.epoch?.takeIf { it > 0 && page.phase != BrowserPhase.IDLE }
        if (epoch == null) {
            val minted = maxOf(lastTvEpoch + 1, System.currentTimeMillis())
            lastTvEpoch = minted
            tvCommandId = 1
            val effect = coordinator.handleOpen(minted, tvCommandId, url)
            if (effect is BrowserCommandEffect.Open) {
                restoreTvSessionAfterStart(keepCurrentPage = true)
                inputRouter.enableForBrowserEpoch(minted)
                enterSurface()
            } else {
                showNotice("That address could not be opened.")
            }
            return
        }
        tvCommandId = maxOf(tvCommandId, page.lastAcceptedCommandId) + 1
        val effect = coordinator.handleNavigate(epoch, tvCommandId, url)
        if (effect is BrowserCommandEffect.Rejected) {
            showNotice("That address could not be opened.")
        }
    }

    /** Closes the browser from the television, with no host command to answer. */
    fun closeFromTv() {
        persistTvBrowsingSession()
        val page = coordinator.snapshot()
        val epoch = page.epoch
        if (epoch != null && epoch > 0) {
            coordinator.handleClose(epoch, maxOf(tvCommandId, page.lastAcceptedCommandId) + 1)
        }
        inputRouter.disable()
        clearTabs()
        leaveSurface()
    }

    /** Shows an arbitrary sentence, for things that are not a [BrowserRefusal]. */
    fun showNotice(message: String) {
        noticeState = noticeReducer.show(noticeState, message, System.currentTimeMillis())
        publishNotice()
    }

    /** Withdraws the current notice once the surface's timer has run out. */
    fun dismissNotice() {
        noticeState = noticeReducer.dismiss(noticeState)
        publishNotice()
    }

    /** Reports whether the page is filling the screen, so chrome and cursor can stand down. */
    fun setFullscreen(active: Boolean) {
        uiState.update { it.copy(browserFullscreen = active) }
    }

    /**
     * Reports that a page text field gained or lost focus.
     *
     * Published to the host so the desktop can start forwarding keystrokes on its own. The field's
     * *contents* are never sent — the desktop needs to know where typing will land, not what is
     * already in the box.
     */
    fun setEditing(active: Boolean) {
        if (editingFocused == active) {
            return
        }
        editingFocused = active
        publishViewStateToHost()
    }

    private fun publishNotice() {
        val active = noticeState.active
        uiState.update {
            it.copy(browserNotice = active?.let { notice -> BrowserNoticeUi(notice.id, notice.message) })
        }
    }

    fun attachWebView(driver: BrowserWebViewDriver) = hostBridge.attachDriver(driver)

    fun detachWebView(driver: BrowserWebViewDriver) = hostBridge.detachDriver(driver)

    /** Attaches the Activity-owned multi-tab renderer and rebuilds its current tab set. */
    fun attachTabSurface(surface: BrowserTabSurfacePort) {
        scope.launch {
            if (uiState.value.browserWorkspaceVisible) return@launch
            if (tabSurface === surface) return@launch
            detachAttachedDriver()
            tabSurface = surface

            // Activity recreation discards every WebView and saved Bundle with the old view tree.
            // Recreate each tab from its bounded model; frozen tabs are created and immediately
            // frozen so their fallback URL exists without keeping eight renderers alive at once.
            tabs.state.tabs.forEach { tab ->
                surface.apply(TabEffect.Create(tab.id, tab.url.ifBlank { null }))
                if (!tab.live) surface.apply(TabEffect.Freeze(tab.id))
            }
            tabs.state.active?.let { active ->
                surface.apply(TabEffect.Show(active.id))
                activateDriver(active.id)
            }
        }
    }

    /** Opens a real new-tab surface using only the television and its last selected TV profile. */
    fun openFromTv() {
        if (!allowBrowsingUnderVpnPolicy()) {
            vpn.maybeAutoConnect()
            return
        }
        val page = coordinator.snapshot()
        if (page.phase != BrowserPhase.IDLE && page.epoch != null) {
            enterSurface()
            return
        }
        val epoch = mintTvEpoch()
        tvCommandId = 1
        val effect = coordinator.handleOpenBlank(epoch, tvCommandId)
        if (effect is BrowserCommandEffect.OpenBlank) {
            val reopenWorkspace = restoreTvSessionAfterStart(keepCurrentPage = false)
            inputRouter.enableForBrowserEpoch(epoch)
            enterSurface()
            publishCockpitState()
            if (reopenWorkspace) openWorkspaceFromTv(null)
        } else {
            showNotice("The TV browser could not be opened.")
        }
    }

    fun selectTvProfile(profileId: String) {
        profileActions.selectTv(profileId)
    }

    fun selectConnectedDeviceProfile() = profileActions.selectDevice()

    fun createTvProfile(name: String) = profileActions.createTv(name)

    fun renameTvProfile(profileId: String, name: String) = profileActions.renameTv(profileId, name)

    fun deleteTvProfile(profileId: String) = profileActions.deleteTv(profileId)

    fun detachTabSurface(surface: BrowserTabSurfacePort) {
        scope.launch {
            if (tabSurface !== surface) return@launch
            detachAttachedDriver()
            tabSurface?.setPageFocusEnabled(true)
            tabSurface = null
        }
    }

    /** While Compose chrome owns the remote, the WebView must not keep Android view focus. */
    fun setPageFocusEnabled(enabled: Boolean) {
        tabSurface?.setPageFocusEnabled(enabled)
    }

    /** Page callbacks are keyed before they cross from an Activity-owned renderer. */
    fun onTabStateEvent(tabId: Long, event: BrowserStateEvent) {
        if (tabId == tabs.state.activeId) {
            coordinator.onStateEvent(event)
            if (event is BrowserStateEvent.PageFinished) recordVisit(tabId, coordinator.snapshot())
            publishTabStateToHost()
            return
        }
        val updated = tabs.recordEvent(tabId, event)
        if (updated != null) {
            publishBrowserUi(coordinator.snapshot())
            if (event is BrowserStateEvent.PageFinished) recordVisit(tabId, updated)
            publishTabStateToHost()
        }
    }

    fun onTabPageDialog(tabId: Long, pending: PendingJsDialog) {
        if (tabId == tabs.state.activeId) {
            hostBridge.onPageDialog(pending)
        } else {
            // A detached page must never put a modal over the foreground tab.
            pending.resolve(BrowserDialogAnswer.Cancel)
        }
    }

    fun onTabFavicon(tabId: Long, bitmap: Bitmap) {
        val accepted = faviconCache.offer(bitmap) as? BrowserFaviconOffer.Accepted ?: return
        tabs.setFavicon(tabId, accepted.favicon.id)
        publishBrowserUi(coordinator.snapshot())
        cockpitPublisher.publishFavicon(coordinator.snapshot(), accepted.favicon)
        publishTabStateToHost()
    }

    fun onFindChanged(tabId: Long, find: BrowserFindState) {
        if (tabId != tabs.state.activeId) return
        if (find.active) {
            viewSettings.startFind(find.query)
            viewSettings.updateFind(
                activeMatchOrdinal = (find.currentMatch - 1).coerceAtLeast(0),
                numberOfMatches = find.totalMatches,
                doneCounting = find.doneCounting,
            )
        } else {
            viewSettings.clearFind()
        }
        publishViewState()
    }

    fun zoomBrowserIn() = updateView(viewSettings.zoomIn())

    fun zoomBrowserOut() = updateView(viewSettings.zoomOut())

    fun resetBrowserZoom() = updateView(viewSettings.resetZoom())

    fun cycleBrowserUserAgent() {
        val next = when (viewSettings.state.userAgentMode) {
            BrowserUserAgentMode.TV -> BrowserUserAgentMode.DESKTOP
            BrowserUserAgentMode.DESKTOP -> BrowserUserAgentMode.MOBILE
            BrowserUserAgentMode.MOBILE -> BrowserUserAgentMode.TV
        }
        updateView(viewSettings.setUserAgent(next))
        attachedDriver?.reload()
        showNotice("${next.name.lowercase().replaceFirstChar(Char::uppercase)} site layout")
    }

    fun toggleBrowserDarkMode() =
        updateView(viewSettings.setDarkMode(!viewSettings.state.darkModeEnabled))

    fun setBrowserInputMode(mode: BrowserInteractionMode) =
        updateView(viewSettings.setInputMode(mode), applyToPage = false)

    fun cycleBrowserSearchEngine() {
        val presets = BrowserSearchEngine.PRESETS
        val index = presets.indexOf(viewSettings.state.searchEngine).takeIf { it >= 0 } ?: 0
        updateView(viewSettings.setSearchEngine(presets[(index + 1) % presets.size]), applyToPage = false)
    }

    fun startFind(query: String) {
        updateView(viewSettings.startFind(query), applyToPage = false)
        attachedDriver?.startFind(query)
    }

    fun nextFind() {
        attachedDriver?.findNext()
    }

    fun previousFind() {
        attachedDriver?.findPrevious()
    }

    fun clearFind() {
        attachedDriver?.clearFind()
        updateView(viewSettings.clearFind(), applyToPage = false)
    }

    fun toggleBookmark() {
        val page = coordinator.snapshot()
        val url = page.address?.canonicalUrl ?: return
        val title = page.title?.value.orEmpty()
        val faviconId = tabs.state.active?.faviconId ?: 0
        scope.launch(Dispatchers.IO) {
            val saved = profiles.snapshot().library.bookmarks.any { it.url == url }
            if (saved) {
                profiles.removeBookmark(url)
            } else {
                profiles.addBookmark(url, title, faviconId)
            }
            publishProfileState()
            publishLibraryStateToHost()
        }
    }

    fun removeBookmark(url: String) {
        scope.launch(Dispatchers.IO) {
            profiles.removeBookmark(url)
            publishProfileState()
            publishLibraryStateToHost()
        }
    }

    fun clearLibrary() {
        scope.launch(Dispatchers.IO) {
            profiles.clearTvLibrary()
            faviconCache.clear()
            publishProfileState()
            publishLibraryStateToHost()
        }
    }

    fun requestClearData() {
        coordinator.snapshot().epoch?.takeIf { it > 0 }?.let(hostBridge::requestClearData)
    }

    fun openTabFromTv(url: String? = null) {
        if (!allowBrowsingUnderVpnPolicy()) {
            vpn.maybeAutoConnect()
            return
        }
        scope.launch {
            val current = coordinator.snapshot()
            if (current.epoch == null || current.phase == BrowserPhase.IDLE) {
                showNotice("Open the browser before adding another tab.")
                return@launch
            }
            val transition = tabs.open(current, url)
            applyTabTransition(transition)
            if (transition.effects.any { it is TabEffect.Refused }) return@launch
            if (!url.isNullOrBlank()) navigateFromTv(url)
        }
    }

    fun selectTabFromTv(tabId: Long) {
        scope.launch {
            applyTabTransition(tabs.select(coordinator.snapshot(), tabId))
            ensureBrowserSurfaceVisible()
        }
    }

    fun closeTabFromTv(tabId: Long = tabs.state.activeId) {
        scope.launch {
            if (tabId == 0L) return@launch
            val transition = tabs.close(coordinator.snapshot(), tabId)
            applyTabTransition(transition)
            // Closing from the switcher should leave a useful browser, not a black surface. Back on
            // the sole tab still uses the explicit leave confirmation instead.
            if (tabs.state.tabs.isEmpty()) {
                applyTabTransition(tabs.open(coordinator.snapshot()))
            }
        }
    }

    /** Exits the actual custom view first; its callback updates [browserFullscreen]. */
    fun exitFullscreen(): Boolean {
        val exited = tabSurface?.exitFullscreen() == true
        if (!exited) setFullscreen(false)
        return exited
    }

    fun onPageDialog(pending: PendingJsDialog) = hostBridge.onPageDialog(pending)

    fun replyDialogLocal(accepted: Boolean, promptText: String? = null) =
        hostBridge.replyFromTv(accepted, promptText)

    /**
     * Handles a remote key while the browser owns the glass.
     *
     * Back used to reach the service's media key handler, which treats any non-idle surface as
     * something to tear down — so a single Back on the first page of a site ended the browser
     * session outright instead of walking history. Back belongs to the browser here, and the
     * smallest step back is a history entry.
     */
    fun handleTvKey(keyCode: Int): Boolean {
        if (uiState.value.surfaceMode != SurfaceMode.BROWSER) {
            return false
        }
        return when (keyCode) {
            KeyEvent.KEYCODE_BACK -> {
                // Activity.onKeyDown is a fallback reached only when the focused Compose/WebView
                // path did not consume Back. Resolve state visible to the service, then swallow the
                // key at the final rung: closing here would bypass chrome/overlay/tab/leave guards
                // that deliberately live beside the surface.
                when {
                    uiState.value.browserNotice != null -> dismissNotice()
                    uiState.value.browser.dialog != null -> replyDialogLocal(accepted = false)
                    uiState.value.browserFullscreen -> exitFullscreen()
                    coordinator.snapshot().canGoBack -> coordinator.goBack()
                    else -> Log.i(TAG, "Browser Back reached service fallback; session preserved")
                }
                true
            }
            KeyEvent.KEYCODE_MEDIA_STOP -> {
                closeFromTv()
                true
            }
            else -> false
        }
    }

    /**
     * Drops a TV-resident browser so cast media/mirror/idle can own the glass.
     *
     * The Windows shell usually stops the browser first; this is the receiver-side guarantee when a
     * cast command arrives while Compose is still on [SurfaceMode.BROWSER].
     */
    fun releaseForCastSwitch(reason: String) {
        val showingBrowser = uiState.value.surfaceMode == SurfaceMode.BROWSER
        val browserLive = coordinator.snapshot().phase != BrowserPhase.IDLE
        if (!showingBrowser && !browserLive) {
            return
        }
        Log.i(TAG, "Releasing browser surface for cast switch ($reason)")
        persistTvBrowsingSession()
        inputRouter.disable()
        hostBridge.onSessionEnded()
        coordinator.handleSessionEnded()
        clearTabs()
        workspaceSession?.closeWorkspace()
        vpn.endSession()
        setPreviewEnabled(false)
        uiState.update {
            it.copy(
                browser = BrowserSurfaceUi(),
                browserCursor = BrowserCursorUi(),
                browserNotice = null,
                browserFullscreen = false,
                browserWorkspaceVisible = false,
            )
        }
    }

    private fun handleCommand(command: BrowserCommandMessage) {
        if (command.action != BrowserCommandAction.CLOSE &&
            command.action != BrowserCommandAction.CLEAR_DATA && !allowBrowsingUnderVpnPolicy()
        ) {
            Log.w(TAG, "Secure browser ${command.action} blocked by VPN policy cmdId=${command.commandId}")
            return
        }
        if (uiState.value.browserWorkspaceVisible &&
            command.action != BrowserCommandAction.CLOSE &&
            command.action != BrowserCommandAction.CLEAR_DATA &&
            command.action != BrowserCommandAction.SET_PREVIEW_ENABLED
        ) {
            Log.i(TAG, "Secure browser ${command.action} ignored while workspace visible cmdId=${command.commandId}")
            return
        }
        Log.i(
            TAG,
            "Secure browser command ${command.action} epoch=${command.epoch} cmdId=${command.commandId}",
        )
        when (command.action) {
            BrowserCommandAction.OPEN -> {
                when (
                    val effect =
                        coordinator.handleOpen(command.epoch, command.commandId, command.url.orEmpty())
                ) {
                    is BrowserCommandEffect.Open -> {
                        applyTabTransition(tabs.start(coordinator.snapshot()))
                        inputRouter.enableForBrowserEpoch(command.epoch)
                        enterSurface()
                        // Republish cockpit (including network) under the new epoch. Auth-time
                        // publish often runs while epoch is still 0, so VPN never unlocked on Windows.
                        publishCockpitState()
                        publishWorkspaceStateToHost()
                        Log.i(TAG, "Secure browser OPEN accepted cmdId=${command.commandId}")
                    }
                    // Do not flip to a blank WebView when OPEN was refused (stale epoch / surface
                    // busy). That left the TV on a white page with nothing loading.
                    is BrowserCommandEffect.Rejected -> {
                        Log.w(TAG, "Secure browser OPEN rejected: ${effect.reason}")
                        showNotice(
                            when (effect.reason) {
                                BrowserCommandRejection.SURFACE_BUSY ->
                                    "The TV is busy with another surface, so Windows could not open that page."
                                BrowserCommandRejection.STALE_EPOCH ->
                                    "That open was refused as stale. Try again from Windows."
                                else ->
                                    "Windows could not open that page (${effect.reason})."
                            },
                        )
                    }
                    else -> Log.w(TAG, "Secure browser OPEN produced unexpected effect: $effect")
                }
            }
            BrowserCommandAction.NAVIGATE -> {
                when (
                    val effect =
                        coordinator.handleNavigate(command.epoch, command.commandId, command.url.orEmpty())
                ) {
                    is BrowserCommandEffect.Navigate ->
                        Log.i(TAG, "Secure browser NAVIGATE accepted cmdId=${command.commandId}")
                    is BrowserCommandEffect.Rejected -> {
                        Log.w(TAG, "Secure browser NAVIGATE rejected: ${effect.reason}")
                        showNotice(
                            when (effect.reason) {
                                BrowserCommandRejection.STALE_COMMAND,
                                BrowserCommandRejection.STALE_EPOCH,
                                ->
                                    "That navigation was refused as stale. Try Go again from Windows."
                                BrowserCommandRejection.NO_ACTIVE_BROWSER,
                                BrowserCommandRejection.BROWSER_REQUIRES_OPEN,
                                ->
                                    "Open a page on the TV before navigating from Windows."
                                else ->
                                    "Windows could not navigate (${effect.reason})."
                            },
                        )
                    }
                    else -> Log.w(TAG, "Secure browser NAVIGATE produced unexpected effect: $effect")
                }
            }
            BrowserCommandAction.BACK -> coordinator.goBack()
            BrowserCommandAction.FORWARD -> coordinator.goForward()
            BrowserCommandAction.RELOAD -> coordinator.reload()
            BrowserCommandAction.STOP -> coordinator.stopLoading()
            BrowserCommandAction.CLOSE -> {
                coordinator.handleClose(command.epoch, command.commandId)
                inputRouter.disable()
                clearTabs()
                leaveSurface()
                Log.i(TAG, "Secure browser CLOSE applied cmdId=${command.commandId}")
            }
            BrowserCommandAction.SET_PREVIEW_ENABLED ->
                setPreviewEnabled(command.previewEnabled == true)
            BrowserCommandAction.CLEAR_DATA -> hostBridge.requestClearData(command.epoch)
        }
    }

    private fun handleLibraryCommand(command: BrowserLibraryCommandMessage) {
        if (!remoteCommands.accept(command.epoch, command.commandId, "library")) return
        val selected = profiles.snapshot().profiles
        if (selected.activeSource != BrowserProfileSource.TV) {
            Log.w(TAG, "Ignoring a TV-library command while the connected device owns the library")
            return
        }
        scope.launch(Dispatchers.IO) {
            when (command.action) {
                BrowserLibraryAction.ADD_BOOKMARK ->
                    profiles.addBookmark(command.url, command.title)
                BrowserLibraryAction.REMOVE_BOOKMARK ->
                    profiles.removeBookmark(command.url)
                BrowserLibraryAction.CLEAR_HISTORY -> profiles.clearHistory()
                BrowserLibraryAction.CLEAR_BOOKMARKS -> profiles.clearBookmarks()
                BrowserLibraryAction.REQUEST_SNAPSHOT -> Unit
            }
            publishProfileState()
            publishLibraryStateToHost()
        }
    }

    private fun applyHostTabTransition(transition: TabTransition) {
        applyTabTransition(transition)
        // Host tab Select/New must own the glass. Without this the strip updates on Windows while
        // Compose stays on the idle "PC CONNECTED" card (2026-09-08).
        if (tabs.state.tabs.isNotEmpty()) {
            ensureBrowserSurfaceVisible()
        }
    }

    private fun ensureBrowserSurfaceVisible() {
        enterSurface()
        coordinator.snapshot().epoch?.let(inputRouter::enableForBrowserEpoch)
    }

    private fun applyTabTransition(transition: TabTransition) {
        publishBrowserUi(tabs.activePage() ?: coordinator.snapshot())
        publishTabStateToHost()
        if (transition.effects.isEmpty()) return
        persistTvBrowsingSession()
        scope.launch {
            val surface = tabSurface ?: return@launch
            transition.effects.forEach { effect ->
                when (effect) {
                    is TabEffect.Freeze -> {
                        if (effect.id == attachedTabId) detachAttachedDriver()
                        surface.apply(effect)
                    }
                    is TabEffect.Destroy -> {
                        if (effect.id == attachedTabId) detachAttachedDriver()
                        surface.apply(effect)
                    }
                    is TabEffect.Show -> {
                        surface.apply(effect)
                        activateDriver(effect.id)
                    }
                    TabEffect.Refused -> showNotice("Eight tabs are already open. Close one to add another.")
                    else -> surface.apply(effect)
                }
            }
        }
    }

    private fun activateDriver(tabId: Long) {
        val surface = tabSurface ?: return
        val driver = surface.driverFor(tabId) ?: return
        val page = tabs.pageFor(tabId) ?: return

        detachAttachedDriver()
        if (!coordinator.activatePage(page)) {
            showNotice("That tab belongs to an expired browser session.")
            return
        }

        attachedTabId = tabId
        attachedDriver = driver
        hostBridge.attachDriver(driver)
        driver.applyViewSettings(viewSettings.state)
        if (page.address != null) {
            // Tab already has a destination (New with URL / restored session strip). Do not replay
            // a pending OPEN from another tab into this WebView — that collapsed distinct Google
            // searches into one query when Windows restored siblings after Mirror (2026-09-08).
            coordinator.discardPendingNavigation()
            coordinator.attachPort(driver)
            driver.resume(page)
        } else {
            val replayedOpen = coordinator.attachPort(driver)
            if (!replayedOpen) driver.resume(page)
        }
        if (viewSettings.state.find.active) driver.startFind(viewSettings.state.find.query)
        publishBrowserUi(page)
    }

    private fun detachAttachedDriver() {
        val driver = attachedDriver ?: return
        coordinator.detachPort(driver)
        hostBridge.detachDriver(driver)
        attachedDriver = null
        attachedTabId = 0
    }

    private fun clearTabs() {
        val openIds = tabs.state.tabs.map { it.id }
        tabs.clear()
        scope.launch {
            detachAttachedDriver()
            val surface = tabSurface
            if (surface != null) {
                openIds.forEach { surface.apply(TabEffect.Destroy(it)) }
            }
        }
    }

    private fun updateView(
        next: com.rextechnologies.flint.receiver.browser.BrowserViewState,
        applyToPage: Boolean = true,
    ) {
        if (applyToPage) attachedDriver?.applyViewSettings(next)
        publishViewState()
    }

    private fun publishBrowserUi(page: BrowserState) = publishers.browserUi(page)

    private fun publishViewState() = publishers.viewState()

    private fun publishTabStateToHost() = publishers.tabStateToHost()

    private fun publishViewStateToHost() = publishers.viewStateToHost()

    private fun publishProfileState() = publishers.profileState()

    private fun publishLibraryStateToHost() = publishers.libraryStateToHost()

    /** Publishes the active workspace only for protocol v3 hosts that negotiated workspace support. */
    fun publishWorkspaceStateToHost() = publishers.workspaceStateToHost()

    private fun publishCockpitState() = publishers.cockpitState()

    private fun activateViewSite(page: BrowserState) {
        val site = runCatching { page.address?.canonicalUrl?.let { URI(it).host } }.getOrNull()
        if (site == activeViewSite) return
        activeViewSite = site
        updateView(viewSettings.activateSite(site))
    }

    private fun recordVisit(tabId: Long, page: BrowserState) {
        val address = page.address ?: return
        val title = page.title?.value.orEmpty()
        val faviconId = tabs.state.tabs.firstOrNull { it.id == tabId }?.faviconId ?: 0
        scope.launch(Dispatchers.IO) {
            profiles.recordVisit(address.canonicalUrl, title, faviconId)
            publishProfileState()
            publishLibraryStateToHost()
        }
    }

    /**
     * Points the opt-in JPEG capture loop at the mosaic root (every live pane) so Windows shows the
     * same layout as the sofa — falling back to the focused WebView only when the host is absent.
     */
    private fun attachWorkspacePreviewDriver(session: BrowserWorkspaceSession) {
        if (!uiState.value.browserWorkspaceVisible) return
        val driver = session.focusedDriver()
        if (driver != null && attachedDriver !== driver) {
            attachedDriver?.let { previous ->
                if (attachedTabId != 0L) {
                    coordinator.detachPort(previous)
                    attachedTabId = 0
                }
                hostBridge.detachDriver(previous)
            }
            attachedDriver = driver
            // Dialogs / clear-data still need a driver; preview target is chosen separately.
            hostBridge.attachDriver(driver)
        }
        val mosaic = session.previewCaptureView()
        when {
            mosaic != null -> hostBridge.attachPreviewTarget(mosaic)
            driver != null -> hostBridge.attachPreviewTarget(driver.webView())
            else -> return
        }
        if (previewEnabled) {
            hostBridge.setPreviewEnabled(true)
        }
    }

    /** TV-owned libraries travel out; a device-owned library remains authoritative on the host. */
    private fun resetForProfileSwitch() {
        vpn.invalidateConsentRequest()
        val reopenWorkspace = uiState.value.browserWorkspaceVisible
        workspaceSession?.saveLocalState()
        workspaceSession?.closeWorkspace()
        if (reopenWorkspace) {
            uiState.update { it.copy(browserWorkspaceVisible = false) }
        }
        vpn.endSession()
        val page = coordinator.snapshot()
        val epoch = page.epoch ?: return
        if (page.phase == BrowserPhase.IDLE) return
        tvCommandId = maxOf(tvCommandId, page.lastAcceptedCommandId) + 1
        if (coordinator.handleResetBlank(epoch, tvCommandId) !is BrowserCommandEffect.ResetBlank) return
        val saved = profiles.browsingSession().takeIf {
            profiles.snapshot().profiles.activeSource == BrowserProfileSource.TV
        }
        restoringTvSession = true
        try {
            clearTabs()
            applyTabTransition(tabs.start(coordinator.snapshot()))
            restoreTvTabs(saved)
        } finally {
            restoringTvSession = false
        }
        persistTvBrowsingSession()
        inputRouter.enableForBrowserEpoch(epoch)
        enterSurface()
        if (saved != null) {
            if (saved.workspaceMode) openWorkspaceFromTv(null)
        } else if (reopenWorkspace) {
            openWorkspaceFromTv(null)
        }
    }

    private fun tvSessionToRestore(): BrowserTvBrowsingSession? =
        profiles.browsingSession().takeIf {
            profiles.snapshot().profiles.activeSource == BrowserProfileSource.TV &&
                it.tabs.any { tab -> tab.url.isNotBlank() }
        }

    /**
     * Reloads the active TV profile's tab strip after a cold start. Returns whether workspace
     * mode was saved and should be re-entered — only when the start itself was a blank Browse.
     */
    private fun restoreTvSessionAfterStart(keepCurrentPage: Boolean): Boolean {
        val saved = tvSessionToRestore()
        restoringTvSession = true
        try {
            applyTabTransition(tabs.start(coordinator.snapshot()))
            restoreTvTabs(saved)
        } finally {
            restoringTvSession = false
        }
        persistTvBrowsingSession(if (keepCurrentPage) saved?.workspaceMode else null)
        return !keepCurrentPage && saved?.workspaceMode == true
    }

    @Volatile
    private var restoringTvSession = false

    private fun persistTvBrowsingSession(workspaceMode: Boolean? = null) {
        if (restoringTvSession) return
        if (profiles.snapshot().profiles.activeSource != BrowserProfileSource.TV) return
        val open = tabs.state
        val savedTabs = open.tabs.map { BrowserSavedTab(it.url, it.title) }.filter { it.url.isNotBlank() }
        val active = open.tabs.indexOfFirst { it.id == open.activeId }.coerceAtLeast(0)
        profiles.saveBrowsingSession(
            BrowserTvBrowsingSession(
                savedTabs,
                active,
                workspaceMode ?: uiState.value.browserWorkspaceVisible,
            ),
        )
    }

    private fun restoreTvTabs(session: BrowserTvBrowsingSession?) {
        val current = coordinator.snapshot()
        val plan = TvSessionRestore.plan(session, current.address?.canonicalUrl)
        if (plan.isEmpty) return
        val epoch = current.epoch ?: return
        if (plan.navigateCurrentTo != null) {
            tvCommandId = maxOf(tvCommandId, coordinator.snapshot().lastAcceptedCommandId) + 1
            coordinator.handleNavigate(epoch, tvCommandId, plan.navigateCurrentTo)
        }
        for (url in plan.extraUrls) {
            applyTabTransition(tabs.open(coordinator.snapshot(), url))
        }
        val activeId = tabs.state.tabs.getOrNull(plan.activeIndex)?.id ?: return
        if (activeId != tabs.state.activeId) {
            applyTabTransition(tabs.select(coordinator.snapshot(), activeId))
        }
    }

    private fun mintTvEpoch(): Long {
        val minted = maxOf(lastTvEpoch + 1, System.currentTimeMillis())
        lastTvEpoch = minted
        return minted
    }

    private fun setPreviewEnabled(enabled: Boolean) {
        previewEnabled = enabled
        hostBridge.setPreviewEnabled(enabled)
    }

    private fun enterSurface() {
        scope.launch {
            pausePlayer()
            vpn.maybeAutoConnect()
            uiState.update {
                it.copy(
                    surfaceMode = SurfaceMode.BROWSER,
                    detail = "Secure browser",
                    error = null,
                    // Leaving mirror/player chrome behind; frames may still arrive until the host
                    // stops them, but Compose will not show that surface.
                    mirrorFrameReceived = false,
                )
            }
        }
    }

    private fun leaveSurface() {
        scope.launch {
            pendingWorkspaceOpen = false
            vpn.invalidateConsentRequest()
            workspaceSession?.closeWorkspace()
            setPreviewEnabled(false)
            vpn.endSession()
            uiState.update {
                it.copy(
                    surfaceMode = SurfaceMode.IDLE,
                    title = "",
                    browser = BrowserSurfaceUi(),
                    browserCursor = BrowserCursorUi(),
                    browserNotice = null,
                    browserFullscreen = false,
                    browserWorkspaceVisible = false,
                    detail = if (it.connected) IDLE_CONNECTED_DETAIL else IDLE_DETAIL,
                    error = null,
                )
            }
        }
    }

    /**
     * ADR-0022: auto-connect only when the active TV profile has valid network settings and the
     * capability probe allows it. Soft-fails into [vpnState] for a banner.
     */
    private fun publishUnavailable() {
        previewPublisher = null
        previewEnabled = false
        hostBridge.bindPreviewPublisher(null)
        uiState.update { it.copy(browserPort = 0, browserFingerprint = null) }
    }

    private fun publishStateToHost(page: BrowserState) {
        // Idle/reset snapshots use epoch/revision 0 and are not legal on the wire.
        if (page.epoch == null || page.epoch <= 0L || page.revision <= 0L) {
            return
        }

        val viewport = coordinator.viewport()
        val message = page.toWireMessage(
            viewportWidth = viewport?.first ?: 0,
            viewportHeight = viewport?.second ?: 0,
            previewState = previewState(),
        )
        try {
            if (server?.send(BrowserOutboundMessage.State(message)) != true) {
                Log.i(TAG, "Secure browser state not delivered (no active host session)")
            }
        } catch (exception: Exception) {
            Log.w(TAG, "Secure browser state rejected by wire rules", exception)
        }
    }

    private fun previewState(): BrowserPreviewState = when {
        previewPublisher == null -> BrowserPreviewState.UNAVAILABLE
        previewEnabled -> BrowserPreviewState.ENABLED
        else -> BrowserPreviewState.DISABLED
    }

    /**
     * Routes Windows mosaic preview pointer/scroll onto the pane under the cursor and selects that
     * pane so it becomes the active focused page.
     */
    private fun handleWorkspacePreviewInput(input: BrowserInputMessage) {
        val session = workspaceSession ?: return
        val mosaic = session.previewCaptureView() ?: return
        val mosaicWidth = mosaic.width
        val mosaicHeight = mosaic.height
        if (mosaicWidth <= 0 || mosaicHeight <= 0) {
            Log.i(TAG, "Workspace preview input ignored — mosaic size unknown")
            return
        }
        val page = coordinator.snapshot()
        val previewFrameId = previewPublisher?.lastPublishedFrameId ?: 0L
        val mapping = inputRouter.handle(
            input,
            currentNavigationId = page.navigationId,
            currentFrameId = if (previewFrameId > 0L) previewFrameId else page.navigationId,
            viewportWidth = mosaicWidth,
            viewportHeight = mosaicHeight,
        )
        if (mapping !is BrowserInputMapping.Accepted) {
            Log.i(TAG, "Workspace preview input rejected sequence=${input.sequence}")
            return
        }
        when (val native = mapping.input) {
            is BrowserNativeInput.Pointer -> {
                val delivered = session.dispatchPreviewPointer(native.x, native.y, native)
                if (native.action != BrowserPointerAction.MOVE) {
                    Log.i(
                        TAG,
                        "Workspace preview pointer seq=${input.sequence} action=${native.action} " +
                            "x=${native.x} y=${native.y} delivered=$delivered focused=${session.state.focusedPaneId}",
                    )
                }
            }
            is BrowserNativeInput.Scroll -> {
                val delivered = session.dispatchPreviewScroll(native.x, native.y, native)
                Log.i(
                    TAG,
                    "Workspace preview scroll seq=${input.sequence} dx=${native.deltaX} dy=${native.deltaY} delivered=$delivered",
                )
            }
            else -> Unit
        }
    }

    internal companion object {
        const val TAG = "FlintBrowser"

        /** Workspace messages exist only from protocol v3; older hosts must never be sent one. */
        const val WORKSPACE_PROTOCOL_MINIMUM = 3
        const val IDLE_CONNECTED_DETAIL = "Connected — choose something to cast"
        const val IDLE_DETAIL = "Ready on the hotspot LAN"
    }
}
