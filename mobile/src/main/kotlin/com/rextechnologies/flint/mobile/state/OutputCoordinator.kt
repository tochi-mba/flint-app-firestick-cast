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
import com.rextechnologies.flint.castcore.capability.PhoneCapabilities
import com.rextechnologies.flint.castcore.copy.ScreenCopy
import com.rextechnologies.flint.castcore.media.EncoderPolicy
import com.rextechnologies.flint.castcore.media.KeyFrameStrategy
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
import com.rextechnologies.flint.protocol.wire.StatsMessage
import com.rextechnologies.flint.protocol.wire.SurfaceMessage
import com.rextechnologies.flint.protocol.wire.SurfaceMode
import com.rextechnologies.flint.protocol.wire.VideoConfigMessage
import com.rextechnologies.flint.protocol.wire.WireMessage
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
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

    private var encoder: ScreenEncoder? = null
    private var secondScreen: SecondScreenHost? = null
    private var projection: MediaProjection? = null
    private var mirrorDisplay: VirtualDisplay? = null
    private var bitrate: BitrateController? = null
    private var startedAtMillis: Long = 0

    init {
        session.onMessage(::onReceiverMessage)
        // The session going away takes the output with it. Without this a television that said BYE
        // would leave the phone encoding into a socket nobody is reading.
        scope.launch {
            session.state.collect { link ->
                if (link !is LinkState.Connected && mutable.value != null) stop()
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
     */
    suspend fun startSecondScreen(activity: Activity, phone: PhoneCapabilities): String? {
        val connection = session.active() ?: return ScreenCopy.NOT_PAIRED
        if (mutable.value != null) return null

        val deviceName = (session.state.value as? LinkState.Connected)?.device?.displayName.orEmpty()
        CastService.start(applicationContext, OutputMode.SECOND_SCREEN, deviceName)

        val (width, height) = EncoderPolicy.scaleToFit(
            phone.screenWidth,
            phone.screenHeight,
            EncoderPolicy.MAXIMUM_LONG_EDGE,
        )
        val started = startEncoder(connection, phone, width, height) ?: return startFailure()
        val surface = started.inputSurface ?: return startFailure()

        val host = SecondScreenHost(activity, width, height, phone.densityDpi)
        val outcome = host.start(surface) { SecondScreenContent(deviceName = deviceName) }
        if (outcome.isFailure) {
            stop()
            return ScreenCopy.encoderFailure(outcome.exceptionOrNull()?.message.orEmpty())
        }
        secondScreen = host

        session.send(SurfaceMessage(SurfaceMode.PRESENTATION, caption = phone.deviceName))
        publish(OutputMode.SECOND_SCREEN, deviceName, width, height)
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

        val deviceName = (session.state.value as? LinkState.Connected)?.device?.displayName.orEmpty()
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
        val started = startEncoder(connection, phone, width, height) ?: return startFailure()
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
        publish(OutputMode.MIRROR, deviceName, width, height)
        return null
    }

    /** Stops whatever is running, in the order that leaves nothing pointing at a released surface. */
    suspend fun stop() {
        if (mutable.value == null && encoder == null) return
        withContext(Dispatchers.Main.immediate) {
            runCatching { secondScreen?.close() }
            secondScreen = null
        }
        runCatching { mirrorDisplay?.release() }
        mirrorDisplay = null
        runCatching { projection?.stop() }
        projection = null
        runCatching { encoder?.stop() }
        encoder = null
        bitrate = null

        session.send(SurfaceMessage(SurfaceMode.IDLE))
        CastService.stop(applicationContext)
        mutable.value = null
    }

    private fun startEncoder(
        connection: CastConnection,
        phone: PhoneCapabilities,
        width: Int,
        height: Int,
    ): ScreenEncoder? {
        val codec = phone.advertisedCodecs.firstOrNull() ?: return null
        val controller = BitrateController()
        bitrate = controller
        val config = EncoderPolicy.forSession(
            codec = codec,
            sourceWidth = width,
            sourceHeight = height,
            bitrateBitsPerSecond = controller.currentBitrate,
            apiLevel = phone.apiLevel,
        )
        val created = ScreenEncoder(config, Sink(connection))
        return runCatching {
            created.start()
            created
        }.getOrNull().also { encoder = it }
    }

    private suspend fun startFailure(): String {
        stop()
        return ScreenCopy.encoderFailure("")
    }

    private fun publish(mode: OutputMode, deviceName: String, width: Int, height: Int) {
        startedAtMillis = clock()
        mutable.value = LiveOutput(
            mode = mode,
            deviceName = deviceName,
            width = width,
            height = height,
            elapsedSeconds = 0,
            health = LinkHealth.Stable,
            bitrateBitsPerSecond = bitrate?.currentBitrate ?: 0,
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

        mutable.update { live ->
            live?.copy(
                health = decision.health,
                bitrateBitsPerSecond = decision.bitrateBitsPerSecond,
                degradedReason = ScreenCopy.CONGESTED_EXPLANATION
                    .takeIf { decision.health == LinkHealth.Congested },
            )
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
        override fun onVideoConfig(message: VideoConfigMessage) {
            connection.send(message)
        }

        override fun onVideoPacket(presentationTimeUs: Long, keyFrame: Boolean, data: ByteBuffer) {
            connection.sendVideoPacket(presentationTimeUs, keyFrame, data)
        }

        override fun onKeyFrameStrategyChanged(strategy: KeyFrameStrategy.BoundedInterval) {
            // Restarting the encoder with a bounded interval is slice 08's work; what matters now is
            // that the observation is not silently dropped, because it is the one piece of evidence
            // that a device ignores a sync-frame request.
            scope.launch {
                mutable.update {
                    it?.copy(
                        degradedReason = "This phone's encoder ignores a request for a key frame, " +
                            "so Flint will send one every ${strategy.seconds} seconds instead.",
                    )
                }
            }
        }

        override fun onEncoderFailed(detail: String) {
            scope.launch { stop() }
        }
    }

    private companion object {
        const val MIRROR_DISPLAY_NAME = "flint-mirror"
        const val ELAPSED_TICK_MILLIS = 1_000L
    }
}
