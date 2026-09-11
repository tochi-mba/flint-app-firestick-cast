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
 *
 * ## Threads
 *
 * Three touch this object. [start] and [stop] come from the session; [requestKeyFrame] and
 * [setBitrate] come from the control-socket reader, which is a different one; and everything inside
 * [drain] is the encoder thread this class starts. Only the last of those may touch the codec's
 * buffers, and none of the others may be running while the codec is released — a `MediaCodec` freed
 * underneath a thread that is inside one of its methods is a native use-after-free, which is not an
 * exception anything can catch. [lifecycle] is what makes that true rather than likely.
 */
class ScreenEncoder(
    private val config: EncoderConfig,
    private val sink: EncodedVideoSink,
    private val governor: KeyFrameGovernor = KeyFrameGovernor(),
) {
    /**
     * Guards the codec's lifetime against the control calls that reach it from other threads.
     *
     * Never taken by the encoder thread. That is not an optimisation: [stop] holds this lock while
     * it joins that thread, so an encoder thread that waited for it would be waiting on a thread
     * that is waiting on it.
     */
    private val lifecycle = Any()

    @Volatile
    private var codec: MediaCodec? = null

    private val running = AtomicBoolean()

    /** Reused for every frame. A fresh one per frame is an allocation per frame. */
    private val bufferInfo = MediaCodec.BufferInfo()

    private var thread: Thread? = null

    /** What the display or the projection draws into. Valid between [start] and [stop]. */
    @Volatile
    var inputSurface: Surface? = null
        private set

    fun start() = synchronized(lifecycle) {
        check(codec == null) { "This encoder has already been started" }
        val mime = mimeFor(config.codec)
        val format = MediaFormat.createVideoFormat(mime, config.width, config.height).apply {
            setInteger(
                MediaFormat.KEY_COLOR_FORMAT,
                MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface,
            )
            setInteger(MediaFormat.KEY_BIT_RATE, config.bitrateBitsPerSecond)
            setInteger(MediaFormat.KEY_FRAME_RATE, config.frameRate)

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                setInteger(MediaFormat.KEY_MAX_B_FRAMES, 0)
            }
            setInteger(MediaFormat.KEY_PROFILE, profileFor(mime))
            // A level is required wherever a profile is given. Several vendor encoders reject a
            // format that names one without the other, and a level is a ceiling rather than a
            // target, so the one named is the highest this encoder will ever ask for: EncoderPolicy
            // caps the long edge at 1920 and the rate at 60, which is exactly AVC 4.2 and HEVC Main
            // tier 4.1.
            setInteger(MediaFormat.KEY_LEVEL, levelFor(mime))

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
        try {
            created.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
            inputSurface = created.createInputSurface()
            created.start()
        } catch (failure: Throwable) {
            // Without this the codec is leaked and `codec` stays null, so start() is permanently
            // un-retryable and the device holds an encoder nobody can reach.
            runCatching { created.release() }
            runCatching { inputSurface?.release() }
            inputSurface = null
            throw failure
        }
        codec = created
        running.set(true)

        thread = Thread({ drain(created) }, "flint-encoder").apply {
            // Above a default thread and below the audio threads. The encoder missing its deadline
            // is a visible stutter; nothing else on this process is more urgent.
            priority = Thread.MAX_PRIORITY - 1
            start()
        }
    }

    /**
     * The profile, which below API 29 is the only thing standing between this and a B-frame.
     *
     * `KEY_MAX_B_FRAMES` arrived in API 29. Below it the profile is the only lever, and Main is not
     * that lever: Main permits B-frames and every H.264 profile above it does too. Constrained
     * Baseline is the one that forbids them in the bitstream syntax, so that is what an old device
     * gets — at the cost of CABAC, which is a real quality loss and the price of the rule in
     * AGENTS.md being absolute rather than a preference.
     *
     * HEVC has no B-frame-free profile at all. Below API 29 there is therefore no structural
     * guarantee for it, only the encoder's default, which is why H.264 is what the phone advertises
     * first.
     */
    private fun profileFor(mime: String): Int = when {
        mime == MediaFormat.MIMETYPE_VIDEO_HEVC -> MediaCodecInfo.CodecProfileLevel.HEVCProfileMain
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q -> MediaCodecInfo.CodecProfileLevel.AVCProfileMain
        else -> MediaCodecInfo.CodecProfileLevel.AVCProfileConstrainedBaseline
    }

    private fun levelFor(mime: String): Int =
        if (mime == MediaFormat.MIMETYPE_VIDEO_HEVC) {
            MediaCodecInfo.CodecProfileLevel.HEVCMainTierLevel41
        } else {
            MediaCodecInfo.CodecProfileLevel.AVCLevel42
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
    fun requestKeyFrame() = synchronized(lifecycle) {
        val active = codec ?: return@synchronized
        requestSyncFrameOn(active)
    }

    /** Changes the bitrate without reconfiguring, which would drop every queued frame. */
    fun setBitrate(bitsPerSecond: Int) = synchronized(lifecycle) {
        val active = codec ?: return@synchronized
        setParameterOn(active, MediaCodec.PARAMETER_KEY_VIDEO_BITRATE, bitsPerSecond)
    }

    /**
     * The unlocked form, for the encoder thread.
     *
     * It is safe there without the lock, and must be without it: [stop] holds the lock while it
     * joins this thread, so a thread that took the lock on its way out would wait on a lock the
     * stopping thread will not release until this one has finished. The reference it is given stays
     * valid for as long as it runs, because that is exactly what [stop] guarantees by joining before
     * it releases anything.
     */
    private fun requestSyncFrameOn(active: MediaCodec) {
        governor.onSyncFrameRequested()
        setParameterOn(active, MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME, 0)
    }

    private fun setParameterOn(active: MediaCodec, key: String, value: Int) {
        runCatching {
            active.setParameters(android.os.Bundle().apply { putInt(key, value) })
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
                index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> publishConfig(active, active.outputFormat)
                index < 0 -> Unit
                else -> {
                    val buffer = active.getOutputBuffer(index)
                    if (buffer != null) emit(buffer)
                    active.releaseOutputBuffer(index, false)
                }
            }
            // The encoder is finished. Leaving the loop here rather than on the flag is what makes
            // signalEndOfInputStream mean something: the tail reaches the sink first.
            if (bufferInfo.flags and MediaCodec.BUFFER_FLAG_END_OF_STREAM != 0) return
        }
    }

    private fun emit(buffer: ByteBuffer) {
        if (bufferInfo.size <= 0) return

        // The codec-config buffer is the parameter sets, which were already sent as VIDEO_CONFIG.
        // Sending them again as a frame would have the receiver try to decode a parameter set.
        if (bufferInfo.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0) return

        val end = bufferInfo.offset.toLong() + bufferInfo.size
        if (bufferInfo.offset < 0 || end > buffer.capacity()) return

        val keyFrame = bufferInfo.flags and MediaCodec.BUFFER_FLAG_KEY_FRAME != 0
        val strategy = governor.onFrameEmitted(keyFrame)
        if (strategy is KeyFrameStrategy.BoundedInterval && !fallbackReported) {
            fallbackReported = true
            sink.onKeyFrameStrategyChanged(strategy)
        }

        // Cleared first, then limit, then position. Setting the position before the limit throws
        // whenever the buffer arrives with a limit below the new offset, which is exactly what a
        // codec that reuses one large buffer for frames of different sizes hands back.
        buffer.clear()
        buffer.limit(end.toInt())
        buffer.position(bufferInfo.offset)
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

    private fun publishConfig(active: MediaCodec, format: MediaFormat) {
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
        requestSyncFrameOn(active)
    }

    private fun MediaFormat.tryInt(key: String, fallback: Int): Int =
        runCatching { getInteger(key) }.getOrDefault(fallback)

    /**
     * Stops, in the one order that is safe.
     *
     * The end of the input stream is signalled while the encoder thread is still draining, so the
     * tail actually reaches the sink; the thread is then joined; and only once it has genuinely
     * exited is anything released. Releasing a `MediaCodec` while that thread is inside
     * `getOutputBuffer` or `sink.onVideoPacket` frees memory it is reading, which crashes the
     * process rather than throwing.
     *
     * If the thread does not exit — the sink is blocked on a socket that has stopped draining, say —
     * the codec is deliberately left unreleased and the sink is told. One leaked encoder is
     * recoverable; a native crash in a background service is not.
     */
    fun stop() = synchronized(lifecycle) {
        if (!running.compareAndSet(true, false)) return@synchronized
        val active = codec
        val encoderThread = thread
        thread = null

        runCatching { active?.signalEndOfInputStream() }
        encoderThread?.join(THREAD_JOIN_MILLIS)
        if (encoderThread != null && encoderThread.isAlive) {
            sink.onEncoderFailed(
                "The encoder thread did not stop within ${THREAD_JOIN_MILLIS}ms, so its codec has " +
                    "been left alone rather than freed underneath it",
            )
            return@synchronized
        }

        runCatching { active?.stop() }
        runCatching { active?.release() }
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

        /**
         * Generous on purpose.
         *
         * The drain loop checks whether it has been asked to stop every DEQUEUE_TIMEOUT_MICROS, so
         * it normally exits in about ten milliseconds. This is the budget for the abnormal case —
         * one last frame in the sink, writing to a socket — and overrunning it means declining to
         * release the codec, so it is worth more than a couple of frames.
         */
        const val THREAD_JOIN_MILLIS = 2_000L
    }
}
