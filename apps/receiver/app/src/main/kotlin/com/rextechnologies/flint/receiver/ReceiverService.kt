package com.rextechnologies.flint.receiver

import android.app.Service
import android.content.Intent
import android.media.AudioManager
import android.os.Binder
import android.os.Build
import android.os.IBinder
import android.util.Log
import android.view.KeyEvent
import android.view.Surface
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import androidx.media3.common.MimeTypes
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.datasource.DefaultDataSource
import androidx.media3.datasource.DefaultHttpDataSource
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import com.rextechnologies.flint.protocol.discovery.PairingCode
import com.rextechnologies.flint.protocol.discovery.ReceiverProbe
import com.rextechnologies.flint.protocol.wire.AudioConfigMessage
import com.rextechnologies.flint.protocol.wire.AudioPacket
import com.rextechnologies.flint.protocol.wire.BrowserCommandAction
import com.rextechnologies.flint.protocol.wire.BrowserCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserDialogReplyMessage
import com.rextechnologies.flint.protocol.wire.BrowserInputMessage
import com.rextechnologies.flint.protocol.wire.BrowserPointerAction
import com.rextechnologies.flint.protocol.wire.BrowserPreviewState
import com.rextechnologies.flint.protocol.wire.ControlMessage
import com.rextechnologies.flint.protocol.wire.KeyAction
import com.rextechnologies.flint.protocol.wire.KeyControl
import com.rextechnologies.flint.protocol.wire.MediaAction
import com.rextechnologies.flint.protocol.wire.MediaCommandMessage
import com.rextechnologies.flint.protocol.wire.MediaDataMessage
import com.rextechnologies.flint.protocol.wire.PlaybackState
import com.rextechnologies.flint.protocol.wire.PlaybackStateMessage
import com.rextechnologies.flint.protocol.wire.StatsMessage
import com.rextechnologies.flint.protocol.wire.SurfaceMessage
import com.rextechnologies.flint.protocol.wire.SurfaceMode
import com.rextechnologies.flint.protocol.wire.TransportAction
import com.rextechnologies.flint.protocol.wire.TransportControl
import com.rextechnologies.flint.protocol.wire.VideoConfigMessage
import com.rextechnologies.flint.protocol.wire.VideoPacket
import com.rextechnologies.flint.protocol.wire.VolumeControl
import com.rextechnologies.flint.receiver.browser.AndroidBrowserVpnConnectionVerifier
import com.rextechnologies.flint.receiver.browser.AndroidKeystoreBrowserNetworkCrypto
import com.rextechnologies.flint.receiver.browser.AndroidVpnCapabilityProbe
import com.rextechnologies.flint.receiver.browser.BrowserCommandEffect
import com.rextechnologies.flint.receiver.browser.BrowserCoordinator
import com.rextechnologies.flint.receiver.browser.BrowserDialogAnswer
import com.rextechnologies.flint.receiver.browser.BrowserHostBridge
import com.rextechnologies.flint.receiver.browser.BrowserInputMapping
import com.rextechnologies.flint.receiver.browser.BrowserInputRouter
import com.rextechnologies.flint.receiver.browser.BrowserLibraryNetworkProfileAuthority
import com.rextechnologies.flint.receiver.browser.BrowserLibraryStore
import com.rextechnologies.flint.receiver.browser.BrowserNativeInput
import com.rextechnologies.flint.receiver.browser.BrowserNetworkStore
import com.rextechnologies.flint.receiver.browser.BrowserPreviewPublisher
import com.rextechnologies.flint.receiver.browser.BrowserRefusal
import com.rextechnologies.flint.receiver.browser.BrowserState
import com.rextechnologies.flint.receiver.browser.BrowserStateEvent
import com.rextechnologies.flint.receiver.browser.BrowserTabSurfacePort
import com.rextechnologies.flint.receiver.browser.BrowserVpnTunnelImpl
import com.rextechnologies.flint.receiver.browser.BrowserWebViewDriver
import com.rextechnologies.flint.receiver.browser.BrowserWorkspaceStore
import com.rextechnologies.flint.receiver.browser.PendingJsDialog
import com.rextechnologies.flint.receiver.browser.identity.AndroidKeystoreReceiverIdentityProvider
import com.rextechnologies.flint.receiver.browser.identity.PersistentReceiverIdentityProvider
import com.rextechnologies.flint.receiver.browser.net.BrowserOutboundMessage
import com.rextechnologies.flint.receiver.browser.net.BrowserSecureSessionListener
import com.rextechnologies.flint.receiver.browser.net.BrowserTlsServer
import com.rextechnologies.flint.receiver.browser.toWireMessage
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceSession
import com.rextechnologies.flint.receiver.media.MirrorAudioDecoder
import com.rextechnologies.flint.receiver.media.MirrorVideoDecoder
import com.rextechnologies.flint.receiver.media.PushedMediaSink
import com.rextechnologies.flint.receiver.net.ReceiverBroadcastResponder
import com.rextechnologies.flint.receiver.net.ReceiverMdnsResponder
import com.rextechnologies.flint.receiver.net.ReceiverServer
import com.rextechnologies.flint.receiver.net.ReceiverSessionListener
import com.rextechnologies.flint.receiver.net.ReceiverState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.net.Inet4Address

/**
 * Owns the listening socket and decoders independently of the TV activity.
 * The activity may be recreated or hidden without dropping a cast session.
 */
@androidx.annotation.OptIn(markerClass = [UnstableApi::class])
class ReceiverService : Service(), ReceiverSessionListener, Player.Listener {
    inner class LocalBinder : Binder() {
        val service: ReceiverService get() = this@ReceiverService
    }

    private val binder = LocalBinder()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val _uiState = MutableStateFlow(ReceiverUiState())
    val uiState: StateFlow<ReceiverUiState> = _uiState.asStateFlow()

    private lateinit var player: ExoPlayer
    private lateinit var videoDecoder: MirrorVideoDecoder
    private lateinit var audioDecoder: MirrorAudioDecoder
    private var pairingCode = PairingCode.generate()
    private var server: ReceiverServer? = null
    private var mdns: ReceiverMdnsResponder? = null
    private var broadcastResponder: ReceiverBroadcastResponder? = null
    private var serverStateJob: Job? = null
    private var currentAddress: Inet4Address? = null
    private val notifications = ReceiverNotifications(this)

    // Both stores must share this one authoritative catalog: VPN credentials are valid only for
    // durable TV profiles, never for a paired device's memory-only projection.
    private val browserLibraryStore by lazy {
        BrowserLibraryStore(File(filesDir, "browser-library.json"))
    }

    private val browserNetworkStore by lazy {
        BrowserNetworkStore(
            file = File(filesDir, "browser-network.json"),
            crypto = AndroidKeystoreBrowserNetworkCrypto(),
            profileAuthority = BrowserLibraryNetworkProfileAuthority(browserLibraryStore),
        )
    }

    private val browserWorkspaceStore by lazy {
        BrowserWorkspaceStore(
            file = File(filesDir, "browser-workspace.json"),
            profileAuthority = BrowserLibraryNetworkProfileAuthority(browserLibraryStore),
        )
    }

    /**
     * Everything browser-shaped, including its own TLS listener and identity.
     *
     * The service keeps the surface decision and the cast session; the controller owns the browser
     * session, so neither lifecycle has to be read through the other.
     */
    private val workspaceSession: BrowserWorkspaceSession by lazy {
        BrowserWorkspaceSession(
            notice = { message -> showBrowserNotice(message) },
            workspaceStore = browserWorkspaceStore,
            onStateChanged = { publishWorkspaceStateToHost() },
        )
    }

    internal val browserController: ReceiverBrowserController by lazy {
        ReceiverBrowserController(
            scope = scope,
            uiState = _uiState,
            identityProvider = AndroidKeystoreReceiverIdentityProvider(
                fallback = PersistentReceiverIdentityProvider(File(filesDir, "browser-identity")),
            ),
            libraryStore = browserLibraryStore,
            pausePlayer = { player.pause() },
            networkStore = browserNetworkStore,
            workspaceStore = browserWorkspaceStore,
            workspaceSession = workspaceSession,
            vpnProbe = AndroidVpnCapabilityProbe(this@ReceiverService),
            vpnNeedsConsent = { AndroidVpnCapabilityProbe(this@ReceiverService).needsConsent() },
            vpnTunnel = BrowserVpnTunnelImpl(this@ReceiverService),
            vpnVerifier = AndroidBrowserVpnConnectionVerifier(this@ReceiverService),
        )
    }

    /** Activity attaches/detaches the WebView driver through this coordinator. */
    fun browserCoordinator(): BrowserCoordinator = browserController.coordinator

    // Some Fire OS builds silently drop an outbound connection this app initiates to a private LAN
    // address, even though the exact same address works perfectly for the connection the host
    // opened to us. When a MediaCommandMessage arrives with no URL, it means the host pushed the
    // file's bytes over that already-working connection instead — reassembled by the sink, which
    // also owns the rules: one transfer at a time, a bound on free space, no partial file left
    // behind a failure.
    private val pushedMedia by lazy { PushedMediaSink(cacheDir) }

    override fun onCreate() {
        super.onCreate()
        notifications.ensureChannel()
        startForeground(
            ReceiverNotifications.NOTIFICATION_ID,
            notifications.ongoing("Starting on the hotspot LAN"),
        )

        // Media3's default HttpDataSource times out a connect attempt after 8 seconds. That is
        // tuned for the open internet, not for a phone-hotspot LAN under load: this project has
        // repeatedly seen DHCP re-leasing and ADB going briefly unresponsive on exactly this kind
        // of network, and 8 seconds is not always enough for a fresh TCP connection to land in
        // that environment. The failure this produces is misleading — Media3 reports
        // ERROR_CODE_IO_NETWORK_CONNECTION_TIMEOUT, indistinguishable from a genuinely blocked
        // port — so a slow-but-working network and a firewalled one look identical to the user.
        // A longer, explicit timeout removes that false positive without hiding a real block: a
        // truly closed port still fails, just after a fairer wait.
        val httpDataSourceFactory = DefaultHttpDataSource.Factory()
            .setConnectTimeoutMs(LOCAL_MEDIA_TIMEOUT_MILLIS)
            .setReadTimeoutMs(LOCAL_MEDIA_TIMEOUT_MILLIS)
            .setAllowCrossProtocolRedirects(true)
        // A pushed file plays from a file:// URI (see MediaDataMessage), not http://, so the
        // player needs a data source that dispatches by scheme rather than one hardwired to HTTP.
        // DefaultHttpDataSource.openConnection() unconditionally casts to HttpURLConnection, which
        // throws ClassCastException the instant it opens a file:// URL — DefaultDataSource.Factory
        // is the wrapper that routes file:// to FileDataSource and only hands http(s):// to
        // httpDataSourceFactory, so both loading paths work through the one player.
        val dataSourceFactory = DefaultDataSource.Factory(this, httpDataSourceFactory)
        val mediaSourceFactory = DefaultMediaSourceFactory(this).setDataSourceFactory(dataSourceFactory)

        player = ExoPlayer.Builder(this)
            .setMediaSourceFactory(mediaSourceFactory)
            .build()
            .apply {
                addListener(this@ReceiverService)
                setAudioAttributes(androidx.media3.common.AudioAttributes.DEFAULT, true)
                setWakeMode(C.WAKE_MODE_LOCAL)
            }
        videoDecoder = MirrorVideoDecoder(::reportMirrorFailure, ::onMirrorFrameRendered)
        audioDecoder = MirrorAudioDecoder(::reportMirrorFailure)
        _uiState.update { it.copy(pairingCode = pairingCode.toString()) }

        scope.launch {
            while (isActive) {
                // Rebind only when the address actually went away. A TV with both Wi-Fi and
                // Ethernet up, or one whose interface list reorders between samples, would
                // otherwise tear down a cast mid-frame for an address that was never lost.
                val addresses = ReceiverServer.localAddresses()
                val bound = currentAddress
                if (bound == null || bound !in addresses) {
                    val next = addresses.firstOrNull()
                    if (next != bound) restartServer(next)
                }
                delay(NETWORK_POLL_MILLIS)
            }
        }
        scope.launch {
            while (isActive) {
                sendPeriodicState()
                delay(REPORT_INTERVAL_MILLIS)
            }
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_NEW_CODE -> refreshPairingCode()
            ACTION_STOP -> stopSelf()
        }
        return START_STICKY
    }

    override fun onBind(intent: Intent?): IBinder = binder

    fun refreshPairingCode() {
        pairingCode = PairingCode.generate()
        _uiState.update { it.copy(pairingCode = pairingCode.toString()) }
        updateNotification()
    }

    /** Clears a recoverable listener failure and asks the network poller to bind again. */
    fun retryConnection() {
        _uiState.update {
            it.copy(
                networkState = ReceiverNetworkState.STARTING,
                peerName = null,
                error = null,
                detail = "Retrying the receiver",
            )
        }
        restartServer(ReceiverServer.findLocalAddress())
    }

    fun player(): Player = player

    fun attachMirrorSurface(surface: Surface?) = videoDecoder.attachSurface(surface)

    fun dispatchTvKey(keyCode: Int): Boolean = browserController.handleTvKey(keyCode) ||
        dispatchCastTvKey(keyCode)

    /**
     * Media-surface remote handling.
     *
     * Deliberately reached only after the browser has declined the key. Back here means "tear the
     * surface down", which is right for a finished video and catastrophic for a browser sitting on
     * the second page of a site.
     */
    private fun dispatchCastTvKey(keyCode: Int): Boolean = when (keyCode) {
        KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE, KeyEvent.KEYCODE_DPAD_CENTER -> {
            if (_uiState.value.surfaceMode == SurfaceMode.PLAYER) {
                if (player.isPlaying) player.pause() else resumeOrRestartPlayback()
                true
            } else {
                false
            }
        }
        KeyEvent.KEYCODE_MEDIA_PLAY -> {
            resumeOrRestartPlayback()
            true
        }
        KeyEvent.KEYCODE_MEDIA_PAUSE -> {
            player.pause()
            true
        }
        KeyEvent.KEYCODE_MEDIA_STOP, KeyEvent.KEYCODE_BACK -> {
            if (_uiState.value.surfaceMode != SurfaceMode.IDLE) {
                clearSessionSurface()
                true
            } else {
                false
            }
        }
        KeyEvent.KEYCODE_MEDIA_FAST_FORWARD -> {
            seekBy(SEEK_STEP_MILLIS)
            true
        }
        KeyEvent.KEYCODE_MEDIA_REWIND -> {
            seekBy(-SEEK_STEP_MILLIS)
            true
        }
        // The basic Fire TV remote has no dedicated fast-forward/rewind keys, so scrubbing during
        // playback is the D-pad's job — the same convention YouTube and Netflix's TV apps use.
        // Gated to the player surface: on every other screen, left/right is ordinary focus
        // navigation between on-screen controls and must not be swallowed here.
        KeyEvent.KEYCODE_DPAD_RIGHT -> {
            if (_uiState.value.surfaceMode == SurfaceMode.PLAYER) {
                seekBy(SEEK_STEP_MILLIS)
                true
            } else {
                false
            }
        }
        KeyEvent.KEYCODE_DPAD_LEFT -> {
            if (_uiState.value.surfaceMode == SurfaceMode.PLAYER) {
                seekBy(-SEEK_STEP_MILLIS)
                true
            } else {
                false
            }
        }
        else -> false
    }

    private fun seekBy(deltaMs: Long) {
        player.seekTo(seekTargetMs(player.currentPosition, deltaMs, player.duration.safeDuration()))
    }

    /**
     * Resumes playback, restarting from the beginning if the item already finished.
     *
     * `Player.play()` alone is a no-op once `playbackState` is `STATE_ENDED` — there is nothing
     * left to render at the current position, so setting `playWhenReady = true` changes nothing.
     * The remote's play/pause button pressed after a clip finishes looked like it did nothing at
     * all for exactly this reason; seeking back to the start first is what actually resumes it.
     */
    private fun resumeOrRestartPlayback() {
        if (needsSeekToStartBeforeResuming(player.playbackState)) {
            player.seekTo(0)
        }
        player.play()
    }

    private fun restartServer(address: Inet4Address?) {
        serverStateJob?.cancel()
        serverStateJob = null
        server?.close()
        server = null
        browserController.close()
        mdns?.close()
        mdns = null
        broadcastResponder?.close()
        broadcastResponder = null
        currentAddress = address
        if (address == null) {
            player.stop()
            player.clearMediaItems()
            _uiState.update {
                it.copy(
                    networkState = ReceiverNetworkState.WAITING,
                    address = null,
                    browserPort = 0,
                    browserFingerprint = null,
                    peerName = null,
                    surfaceMode = SurfaceMode.IDLE,
                    title = "",
                    playbackState = PlaybackState.IDLE,
                    positionMs = 0,
                    durationMs = -1,
                    mirrorWidth = 0,
                    mirrorHeight = 0,
                    mirrorFrameReceived = false,
                    detail = "Connect this TV and your phone or PC to the same network",
                    error = null,
                )
            }
            updateNotification()
            return
        }

        _uiState.update {
            it.copy(
                networkState = ReceiverNetworkState.STARTING,
                address = address.hostAddress,
                peerName = null,
                surfaceMode = SurfaceMode.IDLE,
                title = "",
                playbackState = PlaybackState.IDLE,
                positionMs = 0,
                durationMs = -1,
                mirrorWidth = 0,
                mirrorHeight = 0,
                mirrorFrameReceived = false,
                detail = "Opening the receiver on this network",
                error = null,
            )
        }

        val candidate = ReceiverServer(
            address = address,
            listener = this,
            pairingCodeProvider = { pairingCode },
            displayMetrics = {
                val metrics = resources.displayMetrics
                Triple(metrics.widthPixels, metrics.heightPixels, metrics.densityDpi)
            },
            browserPortProvider = { browserController.port.takeIf { port -> port > 0 } ?: _uiState.value.browserPort },
        )
        val started = candidate.start().onFailure { failure ->
            Log.e(TAG, "Receiver listener could not start", failure)
            _uiState.update {
                it.copy(
                    networkState = ReceiverNetworkState.WAITING,
                    address = address.hostAddress,
                    peerName = null,
                    error = RECEIVER_START_FAILURE_MESSAGE,
                    detail = RECEIVER_START_FAILURE_MESSAGE,
                )
            }
        }
        if (started.isFailure) {
            candidate.close()
            updateNotification()
            return
        }
        server = candidate
        val browserPort = browserController.start(address, pairingCodeProvider = { pairingCode })
        ReceiverMdnsResponder(
            address = address,
            port = candidate.port,
            browserPort = browserPort,
        ).also { responder ->
            responder.start()
                .onSuccess {
                    mdns = responder
                    Log.i(TAG, "Advertising receiver on ${address.hostAddress}:${candidate.port} browser=$browserPort")
                }
                .onFailure { failure ->
                    // Reported, because without the advertisement a host can only reach this
                    // receiver by having its address typed in — which looks like the feature not
                    // working rather than like discovery being unavailable.
                    Log.w(TAG, "Receiver advertisement unavailable; discovery will not find this TV", failure)
                    responder.close()
                }
        }
        // The phone's third discovery rung. Additive: a phone that never sends a broadcast is
        // unaffected, and a failure here costs the ladder one rung rather than the whole feature.
        ReceiverBroadcastResponder(
            address = address,
            servicePort = candidate.port,
            // The same name the TCP probe answers with, from the same place.
            modelName = { Build.MODEL.orEmpty().ifBlank { ReceiverProbe.FALLBACK_MODEL_NAME } },
        ).also { responder ->
            responder.start()
                .onSuccess { broadcastResponder = responder }
                .onFailure { failure ->
                    Log.w(TAG, "Broadcast discovery unavailable; the other rungs still work", failure)
                    responder.close()
                }
        }
        serverStateJob = scope.launch {
            candidate.state.collect(::applyServerState)
        }
        updateNotification()
    }

    private fun applyServerState(state: ReceiverState) {
        when (state) {
            ReceiverState.Starting -> _uiState.update {
                it.copy(
                    networkState = ReceiverNetworkState.STARTING,
                    peerName = null,
                    detail = "Starting receiver",
                    error = null,
                )
            }
            is ReceiverState.Listening -> _uiState.update {
                it.copy(
                    networkState = ReceiverNetworkState.LISTENING,
                    address = state.address,
                    port = state.port,
                    peerName = null,
                    detail = "Ready on the hotspot LAN",
                    error = null,
                )
            }
            is ReceiverState.Connected -> _uiState.update {
                it.copy(
                    networkState = ReceiverNetworkState.LISTENING,
                    peerName = state.peerName,
                    detail = "Connected to ${state.peerName}",
                    error = null,
                )
            }
            is ReceiverState.Failed -> {
                Log.e(TAG, "Receiver listener stopped: ${state.reason}")
                _uiState.update {
                    it.copy(
                        networkState = ReceiverNetworkState.WAITING,
                        peerName = null,
                        surfaceMode = SurfaceMode.IDLE,
                        title = "",
                        playbackState = PlaybackState.IDLE,
                        positionMs = 0,
                        durationMs = -1,
                        mirrorWidth = 0,
                        mirrorHeight = 0,
                        mirrorFrameReceived = false,
                        error = RECEIVER_START_FAILURE_MESSAGE,
                        detail = RECEIVER_START_FAILURE_MESSAGE,
                    )
                }
            }
        }
        updateNotification()
    }

    override fun onMedia(command: MediaCommandMessage) {
        Log.i(TAG, "Handling media command")
        scope.launch {
            when (command.action) {
                MediaAction.CLEAR -> clearSessionSurface()
                MediaAction.LOAD -> load(command)
            }
        }
    }

    override fun onMediaData(chunk: MediaDataMessage) {
        // Deliberately synchronous, with no coroutine launch: this is called once per chunk, in
        // wire order, from the connection's own read loop, which is exactly the ordering the bytes
        // on disk must preserve. An earlier version wrapped each chunk in scope.launch — since a
        // launched coroutine only *starts* in launch order and can then suspend at its own
        // withContext(Dispatchers.IO) hop, two chunks' writes could interleave or land out of
        // order, corrupting the reassembled file in a way that only surfaced as ExoPlayer being
        // unable to recognise the container. Blocking file I/O here is fine: onMediaData already
        // runs on the receiver server's own background dispatcher, never the main thread.
        when (val outcome = pushedMedia.accept(chunk.data.toByteArray(), chunk.isFinal)) {
            is PushedMediaSink.Outcome.Refused -> {
                Log.e(TAG, "Refused a pushed media chunk: ${outcome.reason}")
                reportFailure(outcome.reason)
            }

            is PushedMediaSink.Outcome.Completed -> Log.i(TAG, "Finished receiving pushed media file")
            PushedMediaSink.Outcome.Accepted -> Unit
        }
    }

    private fun load(command: MediaCommandMessage) {
        val uri = if (command.url.isBlank()) {
            val file = pushedMedia.pending
                ?: return reportFailure("no media was pushed to this TV to play")
            android.net.Uri.fromFile(file)
        } else {
            android.net.Uri.parse(command.url)
        }
        Log.i(TAG, "Preparing media player for ${command.mimeType} from $uri")
        browserController.releaseForCastSwitch("media load")
        val builder = MediaItem.Builder()
            .setUri(uri)
            .setMimeType(command.mimeType)
            .setMediaMetadata(MediaMetadata.Builder().setTitle(command.title).build())
        if (command.mimeType.startsWith("image/")) {
            builder.setImageDurationMs(DEFAULT_IMAGE_DURATION_MILLIS)
        }
        command.subtitleUrl?.let { subtitleUrl ->
            builder.setSubtitleConfigurations(
                listOf(
                    MediaItem.SubtitleConfiguration.Builder(android.net.Uri.parse(subtitleUrl))
                        .setMimeType(subtitleMime(subtitleUrl))
                        .setSelectionFlags(C.SELECTION_FLAG_DEFAULT)
                        .build(),
                ),
            )
        }
        _uiState.update {
            it.copy(
                surfaceMode = SurfaceMode.PLAYER,
                title = command.title,
                playbackState = PlaybackState.BUFFERING,
                positionMs = command.startPositionMs,
                durationMs = command.durationMs,
                mirrorFrameReceived = false,
                detail = "Loading ${command.title.ifBlank { "media" }}",
                error = null,
            )
        }
        try {
            player.setMediaItem(builder.build(), command.startPositionMs)
            player.prepare()
            player.playWhenReady = true
            Log.i(TAG, "Player prepare() returned; playbackState=${stateName(player.playbackState)}")
        } catch (error: Exception) {
            Log.e(TAG, "player.prepare() threw synchronously", error)
            throw error
        }
    }

    override fun onSurface(surface: SurfaceMessage) {
        // The cast server must never be allowed to turn a v2 Browser surface
        // into a receiver UI state. Browser ownership lives on its separately
        // authenticated TLS route; this is defence in depth for direct calls.
        if (surface.mode == SurfaceMode.BROWSER) {
            Log.w(TAG, "Ignoring browser surface on the ordinary cast route")
            return
        }
        scope.launch {
            if (surface.mode == SurfaceMode.IDLE) {
                clearSessionSurface()
                return@launch
            }
            // Mirror / presentation from Windows must own the glass: release any browser
            // surface first so Compose does not leave a destroyed WebView claimed as busy.
            browserController.releaseForCastSwitch("cast surface ${surface.mode}")
            if (surface.mode != SurfaceMode.PLAYER) player.pause()
            _uiState.update {
                it.copy(
                    surfaceMode = surface.mode,
                    title = surface.caption,
                    playbackState = PlaybackState.IDLE,
                    positionMs = 0,
                    durationMs = -1,
                    mirrorFrameReceived = false,
                    detail = surface.caption.ifBlank {
                        when (surface.mode) {
                            SurfaceMode.IDLE -> "Connected — choose something to cast"
                            SurfaceMode.PLAYER -> "Preparing media"
                            SurfaceMode.MIRROR -> "Preparing screen mirror"
                            SurfaceMode.PRESENTATION -> "Preparing second screen"
                            SurfaceMode.BROWSER -> "Secure browser session required"
                        }
                    },
                    error = null,
                )
            }
        }
    }

    override fun onControl(control: ControlMessage) {
        scope.launch {
            when (val event = control.event) {
                is TransportControl -> when (event.action) {
                    TransportAction.PLAY -> player.play()
                    TransportAction.PAUSE -> player.pause()
                    TransportAction.STOP -> clearSessionSurface()
                    TransportAction.SEEK_TO -> player.seekTo(event.positionMs)
                    TransportAction.NEXT -> if (player.hasNextMediaItem()) player.seekToNextMediaItem()
                    TransportAction.PREVIOUS -> if (player.hasPreviousMediaItem()) player.seekToPreviousMediaItem()
                }
                is VolumeControl -> {
                    val manager = getSystemService(AudioManager::class.java)
                    val maximum = manager.getStreamMaxVolume(AudioManager.STREAM_MUSIC)
                    manager.setStreamVolume(
                        AudioManager.STREAM_MUSIC,
                        (event.level * maximum).toInt().coerceIn(0, maximum),
                        0,
                    )
                }
                is KeyControl -> if (event.action == KeyAction.DOWN) dispatchTvKey(event.keyCode)
                else -> Unit
            }
        }
    }

    override fun onVideoConfig(config: VideoConfigMessage) {
        videoDecoder.configure(config)
        _uiState.update {
            it.copy(
                mirrorWidth = config.width,
                mirrorHeight = config.height,
                mirrorFrameReceived = false,
            )
        }
    }

    override fun onVideoPacket(packet: VideoPacket) {
        videoDecoder.queue(packet)
    }

    private fun onMirrorFrameRendered() {
        _uiState.update {
            if (
                !it.mirrorFrameReceived &&
                (it.surfaceMode == SurfaceMode.MIRROR || it.surfaceMode == SurfaceMode.PRESENTATION)
            ) {
                it.copy(
                    mirrorFrameReceived = true,
                    detail = if (it.surfaceMode == SurfaceMode.PRESENTATION) {
                        "Second screen active"
                    } else {
                        "Screen mirror active"
                    },
                )
            } else {
                it
            }
        }
    }

    override fun onAudioConfig(config: AudioConfigMessage) {
        audioDecoder.configure(config)
    }

    override fun onAudioPacket(packet: AudioPacket) {
        audioDecoder.queue(packet)
    }

    override fun onSessionEnded() {
        scope.launch {
            withContext(Dispatchers.IO) { pushedMedia.discard() }
            player.stop()
            player.clearMediaItems()
            _uiState.update {
                it.copy(
                    peerName = null,
                    surfaceMode = SurfaceMode.IDLE,
                    title = "",
                    playbackState = PlaybackState.IDLE,
                    positionMs = 0,
                    durationMs = -1,
                    mirrorWidth = 0,
                    mirrorHeight = 0,
                    mirrorFrameReceived = false,
                    detail = "Ready on the hotspot LAN",
                    error = null,
                )
            }
        }
    }

    override fun onPlaybackStateChanged(playbackState: Int) {
        Log.i(TAG, "Player state changed: ${stateName(playbackState)}")
        publishPlayerState()
    }
    override fun onIsPlayingChanged(isPlaying: Boolean) = publishPlayerState()

    override fun onPlayerError(error: PlaybackException) {
        Log.e(TAG, "Receiver playback failed: ${error.errorCodeName}", error)
        reportFailure(friendlyPlaybackError(error.errorCodeName))
        publishPlayerState()
    }

    private fun publishPlayerState() {
        val state = when {
            player.playerError != null -> PlaybackState.ERROR
            player.playbackState == Player.STATE_BUFFERING -> PlaybackState.BUFFERING
            player.playbackState == Player.STATE_ENDED -> PlaybackState.ENDED
            player.isPlaying -> PlaybackState.PLAYING
            player.mediaItemCount > 0 -> PlaybackState.PAUSED
            else -> PlaybackState.IDLE
        }
        val duration = player.duration.takeIf { it != C.TIME_UNSET && it >= 0 } ?: -1
        _uiState.update {
            it.copy(
                playbackState = state,
                positionMs = player.currentPosition.coerceAtLeast(0),
                durationMs = duration,
                detail = when (state) {
                    PlaybackState.BUFFERING -> "Buffering"
                    PlaybackState.PLAYING -> "Playing on this TV"
                    PlaybackState.PAUSED -> "Paused"
                    PlaybackState.ENDED -> "Finished"
                    PlaybackState.ERROR -> it.error ?: "Playback error"
                    PlaybackState.IDLE -> it.detail
                },
            )
        }
        server?.send(
            PlaybackStateMessage(
                state,
                player.currentPosition.coerceAtLeast(0),
                duration,
                _uiState.value.error.orEmpty(),
            ),
        )
    }

    private fun sendPeriodicState() {
        if (_uiState.value.surfaceMode == SurfaceMode.PLAYER) publishPlayerState()
        val video = videoDecoder.stats()
        val audio = audioDecoder.stats()
        server?.send(
            StatsMessage(
                receiverQueueDepth = maxOf(video.queueDepth, audio.queueDepth),
                decodeLatencyUs = maxOf(video.decodeLatencyUs, audio.decodeLatencyUs),
                roundTripTimeUs = 0,
                droppedVideoFrames = video.droppedFrames,
            ),
        )
    }

    private fun clearSessionSurface() {
        browserController.releaseForCastSwitch("clear session surface")
        player.stop()
        player.clearMediaItems()
        // A clear is also how the phone abandons a transfer it cancelled, so the partial file goes
        // with the played one.
        scope.launch(Dispatchers.IO) { pushedMedia.discard() }
        _uiState.update {
            it.copy(
                surfaceMode = SurfaceMode.IDLE,
                title = "",
                playbackState = PlaybackState.IDLE,
                positionMs = 0,
                durationMs = -1,
                mirrorWidth = 0,
                mirrorHeight = 0,
                mirrorFrameReceived = false,
                browser = BrowserSurfaceUi(),
                browserCursor = BrowserCursorUi(),
                detail = if (it.connected) "Connected — choose something to cast" else "Ready on the hotspot LAN",
                error = null,
            )
        }
        server?.send(PlaybackStateMessage(PlaybackState.IDLE))
    }

    fun attachBrowserTabSurface(surface: BrowserTabSurfacePort) = browserController.attachTabSurface(surface)

    fun detachBrowserTabSurface(surface: BrowserTabSurfacePort) = browserController.detachTabSurface(surface)

    /** Service-owned multi-page workspace (ADR-0023). Never Compose-owned with remember. */
    internal fun browserWorkspaceSession(): BrowserWorkspaceSession = workspaceSession
    fun openBrowserWorkspace() = browserController.openWorkspaceFromTv()
    fun closeBrowserWorkspace() = browserController.closeWorkspaceFromTv()

    private fun publishWorkspaceStateToHost() = browserController.publishWorkspaceStateToHost()

    fun setBrowserPageFocusEnabled(enabled: Boolean) = browserController.setPageFocusEnabled(enabled)

    fun onBrowserTabStateEvent(tabId: Long, event: BrowserStateEvent) =
        browserController.onTabStateEvent(tabId, event)

    fun onBrowserTabPageDialog(tabId: Long, pending: PendingJsDialog) =
        browserController.onTabPageDialog(tabId, pending)

    fun onBrowserTabFavicon(tabId: Long, bitmap: android.graphics.Bitmap) =
        browserController.onTabFavicon(tabId, bitmap)

    fun onBrowserFindChanged(
        tabId: Long,
        find: com.rextechnologies.flint.receiver.browser.BrowserFindState,
    ) = browserController.onFindChanged(tabId, find)

    fun openBrowserTab(url: String? = null) = browserController.openTabFromTv(url)

    fun openBrowserFromTv() = browserController.openFromTv()

    fun selectBrowserTab(tabId: Long) = browserController.selectTabFromTv(tabId)

    fun closeBrowserTab(tabId: Long) = browserController.closeTabFromTv(tabId)

    fun exitBrowserFullscreen() {
        browserController.exitFullscreen()
    }

    private fun stateName(playbackState: Int) = when (playbackState) {
        Player.STATE_IDLE -> "IDLE"
        Player.STATE_BUFFERING -> "BUFFERING"
        Player.STATE_READY -> "READY"
        Player.STATE_ENDED -> "ENDED"
        else -> "UNKNOWN($playbackState)"
    }

    private fun reportFailure(message: String) {
        scope.launch {
            _uiState.update { it.copy(error = message, detail = message, playbackState = PlaybackState.ERROR) }
            server?.send(PlaybackStateMessage(PlaybackState.ERROR, detail = message))
        }
    }

    private fun reportMirrorFailure(internalMessage: String) {
        Log.e(TAG, "Mirror decoder stopped: $internalMessage")
        reportFailure(MIRROR_FAILURE_MESSAGE)
    }

    private fun updateNotification() = notifications.update(receiverNotificationText(_uiState.value))

    override fun onDestroy() {
        serverStateJob?.cancel()
        server?.close()
        browserController.close()
        mdns?.close()
        broadcastResponder?.close()
        player.removeListener(this)
        player.release()
        videoDecoder.close()
        audioDecoder.close()
        scope.cancel()
        super.onDestroy()
    }

    private fun Long.safeDuration(): Long = if (this == C.TIME_UNSET || this < 0) Long.MAX_VALUE else this

    private fun subtitleMime(url: String): String = when (
        url.substringBefore(
            '?',
        ).substringAfterLast('.', "").lowercase()
    ) {
        "vtt" -> MimeTypes.TEXT_VTT
        "ssa", "ass" -> MimeTypes.TEXT_SSA
        "ttml", "xml" -> MimeTypes.APPLICATION_TTML
        else -> MimeTypes.APPLICATION_SUBRIP
    }

    companion object {
        const val ACTION_NEW_CODE = "com.rextechnologies.flint.receiver.NEW_CODE"
        const val ACTION_STOP = "com.rextechnologies.flint.receiver.STOP"
        private const val NETWORK_POLL_MILLIS = 2_000L
        private const val REPORT_INTERVAL_MILLIS = 500L
        private const val SEEK_STEP_MILLIS = 10_000L
        private const val DEFAULT_IMAGE_DURATION_MILLIS = 6_000L

        /**
         * Connect and read timeout for fetching local media from the paired PC.
         *
         * Media3's own default is 8 seconds, tuned for the open internet. This is a hotspot
         * LAN that this project has independently observed to be congested enough to wedge ADB
         * and re-lease DHCP mid-session; 30 seconds gives a fresh connection room to land
         * without turning a slow network into an indefinite hang.
         */
        internal const val LOCAL_MEDIA_TIMEOUT_MILLIS = 30_000
        private const val TAG = "FlintReceiver"
    }
}

/**
 * Whether resuming playback from [playbackState] needs an explicit seek to zero first.
 *
 * `Player.play()` is a no-op at `STATE_ENDED`: there is nothing left to render at the current
 * (end-of-item) position, so setting `playWhenReady = true` alone changes nothing observable. Every
 * other state can resume in place — `STATE_IDLE`/`STATE_BUFFERING` because there is no "current
 * position" restart would need to skip past, and `STATE_READY` because play is already meaningful
 * from wherever playback currently sits (mid-clip pause, seek, etc.).
 */
internal fun needsSeekToStartBeforeResuming(playbackState: Int): Boolean =
    playbackState == Player.STATE_ENDED

/**
 * Where a scrub of [deltaMs] from [positionMs] lands, clamped to `[0, durationMs]`.
 *
 * Callers pass an already-sanitized [durationMs] (see the private `Long.safeDuration()` on
 * [ReceiverService], which maps an unknown/unset duration to [Long.MAX_VALUE] rather than a
 * negative sentinel) — clamping to a genuinely negative upper bound would make every rewind land
 * on 0 and every fast-forward silently do nothing.
 */
internal fun seekTargetMs(positionMs: Long, deltaMs: Long, durationMs: Long): Long =
    (positionMs + deltaMs).coerceIn(0L, durationMs)
