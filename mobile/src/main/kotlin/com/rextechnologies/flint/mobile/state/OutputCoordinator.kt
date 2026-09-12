package com.rextechnologies.flint.mobile.state

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.media.projection.MediaProjection
import android.media.projection.MediaProjectionManager
import android.os.Handler
import android.os.Looper
import android.view.Surface
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import com.rextechnologies.flint.castcore.capability.PhoneCapabilities
import com.rextechnologies.flint.castcore.copy.ScreenCopy
import com.rextechnologies.flint.castcore.media.CodecChoice
import com.rextechnologies.flint.castcore.media.EncoderPolicy
import com.rextechnologies.flint.castcore.media.KeyFrameStrategy
import com.rextechnologies.flint.castcore.media.PresentationGeometry
import com.rextechnologies.flint.castcore.media.SendFailureWatch
import com.rextechnologies.flint.castcore.screen.SecondScreenScene
import com.rextechnologies.flint.mobile.LiveOutput
import com.rextechnologies.flint.mobile.OutputMode
import com.rextechnologies.flint.mobile.media.EncodedVideoSink
import com.rextechnologies.flint.mobile.media.ScreenEncoder
import com.rextechnologies.flint.mobile.media.SecondScreenHost
import com.rextechnologies.flint.mobile.net.CastConnection
import com.rextechnologies.flint.mobile.service.CastService
import com.rextechnologies.flint.mobile.ui.SecondScreenContent
import com.rextechnologies.flint.protocol.media.BitrateController
import com.rextechnologies.flint.protocol.media.LinkHealth
import com.rextechnologies.flint.protocol.media.LinkSample
import com.rextechnologies.flint.protocol.wire.CodecId
import com.rextechnologies.flint.protocol.wire.StatsMessage
import com.rextechnologies.flint.protocol.wire.SurfaceMessage
import com.rextechnologies.flint.protocol.wire.SurfaceMode
import com.rextechnologies.flint.protocol.wire.VideoConfigMessage
import com.rextechnologies.flint.protocol.wire.WireMessage
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.nio.ByteBuffer

/**
 * Starts, feeds and stops the two streaming modes.
 *
 * The two differ in exactly one place: where the pixels come from. A second screen draws Flint's own
 * Compose content into a private display this app created; a mirror points a `MediaProjection` at
 * the same encoder surface. Everything downstream of that surface — the encoder, the framing, the
 * bitrate control, the foreground service, the telemetry — is shared, which is why the second screen
 * was built first: it proves the whole path without a consent dialog or an API-level minefield.
 *
 * Three things end a session from inside this class, and each says so through [notices]: the
 * socket refusing a run of frames, the encoder giving up, and a stop from the notification, which
 * is the only control a locked phone has. [stop] is the one cleanup boundary for all of them.
 */
class OutputCoordinator(
    context: Context,
    private val session: SessionCoordinator,
    private val scope: CoroutineScope,
    private val clock: () -> Long = System::currentTimeMillis,
) {
    private val applicationContext = context.applicationContext

    private val mutable = MutableStateFlow<LiveOutput?>(null)
    val state: StateFlow<LiveOutput?> = mutable

    /** What the output path has to say when it stops on its own. The controller turns these into banners. */
    private val mutableNotices = MutableSharedFlow<String>(extraBufferCapacity = NOTICE_BUFFER)
    val notices: SharedFlow<String> = mutableNotices

    private val scene = MutableStateFlow<SecondScreenScene>(SecondScreenScene.Dashboard("", "", STABLE_WORD))

    private var encoder: ScreenEncoder? = null
    private var secondScreen: SecondScreenHost? = null
    private var projection: MediaProjection? = null
    private var mirrorDisplay: VirtualDisplay? = null
    private var bitrate: BitrateController? = null
    private var startedAtMillis: Long = 0

    /** What the running encoder was built from, kept so it can be rebuilt at another size or GOP. */
    private var running: RunningSession? = null

    /** Applied at most once per session, and never undone: an encoder that ignored one request ignores the next. */
    private var fallbackApplied = false

    @Volatile
    private var stopping = false

    init {
        session.onMessage(::onReceiverMessage)
        // The session going away takes the output with it. Without this a television that said BYE
        // would leave the phone encoding into a socket nobody is reading.
        scope.launch {
            session.state.collect { link ->
                if (link !is LinkState.Connected && mutable.value != null) stop()
            }
        }
        // The notification's Stop reaches the service and nothing else. Watching the service leave
        // the foreground is what makes that button stop the encoder too, rather than leaving it
        // encoding into a socket with the notification gone. Only the transition counts: the
        // service is not yet in the foreground during the moments a start is still under way.
        scope.launch {
            var wasForeground = false
            CastService.isForeground.collect { foreground ->
                if (wasForeground && !foreground && mutable.value != null && !stopping) stop()
                wasForeground = foreground
            }
        }
        scope.launch { tickElapsed() }
    }

    /**
     * Starts the second screen.
     *
     * Must be called from the main thread: a `Presentation` is a Dialog. The foreground service is
     * started first and typed `connectedDevice` rather than `mediaProjection`, because a second
     * screen captures nothing — declaring it as a projection would be a claim about what this app is
     * doing that is not true, and on API 34 and above it would also fail to start.
     *
     * The canvas is landscape 1080p whatever way the phone is held. It is a television.
     */
    suspend fun startSecondScreen(activity: Activity, phone: PhoneCapabilities): String? {
        val connection = session.active() ?: return ScreenCopy.NOT_PAIRED
        if (mutable.value != null) return null

        val deviceName = connectedDeviceName()
        CastService.start(applicationContext, OutputMode.SECOND_SCREEN, deviceName)

        val (width, height) = PresentationGeometry.canvas()
        val started = startEncoder(connection, phone, width, height, KeyFrameStrategy.OnDemand)
            ?: return startFailure()
        val surface = started.inputSurface ?: return startFailure()

        scene.value = SecondScreenScene.Dashboard(
            televisionName = deviceName,
            phoneName = phone.deviceName,
            linkWord = STABLE_WORD,
        )
        val host = SecondScreenHost(activity, width, height, PresentationGeometry.DENSITY_DPI)
        val outcome = host.start(surface) {
            val showing by scene.collectAsState()
            SecondScreenContent(showing)
        }
        if (outcome.isFailure) {
            stop()
            return ScreenCopy.encoderFailure(outcome.exceptionOrNull()?.message.orEmpty())
        }
        secondScreen = host

        session.send(SurfaceMessage(SurfaceMode.PRESENTATION, caption = phone.deviceName))
        publish(OutputMode.SECOND_SCREEN, deviceName, width, height, scene.value)
        return null
    }

    /**
     * Starts the mirror, with consent already granted.
     *
     * The ordering is the whole of this method. On API 34 and above `getMediaProjection` throws
     * unless a `mediaProjection`-typed foreground service is already running, and starting such a
     * service throws unless a projection is active — which is only not circular because the consent
     * result counts as the projection for the purpose of starting the service. So: service first,
     * wait until it is genuinely in the foreground, then take the projection.
     */
    suspend fun startMirror(
        phone: PhoneCapabilities,
        resultCode: Int,
        consent: Intent,
    ): String? {
        val connection = session.active() ?: return ScreenCopy.NOT_PAIRED
        if (mutable.value != null) return null

        val deviceName = connectedDeviceName()
        CastService.start(applicationContext, OutputMode.MIRROR, deviceName)
        if (!CastService.awaitForeground()) {
            stop()
            return ScreenCopy.encoderFailure("the casting service did not start")
        }

        val manager = applicationContext.getSystemService(MediaProjectionManager::class.java)
            ?: return startFailure()
        val granted = runCatching { manager.getMediaProjection(resultCode, consent) }.getOrNull()
            ?: return startFailure()
        projection = granted

        // Mandatory from API 34, and good manners before it: the system revokes a projection when
        // the person stops it from the status bar, and an app that did not register a callback finds
        // out by producing black frames.
        granted.registerCallback(
            object : MediaProjection.Callback() {
                override fun onStop() {
                    scope.launch { stop() }
                }
            },
            Handler(Looper.getMainLooper()),
        )

        val (width, height) = EncoderPolicy.scaleToFit(
            phone.screenWidth,
            phone.screenHeight,
            EncoderPolicy.MAXIMUM_LONG_EDGE,
        )
        val started = startEncoder(connection, phone, width, height, KeyFrameStrategy.OnDemand)
            ?: return startFailure()
        val surface = started.inputSurface ?: return startFailure()

        mirrorDisplay = runCatching {
            granted.createVirtualDisplay(
                MIRROR_DISPLAY_NAME,
                width,
                height,
                phone.densityDpi,
                DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,
                surface,
                null,
                null,
            )
        }.getOrNull() ?: return startFailure()

        session.send(SurfaceMessage(SurfaceMode.MIRROR, caption = phone.deviceName))
        publish(OutputMode.MIRROR, deviceName, width, height, scene = null)
        return null
    }

    /**
     * Follows the phone round.
     *
     * The projection and the display are kept: on Android 14 and above a consent result is good for
     * one capture, so asking for a second would mean a second dialog mid-rotation. What changes is
     * the encoder, which cannot be resized, and the display's geometry. The order matters. The new
     * encoder is started first so no frame is lost to a gap; the display is detached before it is
     * resized so no frame of the wrong size reaches either codec; then it is attached to the new
     * surface and the old encoder is stopped. The new encoder's own VIDEO_CONFIG follows, with the
     * sync-frame request `publishConfig` makes, which is what the receiver's decoder reset needs.
     */
    suspend fun reconfigureMirror(screenWidth: Int, screenHeight: Int, densityDpi: Int) {
        val live = mutable.value ?: return
        if (live.mode != OutputMode.MIRROR) return
        val display = mirrorDisplay ?: return
        val (width, height) = EncoderPolicy.scaleToFit(
            screenWidth.coerceAtLeast(1),
            screenHeight.coerceAtLeast(1),
            EncoderPolicy.MAXIMUM_LONG_EDGE,
        )
        if (width == live.width && height == live.height) return

        val swapped = swapEncoder(width, height, currentStrategy()) { surface ->
            runCatching {
                display.surface = null
                display.resize(width, height, densityDpi)
                display.surface = surface
            }.isSuccess
        }
        if (!swapped) {
            stopBecause(ScreenCopy.encoderFailure("the encoder could not be rebuilt for the new orientation"))
            return
        }
        mutable.update { it?.copy(width = width, height = height) }
    }

    /** Feeds a scene to a live second screen. Ignored, harmlessly, when nothing is showing. */
    fun showScene(next: SecondScreenScene) {
        scene.value = next
        mutable.update { live -> live?.takeIf { it.mode == OutputMode.SECOND_SCREEN }?.copy(scene = next) ?: live }
    }

    /** Stops whatever is running, in the order that leaves nothing pointing at a released surface. */
    suspend fun stop() {
        // A start can fail after the foreground service has been requested but before an encoder or
        // LiveOutput exists. That half-start still needs a stop intent; returning here used to leave
        // its notification and wake lock alive with no output that could later clean them up.
        if (
            mutable.value == null &&
            encoder == null &&
            secondScreen == null &&
            projection == null &&
            mirrorDisplay == null
        ) {
            CastService.stop(applicationContext)
            return
        }
        stopping = true
        try {
            withContext(Dispatchers.Main.immediate) {
                runCatching { secondScreen?.close() }
                secondScreen = null
            }
            runCatching { mirrorDisplay?.release() }
            mirrorDisplay = null
            runCatching { projection?.stop() }
            projection = null
            withContext(Dispatchers.Default) { runCatching { encoder?.stop() } }
            encoder = null
            bitrate = null
            running = null
            fallbackApplied = false

            session.send(SurfaceMessage(SurfaceMode.IDLE))
            CastService.stop(applicationContext)
            mutable.value = null
        } finally {
            stopping = false
        }
    }

    private suspend fun stopBecause(reason: String) {
        if (stopping || mutable.value == null) return
        stop()
        mutableNotices.tryEmit(reason)
    }

    /** What this session's encoder was built from, so a rebuild changes one thing at a time. */
    private class RunningSession(
        val connection: CastConnection,
        val phone: PhoneCapabilities,
        val codec: CodecId,
        val strategy: KeyFrameStrategy,
    )

    /**
     * Starts an encoder for the first codec both ends can use, falling back to the next when the
     * platform refuses to configure one.
     *
     * The order is [CodecChoice]'s, bounded by what the receiver said it decodes in its HELLO and
     * what this phone's probe found it can encode. It used to be the first element of a set, which
     * is whichever codec the hash table happened to put first.
     */
    private fun startEncoder(
        connection: CastConnection,
        phone: PhoneCapabilities,
        width: Int,
        height: Int,
        strategy: KeyFrameStrategy,
    ): ScreenEncoder? {
        val receiverCodecs = (session.state.value as? LinkState.Connected)
            ?.parameters?.peer?.codecCapabilities.orEmpty()
        val controller = BitrateController()
        bitrate = controller
        for (codec in CodecChoice.order(receiverCodecs, phone.advertisedCodecs)) {
            val created = buildEncoder(connection, phone, codec, width, height, strategy, controller.currentBitrate)
            if (runCatching { created.start() }.isSuccess) {
                encoder = created
                running = RunningSession(connection, phone, codec, strategy)
                return created
            }
        }
        return null
    }

    private fun buildEncoder(
        connection: CastConnection,
        phone: PhoneCapabilities,
        codec: CodecId,
        width: Int,
        height: Int,
        strategy: KeyFrameStrategy,
        bitrateBitsPerSecond: Int,
    ): ScreenEncoder {
        val config = EncoderPolicy.forSession(
            codec = codec,
            sourceWidth = width,
            sourceHeight = height,
            bitrateBitsPerSecond = bitrateBitsPerSecond,
            apiLevel = phone.apiLevel,
            keyFrameStrategy = strategy,
        )
        return ScreenEncoder(config, Sink(connection))
    }

    /**
     * Replaces the running encoder with one built at [width] by [height] with [strategy].
     *
     * Same codec, same session, same source. [attach] points the source at the new input surface
     * and answers whether it managed to; only then is the old encoder stopped, so the source is never
     * left pointing at a surface that has been released.
     */
    private suspend fun swapEncoder(
        width: Int,
        height: Int,
        strategy: KeyFrameStrategy,
        attach: (Surface) -> Boolean,
    ): Boolean {
        val current = running ?: return false
        val old = encoder ?: return false
        val fresh = buildEncoder(
            current.connection,
            current.phone,
            current.codec,
            width,
            height,
            strategy,
            bitrate?.currentBitrate ?: BitrateController().currentBitrate,
        )
        val started = withContext(Dispatchers.Default) { runCatching { fresh.start() }.isSuccess }
        val surface = fresh.inputSurface
        if (!started || surface == null) {
            withContext(Dispatchers.Default) { runCatching { fresh.stop() } }
            return false
        }
        if (!attach(surface)) {
            withContext(Dispatchers.Default) { runCatching { fresh.stop() } }
            return false
        }
        encoder = fresh
        running = RunningSession(current.connection, current.phone, current.codec, strategy)
        withContext(Dispatchers.Default) { runCatching { old.stop() } }
        return true
    }

    private fun currentStrategy(): KeyFrameStrategy = running?.strategy ?: KeyFrameStrategy.OnDemand

    /**
     * The documented exception to on-demand key frames, applied.
     *
     * There is no parameter that changes a running codec's interval, so the fallback is a restart
     * with a bounded GOP, retargeting whichever source is feeding the encoder. Once per session,
     * guarded so a device that also ignores the bounded interval cannot restart the encoder in a
     * loop; the strip says what was done and why.
     */
    private suspend fun applyKeyFrameFallback(strategy: KeyFrameStrategy.BoundedInterval) {
        val live = mutable.value ?: return
        if (fallbackApplied) return
        fallbackApplied = true
        val swapped = swapEncoder(live.width, live.height, strategy) { surface ->
            when (live.mode) {
                OutputMode.MIRROR -> runCatching { mirrorDisplay?.surface = surface }.isSuccess && mirrorDisplay != null
                OutputMode.SECOND_SCREEN -> secondScreen?.retarget(surface) == true
                OutputMode.NONE -> false
            }
        }
        mutable.update {
            it?.copy(
                keyFrameFallback = swapped,
                degradedReason = if (swapped) ScreenCopy.keyFrameFallback(strategy.seconds) else it.degradedReason,
            )
        }
    }

    private suspend fun startFailure(): String {
        stop()
        return ScreenCopy.encoderFailure("")
    }

    private fun connectedDeviceName(): String =
        (session.state.value as? LinkState.Connected)?.device?.displayName.orEmpty()

    private fun publish(
        mode: OutputMode,
        deviceName: String,
        width: Int,
        height: Int,
        scene: SecondScreenScene?,
    ) {
        val codec = running?.codec ?: return
        startedAtMillis = clock()
        mutable.value = LiveOutput(
            mode = mode,
            deviceName = deviceName,
            width = width,
            height = height,
            codec = codec,
            elapsedSeconds = 0,
            health = LinkHealth.Stable,
            bitrateBitsPerSecond = bitrate?.currentBitrate ?: 0,
            scene = scene,
        )
    }

    /**
     * The receiver's own account of how it is coping, every five hundred milliseconds.
     *
     * Its round-trip figure is discarded: it reports zero because it never measures one, and feeding
     * that to the controller would tell it the link is perfect no matter what else is true. The
     * sender-side queue depth is the honest signal, and it is the one this phone can actually see.
     */
    private fun onReceiverMessage(message: WireMessage) {
        if (message !is StatsMessage) return
        val controller = bitrate ?: return
        val active = encoder ?: return
        val connection = session.active()

        val decision = controller.onSample(
            LinkSample(
                receiverQueueDepth = message.receiverQueueDepth,
                decodeLatencyUs = message.decodeLatencyUs,
                roundTripTimeUs = 0,
                droppedVideoFrames = message.droppedVideoFrames,
                pendingSendBytes = connection?.pendingSendBytes() ?: 0,
            ),
        )
        active.setBitrate(decision.bitrateBitsPerSecond)
        if (decision.requestKeyFrame) active.requestKeyFrame()

        val linkWord = ScreenCopy.linkHealthWord(decision.health)
        mutable.update { live ->
            live?.copy(
                health = decision.health,
                bitrateBitsPerSecond = decision.bitrateBitsPerSecond,
                degradedReason = when {
                    decision.health == LinkHealth.Congested -> ScreenCopy.CONGESTED_EXPLANATION
                    live.keyFrameFallback -> live.degradedReason
                    else -> null
                },
            )
        }
        // The dashboard carries the link's word too, so the television says the same thing the phone does.
        val showing = scene.value
        if (showing is SecondScreenScene.Dashboard && showing.linkWord != linkWord) {
            showScene(showing.copy(linkWord = linkWord))
        }
    }

    private suspend fun tickElapsed() {
        while (scope.isActive) {
            delay(ELAPSED_TICK_MILLIS)
            if (mutable.value == null) continue
            val seconds = (clock() - startedAtMillis) / 1_000
            mutable.update { it?.copy(elapsedSeconds = seconds.coerceAtLeast(0)) }
        }
    }

    /** Everything the encoder produces, framed and written to the socket the session owns. */
    private inner class Sink(private val connection: CastConnection) : EncodedVideoSink {
        private val failures = SendFailureWatch()

        override fun onVideoConfig(message: VideoConfigMessage) {
            connection.send(message)
        }

        override fun onVideoPacket(presentationTimeUs: Long, keyFrame: Boolean, data: ByteBuffer) {
            val sent = connection.sendVideoPacket(presentationTimeUs, keyFrame, data)
            if (failures.onWrite(sent)) {
                scope.launch { stopBecause(ScreenCopy.LINK_STOPPED) }
            }
        }

        override fun onKeyFrameStrategyChanged(strategy: KeyFrameStrategy.BoundedInterval) {
            scope.launch { applyKeyFrameFallback(strategy) }
        }

        override fun onEncoderFailed(detail: String) {
            scope.launch { stopBecause(ScreenCopy.encoderFailure(detail)) }
        }
    }

    private companion object {
        const val MIRROR_DISPLAY_NAME = "flint-mirror"
        const val ELAPSED_TICK_MILLIS = 1_000L
        const val NOTICE_BUFFER = 4
        val STABLE_WORD = ScreenCopy.linkHealthWord(LinkHealth.Stable)
    }
}
