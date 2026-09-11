package com.rextechnologies.flint.mobile.media

import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.os.Build
import android.view.Surface
import com.rextechnologies.flint.castcore.media.EncoderConfig
import com.rextechnologies.flint.castcore.media.KeyFrameGovernor
import com.rextechnologies.flint.castcore.media.KeyFrameStrategy
import com.rextechnologies.flint.protocol.BinaryData
import com.rextechnologies.flint.protocol.wire.CodecId
import com.rextechnologies.flint.protocol.wire.VideoConfigMessage
import java.nio.ByteBuffer
import java.util.concurrent.atomic.AtomicBoolean

/** Where encoded frames go. Implemented by the session; kept an interface so the encoder is testable. */
interface EncodedVideoSink {
    /** Called once, before any frame, with the parameter sets the decoder needs to start. */
    fun onVideoConfig(message: VideoConfigMessage)

    /**
     * One encoded frame.
     *
     * The buffer belongs to the encoder and is valid only for the duration of this call. An
     * implementation that needs to keep it must copy it, and must know that doing so puts an
     * allocation on the frame path.
     */
    fun onVideoPacket(presentationTimeUs: Long, keyFrame: Boolean, data: ByteBuffer)

    /**
     * This device has been observed ignoring a sync-frame request.
     *
     * The only way to apply a bounded key-frame interval is to configure the codec with it, so the
     * session has to stop this encoder and start another -- and then send a fresh VIDEO_CONFIG,
     * because the new codec will produce its own parameter sets. It is an expensive thing to do and
     * it happens at most once per session, which is why it is reported rather than attempted here.
     */
    fun onKeyFrameStrategyChanged(strategy: KeyFrameStrategy.BoundedInterval)

    fun onEncoderFailed(detail: String)
}

/**
 * The capture-encode loop.
 *
 * It owns a `MediaCodec` in surface-input mode: whatever draws into [inputSurface] is what gets
 * encoded, with no copy through application memory at any point. Both modes feed it the same way —
 * the second screen through a `VirtualDisplay`, the mirror through a `MediaProjection` — which is why
 * slice 06 proving this path against the simpler surface was worth doing first.
 *
 * Nothing in the steady state allocates. One `BufferInfo` is reused for every frame, the output
 * buffer is passed straight through to the sink, and the sink writes it to the socket through
 * `FrameWriter`, which borrows rather than copies. The rule exists because a collection pause between
 * two frames is a stutter on the television, and it arrives exactly when the encoder is busiest.
 */
class ScreenEncoder(
    private val config: EncoderConfig,
    private val sink: EncodedVideoSink,
    private val governor: KeyFrameGovernor = KeyFrameGovernor(),
) {
    private var codec: MediaCodec? = null
    private val running = AtomicBoolean()

    /** Reused for every frame. A fresh one per frame is an allocation per frame. */
    private val bufferInfo = MediaCodec.BufferInfo()

    private var thread: Thread? = null

    /** What the display or the projection draws into. Valid between [start] and [stop]. */
    var inputSurface: Surface? = null
        private set

    fun start() {
        check(codec == null) { "This encoder has already been started" }
        val mime = mimeFor(config.codec)
        val format = MediaFormat.createVideoFormat(mime, config.width, config.height).apply {
            setInteger(
                MediaFormat.KEY_COLOR_FORMAT,
                MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface,
            )
            setInteger(MediaFormat.KEY_BIT_RATE, config.bitrateBitsPerSecond)
            setInteger(MediaFormat.KEY_FRAME_RATE, config.frameRate)

            // No B-frames, ever: one cannot be emitted until a later frame has been encoded, which is
            // latency by construction. The key exists only from API 29; below that the Baseline and
            // Main profiles set below are what rule them out, which is why the profile is not left
            // to the encoder's default.
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                setInteger(MediaFormat.KEY_MAX_B_FRAMES, 0)
            }
            setInteger(
                MediaFormat.KEY_PROFILE,
                if (mime == MediaFormat.MIMETYPE_VIDEO_HEVC) {
                    MediaCodecInfo.CodecProfileLevel.HEVCProfileMain
                } else {
                    MediaCodecInfo.CodecProfileLevel.AVCProfileMain
                },
            )

            applyKeyFrameStrategy(this, governor.strategy)

            // Ask the encoder to hold as little as it can. KEY_LATENCY is honoured widely;
            // KEY_LOW_LATENCY exists only from API 30 and only where the codec advertises the
            // feature, so it is asked for rather than assumed.
            setInteger(MediaFormat.KEY_LATENCY, 1)
            setInteger(MediaFormat.KEY_PRIORITY, 0)
            if (config.lowLatencyRequested && Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
            }
        }

        val created = MediaCodec.createEncoderByType(mime)
        created.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
        inputSurface = created.createInputSurface()
        created.start()
        codec = created
        running.set(true)

        thread = Thread({ drain(created) }, "flint-encoder").apply {
            // Above a default thread and below the audio threads. The encoder missing its deadline
            // is a visible stutter; nothing else on this process is more urgent.
            priority = Thread.MAX_PRIORITY - 1
            start()
        }
    }

    private fun applyKeyFrameStrategy(format: MediaFormat, strategy: KeyFrameStrategy) {
        when (strategy) {
            is KeyFrameStrategy.OnDemand ->
                // Effectively infinite. An intra frame is a bitrate spike, and one nobody asked for
                // buys nothing on a link already carrying the stream.
                format.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, INFINITE_GOP_SECONDS)

            is KeyFrameStrategy.BoundedInterval ->
                format.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, strategy.seconds)
        }
    }

    /**
     * Asks for an intra frame.
     *
     * Called when the receiver reports enough loss to be unable to recover, and always immediately
     * after a `VIDEO_CONFIG`: that message is a hard decoder reset on the receiver, which discards
     * its whole pending queue and re-arms its key-frame gate, so the next packet has to be one it can
     * start from or the picture freezes.
     */
    fun requestKeyFrame() {
        val active = codec ?: return
        governor.onSyncFrameRequested()
        runCatching {
            active.setParameters(
                android.os.Bundle().apply {
                    putInt(MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME, 0)
                },
            )
        }
    }

    /** Changes the bitrate without reconfiguring, which would drop every queued frame. */
    fun setBitrate(bitsPerSecond: Int) {
        val active = codec ?: return
        runCatching {
            active.setParameters(
                android.os.Bundle().apply {
                    putInt(MediaCodec.PARAMETER_KEY_VIDEO_BITRATE, bitsPerSecond)
                },
            )
        }
    }

    private fun drain(active: MediaCodec) {
        while (running.get()) {
            val index = try {
                active.dequeueOutputBuffer(bufferInfo, DEQUEUE_TIMEOUT_MICROS)
            } catch (failure: IllegalStateException) {
                if (running.get()) sink.onEncoderFailed(failure.message.orEmpty())
                return
            }

            when {
                index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> publishConfig(active.outputFormat)
                index < 0 -> Unit
                else -> {
                    val buffer = active.getOutputBuffer(index)
                    if (buffer != null) emit(buffer)
                    active.releaseOutputBuffer(index, false)
                }
            }
        }
    }

    private fun emit(buffer: ByteBuffer) {
        if (bufferInfo.size <= 0) return

        // The codec-config buffer is the parameter sets, which were already sent as VIDEO_CONFIG.
        // Sending them again as a frame would have the receiver try to decode a parameter set.
        if (bufferInfo.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0) return

        val keyFrame = bufferInfo.flags and MediaCodec.BUFFER_FLAG_KEY_FRAME != 0
        val strategy = governor.onFrameEmitted(keyFrame)
        if (strategy is KeyFrameStrategy.BoundedInterval && !fallbackReported) {
            fallbackReported = true
            sink.onKeyFrameStrategyChanged(strategy)
        }

        buffer.position(bufferInfo.offset)
        buffer.limit(bufferInfo.offset + bufferInfo.size)
        sink.onVideoPacket(bufferInfo.presentationTimeUs.coerceAtLeast(0), keyFrame, buffer)
    }

    /**
     * Reported once per session.
     *
     * There is no parameter that changes a running codec's key-frame interval, so the fallback is a
     * restart rather than a setting. An unbounded group of pictures on a device that ignores the
     * request means a receiver which joins late or drops a packet never gets a frame it can start
     * from — a frozen picture, indefinitely. The Windows hardware path carries the same fallback for
     * the same reason, and says so in the same place.
     */
    private var fallbackReported = false

    private fun publishConfig(format: MediaFormat) {
        val codecSpecificData = buildList {
            listOf("csd-0", "csd-1").forEach { key ->
                val buffer = runCatching { format.getByteBuffer(key) }.getOrNull() ?: return@forEach
                val bytes = ByteArray(buffer.remaining())
                buffer.duplicate().get(bytes)
                add(BinaryData.of(bytes))
            }
        }
        sink.onVideoConfig(
            VideoConfigMessage(
                codec = config.codec,
                width = format.tryInt(MediaFormat.KEY_WIDTH, config.width),
                height = format.tryInt(MediaFormat.KEY_HEIGHT, config.height),
                codecSpecificData = codecSpecificData,
            ),
        )
        requestKeyFrame()
    }

    private fun MediaFormat.tryInt(key: String, fallback: Int): Int =
        runCatching { getInteger(key) }.getOrDefault(fallback)

    fun stop() {
        if (!running.compareAndSet(true, false)) return
        thread?.join(THREAD_JOIN_MILLIS)
        thread = null
        runCatching { codec?.signalEndOfInputStream() }
        runCatching { codec?.stop() }
        runCatching { codec?.release() }
        runCatching { inputSurface?.release() }
        codec = null
        inputSurface = null
    }

    private fun mimeFor(codec: CodecId): String = when (codec) {
        CodecId.H265 -> MediaFormat.MIMETYPE_VIDEO_HEVC
        CodecId.H264 -> MediaFormat.MIMETYPE_VIDEO_AVC
        else -> error("Unsupported mirror video codec: ${codec.value}")
    }

    private companion object {
        /**
         * Ten hours. `KEY_I_FRAME_INTERVAL` has no "never" value, and a number this large is
         * indistinguishable from one for any session a person will actually run.
         */
        const val INFINITE_GOP_SECONDS = 36_000

        /** Long enough not to spin, short enough that stop() is not waiting on it. */
        const val DEQUEUE_TIMEOUT_MICROS = 10_000L

        const val THREAD_JOIN_MILLIS = 500L
    }
}
