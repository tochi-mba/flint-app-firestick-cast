package com.rextechnologies.flint.receiver.media

import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioManager
import android.media.AudioTrack
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.os.Build
import android.os.Handler
import android.os.HandlerThread
import android.view.Surface
import com.rextechnologies.flint.protocol.wire.AudioConfigMessage
import com.rextechnologies.flint.protocol.wire.AudioPacket
import com.rextechnologies.flint.protocol.wire.CodecId
import com.rextechnologies.flint.protocol.wire.VideoConfigMessage
import com.rextechnologies.flint.protocol.wire.VideoPacket
import java.nio.ByteBuffer
import java.util.ArrayDeque
import java.util.concurrent.atomic.AtomicLong

data class DecoderStats(
    val queueDepth: Int,
    val decodeLatencyUs: Long,
    val droppedFrames: Long,
)

/** Whether attaching a Surface may retain frames received while Compose was creating it. */
internal fun preservePendingFramesOnSurfaceChange(
    hadSurface: Boolean,
    hadCodec: Boolean,
    hasSurface: Boolean,
): Boolean = !hadSurface && !hadCodec && hasSurface

/** Whether another non-blocking output poll is required after this decoder pass. */
internal fun videoDrainNeedsRetry(pendingFrames: Int, inputsOutstanding: Int): Boolean =
    pendingFrames > 0 || inputsOutstanding > 0

/**
 * Small, bounded MediaCodec pipeline for the receiver's mirror surface.
 *
 * All codec calls happen on one handler thread. When the three-frame jitter
 * queue overflows we discard through the next key frame instead of showing a
 * chain of corrupt dependent frames.
 */
class MirrorVideoDecoder(
    private val onFailure: (String) -> Unit,
    private val onFrameRendered: () -> Unit,
) : AutoCloseable {
    private data class QueuedFrame(val packet: VideoPacket, val receivedNanos: Long)

    private val thread = HandlerThread("rexcast-video-decoder").apply { start() }
    private val handler = Handler(thread.looper)
    private val pending = ArrayDeque<QueuedFrame>()
    private val dropped = AtomicLong()

    @Volatile private var queueDepth = 0

    @Volatile private var lastLatencyUs = 0L

    @Volatile private var closed = false
    private var codec: MediaCodec? = null
    private var configuration: VideoConfigMessage? = null
    private var surface: Surface? = null
    private var waitingForKeyFrame = true
    private var drainScheduled = false
    private var frameReported = false
    private var inputsOutstanding = 0

    fun attachSurface(value: Surface?) {
        if (closed) return
        handler.post {
            val previousSurface = surface
            val nextSurface = value?.takeIf(Surface::isValid)
            if (previousSurface === nextSurface) return@post

            surface = nextSurface
            // Surface creation is asynchronous with respect to the network. On initial attach the
            // first IDR may already be queued; throwing it away leaves an infinite-GOP stream black
            // forever. A replacement/detach is different: input already submitted to the old codec
            // cannot be recovered, so dependent frames must be discarded until another IDR.
            val initialAttach = preservePendingFramesOnSurfaceChange(
                hadSurface = previousSurface != null,
                hadCodec = codec != null,
                hasSurface = nextSurface != null,
            )
            reconfigure(preservePending = initialAttach)
        }
    }

    fun configure(value: VideoConfigMessage) {
        if (closed) return
        handler.post {
            configuration = value
            reconfigure(preservePending = false)
        }
    }

    fun queue(packet: VideoPacket) {
        if (closed) return
        handler.post {
            if (waitingForKeyFrame && !packet.keyFrame) {
                dropped.incrementAndGet()
                return@post
            }
            if (packet.keyFrame) waitingForKeyFrame = false
            if (pending.size >= MAX_QUEUE_DEPTH) {
                dropped.addAndGet(pending.size.toLong())
                pending.clear()
                waitingForKeyFrame = true
                if (!packet.keyFrame) {
                    dropped.incrementAndGet()
                    queueDepth = 0
                    return@post
                }
                waitingForKeyFrame = false
            }
            pending.addLast(QueuedFrame(packet, System.nanoTime()))
            queueDepth = pending.size
            scheduleDrain()
        }
    }

    fun stats(): DecoderStats = DecoderStats(queueDepth, lastLatencyUs, dropped.get())

    private fun reconfigure(preservePending: Boolean) {
        releaseCodec()
        if (!preservePending) pending.clear()
        queueDepth = pending.size
        waitingForKeyFrame = pending.firstOrNull()?.packet?.keyFrame != true
        frameReported = false
        val config = configuration ?: return
        val target = surface?.takeIf(Surface::isValid) ?: return
        try {
            val mime = when (config.codec) {
                CodecId.H264 -> MediaFormat.MIMETYPE_VIDEO_AVC
                CodecId.H265 -> MediaFormat.MIMETYPE_VIDEO_HEVC
                else -> error("Unsupported mirror video codec ${config.codec.value}")
            }
            val format = MediaFormat.createVideoFormat(mime, config.width, config.height)
            config.codecSpecificData.forEachIndexed { index, bytes ->
                format.setByteBuffer("csd-$index", ByteBuffer.wrap(bytes.toByteArray()))
            }
            format.setInteger(MediaFormat.KEY_PRIORITY, 0)
            val candidate = MediaCodec.createDecoderByType(mime)
            if (Build.VERSION.SDK_INT >= 30) {
                val supportsLowLatency = candidate.codecInfo
                    .getCapabilitiesForType(mime)
                    .isFeatureSupported(MediaCodecInfo.CodecCapabilities.FEATURE_LowLatency)
                if (supportsLowLatency) format.setInteger(MediaFormat.KEY_LOW_LATENCY, 1)
            }
            candidate.configure(format, target, null, 0)
            candidate.start()
            codec = candidate
            scheduleDrain()
        } catch (failure: Throwable) {
            releaseCodec()
            onFailure(failure.message ?: "The TV video decoder could not start")
        }
    }

    private fun scheduleDrain() {
        if (drainScheduled || codec == null || closed) return
        drainScheduled = true
        handler.post(drain)
    }

    private val drain = object : Runnable {
        override fun run() {
            drainScheduled = false
            val active = codec ?: return
            try {
                var fed = true
                while (fed && pending.isNotEmpty()) {
                    val index = active.dequeueInputBuffer(0)
                    fed = index >= 0
                    if (fed) {
                        val frame = pending.removeFirst()
                        val data = frame.packet.data.toByteArray()
                        active.getInputBuffer(index)?.apply {
                            clear()
                            put(data)
                        } ?: error("Video decoder returned no input buffer")
                        active.queueInputBuffer(index, 0, data.size, frame.packet.presentationTimeUs, 0)
                        inputsOutstanding += 1
                        lastLatencyUs = (System.nanoTime() - frame.receivedNanos) / 1_000
                    }
                }
                queueDepth = pending.size
                val info = MediaCodec.BufferInfo()
                while (true) {
                    val index = active.dequeueOutputBuffer(info, 0)
                    when {
                        index >= 0 -> {
                            active.releaseOutputBuffer(index, true)
                            if (inputsOutstanding > 0) inputsOutstanding -= 1
                            if (!frameReported) {
                                frameReported = true
                                onFrameRendered()
                            }
                        }
                        index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> continue
                        else -> break
                    }
                }
            } catch (failure: Throwable) {
                onFailure(failure.message ?: "Mirror decoding stopped")
                reconfigure(preservePending = false)
                return
            }
            // MediaCodec commonly accepts an input before its Surface output is ready. Continue
            // polling even after the input queue empties, otherwise a still desktop can strand its
            // one and only IDR inside the decoder until some unrelated later desktop change.
            if (videoDrainNeedsRetry(pending.size, inputsOutstanding)) {
                drainScheduled = true
                handler.postDelayed(this, DRAIN_RETRY_MILLIS)
            }
        }
    }

    private fun releaseCodec() {
        codec?.let { value ->
            runCatching { value.stop() }
            runCatching { value.release() }
        }
        codec = null
        inputsOutstanding = 0
        drainScheduled = false
        handler.removeCallbacks(drain)
    }

    override fun close() {
        if (closed) return
        closed = true
        handler.post {
            releaseCodec()
            pending.clear()
            thread.quitSafely()
        }
    }

    private companion object {
        const val MAX_QUEUE_DEPTH = 3
        const val DRAIN_RETRY_MILLIS = 3L
    }
}

/** Decodes mirror audio to an AudioTrack without routing or capture privileges. */
class MirrorAudioDecoder(
    private val onFailure: (String) -> Unit,
) : AutoCloseable {
    private data class QueuedAudio(val packet: AudioPacket, val receivedNanos: Long)

    private val thread = HandlerThread("rexcast-audio-decoder").apply { start() }
    private val handler = Handler(thread.looper)
    private val pending = ArrayDeque<QueuedAudio>()
    private val dropped = AtomicLong()

    @Volatile private var queueDepth = 0

    @Volatile private var lastLatencyUs = 0L

    @Volatile private var closed = false
    private var codec: MediaCodec? = null
    private var track: AudioTrack? = null
    private var drainScheduled = false

    fun configure(config: AudioConfigMessage) {
        if (closed) return
        handler.post {
            releaseCodec()
            pending.clear()
            queueDepth = 0
            try {
                val mime = when (config.codec) {
                    CodecId.AAC_LC -> MediaFormat.MIMETYPE_AUDIO_AAC
                    CodecId.OPUS -> MediaFormat.MIMETYPE_AUDIO_OPUS
                    else -> error("Unsupported mirror audio codec ${config.codec.value}")
                }
                val format = MediaFormat.createAudioFormat(mime, config.sampleRateHz, config.channelCount)
                if (!config.codecSpecificData.isEmpty) {
                    format.setByteBuffer("csd-0", ByteBuffer.wrap(config.codecSpecificData.toByteArray()))
                }
                MediaCodec.createDecoderByType(mime).also { candidate ->
                    candidate.configure(format, null, null, 0)
                    candidate.start()
                    codec = candidate
                }
            } catch (failure: Throwable) {
                releaseCodec()
                onFailure(failure.message ?: "The TV audio decoder could not start")
            }
        }
    }

    fun queue(packet: AudioPacket) {
        if (closed) return
        handler.post {
            if (pending.size >= MAX_QUEUE_DEPTH) {
                pending.removeFirst()
                dropped.incrementAndGet()
            }
            pending.addLast(QueuedAudio(packet, System.nanoTime()))
            queueDepth = pending.size
            scheduleDrain()
        }
    }

    fun stats(): DecoderStats = DecoderStats(queueDepth, lastLatencyUs, dropped.get())

    private fun scheduleDrain() {
        if (drainScheduled || codec == null || closed) return
        drainScheduled = true
        handler.post(drain)
    }

    private val drain = object : Runnable {
        override fun run() {
            drainScheduled = false
            val active = codec ?: return
            try {
                while (pending.isNotEmpty()) {
                    val index = active.dequeueInputBuffer(0)
                    if (index < 0) break
                    val frame = pending.removeFirst()
                    val data = frame.packet.data.toByteArray()
                    active.getInputBuffer(index)?.apply {
                        clear()
                        put(data)
                    } ?: error("Audio decoder returned no input buffer")
                    active.queueInputBuffer(index, 0, data.size, frame.packet.presentationTimeUs, 0)
                    lastLatencyUs = (System.nanoTime() - frame.receivedNanos) / 1_000
                }
                queueDepth = pending.size
                val info = MediaCodec.BufferInfo()
                while (true) {
                    val index = active.dequeueOutputBuffer(info, 0)
                    when {
                        index >= 0 -> {
                            val output = active.getOutputBuffer(index)
                            if (output != null && info.size > 0) {
                                val bytes = ByteArray(info.size)
                                output.position(info.offset)
                                output.limit(info.offset + info.size)
                                output.get(bytes)
                                track?.write(bytes, 0, bytes.size)
                            }
                            active.releaseOutputBuffer(index, false)
                        }
                        index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> replaceTrack(active.outputFormat)
                        else -> break
                    }
                }
            } catch (failure: Throwable) {
                onFailure(failure.message ?: "Mirror audio stopped")
                releaseCodec()
                return
            }
            if (pending.isNotEmpty()) {
                drainScheduled = true
                handler.postDelayed(this, DRAIN_RETRY_MILLIS)
            }
        }
    }

    private fun replaceTrack(format: MediaFormat) {
        track?.let {
            runCatching { it.stop() }
            it.release()
        }
        val sampleRate = format.getInteger(MediaFormat.KEY_SAMPLE_RATE)
        val channels = format.getInteger(MediaFormat.KEY_CHANNEL_COUNT)
        val mask = if (channels == 1) AudioFormat.CHANNEL_OUT_MONO else AudioFormat.CHANNEL_OUT_STEREO
        val minimum = AudioTrack.getMinBufferSize(sampleRate, mask, AudioFormat.ENCODING_PCM_16BIT)
            .coerceAtLeast(sampleRate * channels / 5)
        track = if (Build.VERSION.SDK_INT >= 23) {
            AudioTrack.Builder()
                .setAudioAttributes(
                    AudioAttributes.Builder()
                        .setUsage(AudioAttributes.USAGE_MEDIA)
                        .setContentType(AudioAttributes.CONTENT_TYPE_MOVIE)
                        .build(),
                )
                .setAudioFormat(
                    AudioFormat.Builder()
                        .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                        .setSampleRate(sampleRate)
                        .setChannelMask(mask)
                        .build(),
                )
                .setTransferMode(AudioTrack.MODE_STREAM)
                .setBufferSizeInBytes(minimum)
                .build()
        } else {
            @Suppress("DEPRECATION")
            AudioTrack(
                AudioManager.STREAM_MUSIC,
                sampleRate,
                mask,
                AudioFormat.ENCODING_PCM_16BIT,
                minimum,
                AudioTrack.MODE_STREAM,
            )
        }.also(AudioTrack::play)
    }

    private fun releaseCodec() {
        codec?.let {
            runCatching { it.stop() }
            runCatching { it.release() }
        }
        codec = null
        track?.let {
            runCatching { it.stop() }
            runCatching { it.release() }
        }
        track = null
        drainScheduled = false
        handler.removeCallbacks(drain)
    }

    override fun close() {
        if (closed) return
        closed = true
        handler.post {
            releaseCodec()
            pending.clear()
            thread.quitSafely()
        }
    }

    private companion object {
        const val MAX_QUEUE_DEPTH = 12
        const val DRAIN_RETRY_MILLIS = 3L
    }
}
