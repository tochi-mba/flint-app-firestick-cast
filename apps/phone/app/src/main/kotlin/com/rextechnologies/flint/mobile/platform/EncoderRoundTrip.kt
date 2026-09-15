package com.rextechnologies.flint.mobile.platform

import android.app.Activity
import android.app.Presentation
import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.ImageFormat
import android.graphics.Paint
import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.media.Image
import android.media.ImageReader
import android.media.MediaCodec
import android.media.MediaFormat
import android.os.Bundle
import android.os.Handler
import android.os.HandlerThread
import android.util.DisplayMetrics
import android.view.Display
import android.view.Surface
import android.view.View
import android.view.ViewGroup
import com.rextechnologies.flint.castcore.capability.ProbeOutcome
import com.rextechnologies.flint.castcore.media.CodecNames
import com.rextechnologies.flint.castcore.media.EncoderPolicy
import com.rextechnologies.flint.castcore.media.KeyFrameStrategy
import com.rextechnologies.flint.castcore.media.PatternCheck
import com.rextechnologies.flint.castcore.media.PlaneBytes
import com.rextechnologies.flint.castcore.media.Quadrant
import com.rextechnologies.flint.castcore.media.Rgb
import com.rextechnologies.flint.castcore.media.TestPattern
import com.rextechnologies.flint.castcore.media.YuvSampler
import com.rextechnologies.flint.mobile.media.EncodedVideoSink
import com.rextechnologies.flint.mobile.media.ScreenEncoder
import com.rextechnologies.flint.mobile.media.VideoMime
import com.rextechnologies.flint.protocol.wire.CodecId
import com.rextechnologies.flint.protocol.wire.VideoConfigMessage
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import java.nio.ByteBuffer
import java.util.concurrent.atomic.AtomicReference

/**
 * Draws a known frame through the production encoder and checks what a decoder makes of it.
 *
 * A codec test that only checks structure is not a codec test. This project has already shipped an
 * encoder whose output was correctly framed, carried correct parameter sets and decoded, frame after
 * frame, to flat green. Every structural assertion passed on the way out. So this check draws four
 * coloured quadrants into a display only this app can see, encodes them with [ScreenEncoder]
 * exactly as a session would, decodes the result with a second codec on this phone, and reads the
 * pixels back. Nothing appears on any television and nothing is sent anywhere.
 *
 * The verdict distinguishes three things. A display that could not be created means the check
 * could not run, which is [ProbeOutcome.NOT_PROBED]: a mirror through a projection may still work.
 * An encoder that refused to start, produced no key frame, or produced frames that decoded to
 * something other than the pattern is [ProbeOutcome.UNSUPPORTED]. Only a frame that came back is
 * [ProbeOutcome.SUPPORTED].
 */
object EncoderRoundTrip {
    /** What the check found, and one sentence saying so. */
    data class Outcome(val outcome: ProbeOutcome, val detail: String)

    suspend fun run(activity: Activity, codec: CodecId, apiLevel: Int): Outcome {
        val label = CodecNames.label(codec)
        val sink = RecordingSink()
        // Building the policy and the encoder was the one part of this check with nothing around
        // it. Neither is expected to fail, which is exactly why it was unguarded -- and an
        // unguarded throw here did not fail the check, it closed the app.
        val encoder = runCatching {
            val config = EncoderPolicy.forSession(
                codec = codec,
                sourceWidth = WIDTH,
                sourceHeight = HEIGHT,
                bitrateBitsPerSecond = BITRATE,
                apiLevel = apiLevel,
            )
            ScreenEncoder(config, sink)
        }.getOrElse { failure ->
            return Outcome(
                ProbeOutcome.UNSUPPORTED,
                "Flint could not set up a $label encoder to check with: ${reason(failure)}.",
            )
        }

        val startFailure = withContext(Dispatchers.Default) {
            runCatching { encoder.start() }.exceptionOrNull()
        }
        if (startFailure != null) {
            return Outcome(
                ProbeOutcome.UNSUPPORTED,
                "The $label encoder refused to start: ${reason(startFailure)}.",
            )
        }

        try {
            val surface = encoder.inputSurface
                ?: return Outcome(ProbeOutcome.UNSUPPORTED, "The $label encoder started without an input surface.")

            drawPattern(activity, surface, sink)?.let { return it }

            val recorded = sink.snapshot()
            val videoConfig = recorded.config
                ?: return Outcome(ProbeOutcome.UNSUPPORTED, "The $label encoder produced frames but no VIDEO_CONFIG.")

            // Stopped before decoding rather than after: the tail of the stream reaches the sink on
            // stop, and a phone with one hardware codec instance to spare cannot open the decoder
            // while the encoder still holds it.
            // Guarded like the one in `finally`. A codec that objects to being stopped is not a
            // reason to take the app down with it.
            withContext(Dispatchers.Default) { runCatching { encoder.stop() } }

            return withContext(Dispatchers.Default) { decode(codec, videoConfig, recorded.packets) }
        } finally {
            withContext(NonCancellable + Dispatchers.Default) { runCatching { encoder.stop() } }
        }
    }

    /**
     * Shows the pattern on a private display backed by the encoder's surface until enough has been
     * encoded, then takes everything down again. Main thread throughout: a `Presentation` is a
     * Dialog, and the frames it produces are drawn by messages on this looper, which is why this
     * suspends here rather than blocking.
     */
    private suspend fun drawPattern(activity: Activity, surface: Surface, sink: RecordingSink): Outcome? =
        withContext(Dispatchers.Main.immediate) {
            val manager = activity.getSystemService(DisplayManager::class.java)
                ?: return@withContext Outcome(ProbeOutcome.NOT_PROBED, "This phone has no display manager.")

            var display: VirtualDisplay? = null
            var presentation: PatternPresentation? = null
            try {
                display = runCatching {
                    manager.createVirtualDisplay(
                        DISPLAY_NAME,
                        WIDTH,
                        HEIGHT,
                        DisplayMetrics.DENSITY_DEFAULT,
                        surface,
                        // Private. PUBLIC is the flag that would need a system permission.
                        0,
                    )
                }.getOrNull() ?: return@withContext Outcome(
                    ProbeOutcome.NOT_PROBED,
                    "This phone refused to create the display the check draws into.",
                )

                presentation = runCatching {
                    PatternPresentation(activity, display.display).also { it.show() }
                }.getOrElse { failure ->
                    return@withContext Outcome(
                        ProbeOutcome.NOT_PROBED,
                        "This phone would not show the test pattern: ${reason(failure)}.",
                    )
                }

                withTimeoutOrNull(CAPTURE_TIMEOUT_MILLIS) { sink.enough.await() }
                sink.problem()?.let { Outcome(ProbeOutcome.UNSUPPORTED, it) }
            } finally {
                runCatching { presentation?.dismiss() }
                runCatching { display?.release() }
            }
        }

    /**
     * Decodes the recorded packets into an `ImageReader` and looks for the pattern.
     *
     * The reader is asked for planar YUV rather than RGBA because that is what a decoder produces;
     * a request for RGBA is honoured by some devices, ignored by others and converted by a few,
     * and none of those is the encoder being checked.
     */
    private suspend fun decode(codec: CodecId, config: VideoConfigMessage, packets: List<Packet>): Outcome {
        val label = CodecNames.label(codec)
        val mime = VideoMime.of(codec)
        val thread = HandlerThread("flint-round-trip").apply { start() }
        val verdict = CompletableDeferred<Outcome>()
        val lastDetail = AtomicReference("the decoder produced no frame to look at")

        // Outside every try until now. A phone that refuses this size or format threw from here
        // straight out of the check and into the caller, which had no handler for it.
        val reader = runCatching {
            ImageReader.newInstance(config.width, config.height, ImageFormat.YUV_420_888, READER_IMAGES)
        }.getOrElse { failure ->
            thread.quitSafely()
            return Outcome(
                ProbeOutcome.NOT_PROBED,
                "This phone would not open a ${config.width}×${config.height} image reader to check " +
                    "the $label encoder's output with: ${reason(failure)}.",
            )
        }
        reader.setOnImageAvailableListener(
            { source ->
                // Everything in here runs on the HandlerThread below, which the coroutine that
                // started this decode does not own and cannot catch for. An exception escaping a
                // listener reaches the default uncaught handler, and on Android that ends the
                // process -- so a decoder handing back a frame shaped differently from the one
                // this code expects would close the whole app rather than fail a check.
                runCatching {
                    val image = source.acquireLatestImage() ?: return@runCatching
                    try {
                        val result = PatternCheck.verdict(sample(image))
                        if (result.passed) {
                            val size = "${config.width}×${config.height}"
                            verdict.complete(
                                Outcome(
                                    ProbeOutcome.SUPPORTED,
                                    "${result.detail.removeSuffix(".")}, through $label at $size.",
                                ),
                            )
                        } else {
                            lastDetail.set(result.detail)
                        }
                    } finally {
                        image.close()
                    }
                }.onFailure { failure ->
                    lastDetail.set("the decoded frame could not be read: ${reason(failure)}")
                }
            },
            Handler(thread.looper),
        )

        val decoder = runCatching { MediaCodec.createDecoderByType(mime) }.getOrElse { failure ->
            thread.quitSafely()
            reader.close()
            return Outcome(
                ProbeOutcome.UNSUPPORTED,
                "This phone has no $label decoder to check with: ${reason(failure)}.",
            )
        }
        try {
            val format = MediaFormat.createVideoFormat(mime, config.width, config.height)
            config.codecSpecificData.forEachIndexed { index, data ->
                format.setByteBuffer("csd-$index", ByteBuffer.wrap(data.toByteArray()))
            }
            decoder.configure(format, reader.surface, null, 0)
            decoder.start()
            feed(decoder, packets, verdict)
            return withTimeoutOrNull(DECODE_TIMEOUT_MILLIS) { verdict.await() }
                ?: Outcome(
                    ProbeOutcome.UNSUPPORTED,
                    "The frames the $label encoder produced did not decode to the pattern that was " +
                        "drawn: ${lastDetail.get()}",
                )
        } catch (failure: Throwable) {
            return Outcome(ProbeOutcome.UNSUPPORTED, "Decoding the $label encoder's output failed: ${reason(failure)}.")
        } finally {
            runCatching { decoder.stop() }
            runCatching { decoder.release() }
            runCatching { reader.setOnImageAvailableListener(null, null) }
            runCatching { reader.close() }
            thread.quitSafely()
        }
    }

    /** Queues every packet, then the end of stream, rendering each decoded frame into the reader. */
    private fun feed(decoder: MediaCodec, packets: List<Packet>, verdict: CompletableDeferred<Outcome>) {
        val info = MediaCodec.BufferInfo()
        var next = 0
        var endQueued = false
        val deadline = System.nanoTime() + FEED_TIMEOUT_MILLIS * 1_000_000L
        while (!verdict.isCompleted && System.nanoTime() < deadline) {
            if (!endQueued) {
                val index = decoder.dequeueInputBuffer(DEQUEUE_TIMEOUT_MICROS)
                if (index >= 0) {
                    if (next < packets.size) {
                        val packet = packets[next++]
                        val buffer = decoder.getInputBuffer(index)
                        if (buffer == null || buffer.capacity() < packet.bytes.size) {
                            decoder.queueInputBuffer(index, 0, 0, 0, 0)
                        } else {
                            buffer.clear()
                            buffer.put(packet.bytes)
                            decoder.queueInputBuffer(index, 0, packet.bytes.size, packet.presentationTimeUs, 0)
                        }
                    } else {
                        decoder.queueInputBuffer(index, 0, 0, 0, MediaCodec.BUFFER_FLAG_END_OF_STREAM)
                        endQueued = true
                    }
                }
            }
            val output = decoder.dequeueOutputBuffer(info, DEQUEUE_TIMEOUT_MICROS)
            if (output >= 0) {
                decoder.releaseOutputBuffer(output, info.size > 0)
                if (info.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0) return
            }
        }
    }

    private fun sample(image: Image): Map<Quadrant, Rgb> {
        val planes = image.planes
        if (planes.size < 3) return emptyMap()
        val luma = planeBytes(planes[0])
        val cb = planeBytes(planes[1])
        val cr = planeBytes(planes[2])
        return buildMap {
            Quadrant.entries.forEach { quadrant ->
                val (x, y) = TestPattern.samplePoint(quadrant, image.width, image.height)
                YuvSampler.sample(luma, cb, cr, x, y)?.let { put(quadrant, it) }
            }
        }
    }

    private fun planeBytes(plane: Image.Plane): PlaneBytes {
        val buffer = plane.buffer
        val bytes = ByteArray(buffer.remaining())
        buffer.duplicate().get(bytes)
        return PlaneBytes(bytes, plane.rowStride, plane.pixelStride)
    }

    private fun reason(failure: Throwable): String =
        failure.message.orEmpty().ifBlank { failure.javaClass.simpleName }

    private class Packet(val presentationTimeUs: Long, val keyFrame: Boolean, val bytes: ByteArray)

    private class Recorded(val config: VideoConfigMessage?, val packets: List<Packet>)

    /**
     * Keeps what the encoder produces until there is enough of it to decode.
     *
     * Copies every packet, which is fine here and would not be on the frame path: this is a check
     * that runs once, not the steady state.
     */
    private class RecordingSink : EncodedVideoSink {
        val enough = CompletableDeferred<Unit>()

        @Volatile
        private var config: VideoConfigMessage? = null

        @Volatile
        private var failure: String? = null

        private val packets = mutableListOf<Packet>()

        override fun onVideoConfig(message: VideoConfigMessage) {
            config = message
            settle()
        }

        override fun onVideoPacket(presentationTimeUs: Long, keyFrame: Boolean, data: ByteBuffer) {
            synchronized(packets) {
                if (packets.size >= MAXIMUM_PACKETS) return
                val copy = ByteArray(data.remaining())
                data.duplicate().get(copy)
                packets += Packet(presentationTimeUs, keyFrame, copy)
            }
            settle()
        }

        override fun onKeyFrameStrategyChanged(strategy: KeyFrameStrategy.BoundedInterval) = Unit

        override fun onEncoderFailed(detail: String) {
            failure = detail.ifBlank { "no reason given" }
            enough.complete(Unit)
        }

        fun snapshot(): Recorded = synchronized(packets) { Recorded(config, packets.toList()) }

        /** Why the recording is not usable, or `null` when it is. */
        fun problem(): String? {
            failure?.let { return "The encoder stopped while the pattern was being drawn: $it." }
            val recorded = snapshot()
            return when {
                recorded.config == null && recorded.packets.isEmpty() ->
                    "The encoder produced nothing within ${CAPTURE_TIMEOUT_MILLIS / 1_000} seconds of the " +
                        "pattern being drawn."

                recorded.config == null -> "The encoder produced frames but never a VIDEO_CONFIG."
                recorded.packets.none { it.keyFrame } ->
                    "The encoder produced ${recorded.packets.size} frames and none of them was a key frame."

                recorded.packets.size < MINIMUM_PACKETS ->
                    "The encoder produced only ${recorded.packets.size} frames within " +
                        "${CAPTURE_TIMEOUT_MILLIS / 1_000} seconds."

                else -> null
            }
        }

        private fun settle() {
            if (enough.isCompleted) return
            val ready = config != null && synchronized(packets) {
                packets.size >= MINIMUM_PACKETS && packets.any { it.keyFrame }
            }
            if (ready) enough.complete(Unit)
        }
    }

    /** The pattern, drawn by a View so the frames come from the same renderer a second screen uses. */
    private class PatternPresentation(context: Context, display: Display) : Presentation(context, display) {
        override fun onCreate(savedInstanceState: Bundle?) {
            super.onCreate(savedInstanceState)
            window?.setLayout(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
            setContentView(PatternView(context))
        }
    }

    /**
     * Four quadrants and a marker.
     *
     * The marker walks along the horizontal midline and is what keeps frames coming: a surface whose
     * content never changes produces one frame and then nothing, and a key frame plus a run of
     * frames is what the decoder needs. It never crosses a sample point.
     */
    private class PatternView(context: Context) : View(context) {
        private val paint = Paint()
        private var tick = 0

        override fun onDraw(canvas: Canvas) {
            val w = width.toFloat()
            val h = height.toFloat()
            Quadrant.entries.forEach { quadrant ->
                val colour = TestPattern.colours.getValue(quadrant)
                paint.color = Color.rgb(colour.red, colour.green, colour.blue)
                val left = if (quadrant == Quadrant.TOP_LEFT || quadrant == Quadrant.BOTTOM_LEFT) 0f else w / 2
                val top = if (quadrant == Quadrant.TOP_LEFT || quadrant == Quadrant.TOP_RIGHT) 0f else h / 2
                canvas.drawRect(left, top, left + w / 2, top + h / 2, paint)
            }
            paint.color = Color.BLACK
            val x = ((tick * MARKER_STEP) % (width - MARKER_SIZE)).toFloat()
            canvas.drawRect(x, h / 2 - MARKER_SIZE / 2, x + MARKER_SIZE, h / 2 + MARKER_SIZE / 2, paint)
            tick++
            postInvalidateOnAnimation()
        }
    }

    private const val DISPLAY_NAME = "flint-encoder-check"

    /** Small enough to encode and decode in well under a second; large enough for any encoder's floor. */
    private const val WIDTH = 640
    private const val HEIGHT = 360
    private const val BITRATE = 4_000_000
    private const val MINIMUM_PACKETS = 6
    private const val MAXIMUM_PACKETS = 30
    private const val READER_IMAGES = 3
    private const val CAPTURE_TIMEOUT_MILLIS = 4_000L
    private const val DECODE_TIMEOUT_MILLIS = 4_000L
    private const val FEED_TIMEOUT_MILLIS = 4_000L
    private const val DEQUEUE_TIMEOUT_MICROS = 10_000L
    private const val MARKER_SIZE = 8
    private const val MARKER_STEP = 4
}
