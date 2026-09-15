package com.rextechnologies.flint.mobile.media

import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioPlaybackCaptureConfiguration
import android.media.AudioRecord
import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.media.projection.MediaProjection
import android.os.Build
import androidx.annotation.RequiresApi
import com.rextechnologies.flint.castcore.media.AudioClock
import com.rextechnologies.flint.castcore.media.AudioLevel
import com.rextechnologies.flint.castcore.media.AudioState
import com.rextechnologies.flint.protocol.BinaryData
import com.rextechnologies.flint.protocol.wire.AudioConfigMessage
import com.rextechnologies.flint.protocol.wire.CodecId
import java.util.concurrent.atomic.AtomicBoolean

/** Where encoded audio goes, and where the truth about it is reported. */
interface EncodedAudioSink {
    /** Once, before any packet, with the decoder configuration. */
    fun onAudioConfig(message: AudioConfigMessage)

    /** One AAC frame. [data] is a scratch array owned by the capture; copy it to keep it. */
    fun onAudioPacket(presentationTimeUs: Long, data: ByteArray, offset: Int, length: Int)

    fun onAudioState(state: AudioState)
}

/**
 * Captures what other apps are playing and encodes it as AAC-LC, on Android 10 and above.
 *
 * `AudioPlaybackCaptureConfiguration` is the only way an ordinary app hears another app's output,
 * and it hears only from apps that allow it: an app that opts out contributes silence, which is why
 * the capture measures its own level and reports [AudioState.Capturing] until something audible has
 * come through. That measurement is the difference between "no sound because the app opted out"
 * and "no sound because Flint is broken", and the strip says which.
 *
 * Timestamps are frames counted from an origin read from the same monotonic clock the video is
 * stamped by, so the receiver can line the two up. Nothing here allocates per packet after the first:
 * one PCM buffer, one scratch array for the encoder's output, one reused `BufferInfo`.
 */
@RequiresApi(Build.VERSION_CODES.Q)
class AudioCapture(
    private val projection: MediaProjection,
    private val sink: EncodedAudioSink,
    private val originMicros: Long,
) {
    private val lifecycle = Any()
    private val running = AtomicBoolean()
    private var record: AudioRecord? = null
    private var codec: MediaCodec? = null
    private var thread: Thread? = null

    /** Starts capture and encoding. Every failure is reported through the sink and answered `false`. */
    fun start(): Boolean = synchronized(lifecycle) {
        check(record == null) { "This capture has already been started" }

        val configuration = AudioPlaybackCaptureConfiguration.Builder(projection)
            .addMatchingUsage(AudioAttributes.USAGE_MEDIA)
            .addMatchingUsage(AudioAttributes.USAGE_GAME)
            .addMatchingUsage(AudioAttributes.USAGE_UNKNOWN)
            .build()
        val format = AudioFormat.Builder()
            .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
            .setSampleRate(SAMPLE_RATE_HZ)
            .setChannelMask(AudioFormat.CHANNEL_IN_STEREO)
            .build()
        val minimum = AudioRecord.getMinBufferSize(
            SAMPLE_RATE_HZ,
            AudioFormat.CHANNEL_IN_STEREO,
            AudioFormat.ENCODING_PCM_16BIT,
        ).coerceAtLeast(READ_BYTES)

        val recorder = try {
            AudioRecord.Builder()
                .setAudioFormat(format)
                .setAudioPlaybackCaptureConfig(configuration)
                .setBufferSizeInBytes(minimum * RECORD_BUFFER_MULTIPLE)
                .build()
        } catch (failure: SecurityException) {
            sink.onAudioState(AudioState.PermissionDenied)
            return false
        } catch (failure: Throwable) {
            sink.onAudioState(AudioState.Failed(reason(failure)))
            return false
        }
        if (recorder.state != AudioRecord.STATE_INITIALIZED) {
            runCatching { recorder.release() }
            sink.onAudioState(AudioState.Failed("the recorder did not initialise"))
            return false
        }

        val encoder = try {
            MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC).apply {
                val encoding = MediaFormat.createAudioFormat(MediaFormat.MIMETYPE_AUDIO_AAC, SAMPLE_RATE_HZ, CHANNELS)
                encoding.setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC)
                encoding.setInteger(MediaFormat.KEY_BIT_RATE, BITRATE)
                encoding.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, READ_BYTES)
                configure(encoding, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
                start()
            }
        } catch (failure: Throwable) {
            runCatching { recorder.release() }
            sink.onAudioState(AudioState.Failed(reason(failure)))
            return false
        }

        record = recorder
        codec = encoder
        running.set(true)
        recorder.startRecording()
        sink.onAudioState(AudioState.Capturing)
        thread = Thread({ loop(recorder, encoder) }, "flint-audio").apply {
            priority = Thread.MAX_PRIORITY - 1
            start()
        }
        true
    }

    private fun loop(recorder: AudioRecord, encoder: MediaCodec) {
        val pcm = ByteArray(READ_BYTES)
        val scratch = ByteArray(SCRATCH_BYTES)
        val info = MediaCodec.BufferInfo()
        val clock = AudioClock(SAMPLE_RATE_HZ, originMicros)
        var frames = 0L
        var heard = false
        var reads = 0
        try {
            while (running.get()) {
                val read = recorder.read(pcm, 0, pcm.size, AudioRecord.READ_BLOCKING)
                if (read < 0) {
                    sink.onAudioState(AudioState.Failed("the recorder answered $read"))
                    return
                }
                if (read == 0) continue
                // Measured every few reads rather than every one: the arithmetic is cheap, but it
                // is on the capture thread and one answer per few hundred milliseconds is enough.
                if (!heard && ++reads % LEVEL_EVERY_READS == 0 && AudioLevel.isAudible(AudioLevel.rms(pcm, 0, read))) {
                    heard = true
                    sink.onAudioState(AudioState.Sounding)
                }
                var offset = 0
                while (offset < read && running.get()) {
                    val index = encoder.dequeueInputBuffer(DEQUEUE_TIMEOUT_MICROS)
                    if (index < 0) {
                        drain(encoder, info, scratch)
                        continue
                    }
                    val buffer = encoder.getInputBuffer(index)
                    if (buffer == null) {
                        encoder.queueInputBuffer(index, 0, 0, 0, 0)
                        continue
                    }
                    buffer.clear()
                    val length = minOf(buffer.capacity(), read - offset)
                    buffer.put(pcm, offset, length)
                    val presentation = clock.presentationTimeMicros(frames + offset / BYTES_PER_FRAME)
                    encoder.queueInputBuffer(index, 0, length, presentation, 0)
                    offset += length
                }
                frames += read / BYTES_PER_FRAME
                drain(encoder, info, scratch)
            }
        } catch (failure: IllegalStateException) {
            if (running.get()) sink.onAudioState(AudioState.Failed(reason(failure)))
        }
    }

    private fun drain(encoder: MediaCodec, info: MediaCodec.BufferInfo, scratch: ByteArray) {
        while (true) {
            val index = encoder.dequeueOutputBuffer(info, 0)
            when {
                index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED -> {
                    val csd = runCatching { encoder.outputFormat.getByteBuffer("csd-0") }.getOrNull()
                    val specific = csd?.let { source ->
                        val bytes = ByteArray(source.remaining())
                        source.duplicate().get(bytes)
                        BinaryData.of(bytes)
                    } ?: BinaryData.EMPTY
                    sink.onAudioConfig(AudioConfigMessage(CodecId.AAC_LC, SAMPLE_RATE_HZ, CHANNELS, specific))
                }

                index < 0 -> return

                else -> {
                    val buffer = encoder.getOutputBuffer(index)
                    val isConfig = info.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0
                    if (buffer != null && info.size > 0 && !isConfig && info.size <= scratch.size) {
                        buffer.position(info.offset)
                        buffer.limit(info.offset + info.size)
                        buffer.get(scratch, 0, info.size)
                        sink.onAudioPacket(info.presentationTimeUs.coerceAtLeast(0), scratch, 0, info.size)
                    }
                    encoder.releaseOutputBuffer(index, false)
                }
            }
        }
    }

    /** Stops, in the one order that is safe: the thread first, then the recorder, then the codec. */
    fun stop() = synchronized(lifecycle) {
        if (!running.compareAndSet(true, false)) return@synchronized
        val worker = thread
        thread = null
        worker?.join(JOIN_MILLIS)
        if (worker != null && worker.isAlive) {
            // Freed underneath a thread that is inside it, a codec crashes the process. Leaked, it
            // costs one encoder until the process ends. The second is the recoverable one.
            sink.onAudioState(AudioState.Failed("the audio thread did not stop in time"))
            return@synchronized
        }
        runCatching { record?.stop() }
        runCatching { record?.release() }
        runCatching { codec?.stop() }
        runCatching { codec?.release() }
        record = null
        codec = null
    }

    private fun reason(failure: Throwable): String =
        failure.message.orEmpty().ifBlank { failure.javaClass.simpleName }

    private companion object {
        const val SAMPLE_RATE_HZ = 48_000
        const val CHANNELS = 2
        const val BYTES_PER_FRAME = CHANNELS * 2
        const val BITRATE = 128_000

        /** 1024 frames of stereo 16-bit, which is one AAC frame's worth. */
        const val READ_BYTES = 1_024 * BYTES_PER_FRAME
        const val RECORD_BUFFER_MULTIPLE = 4
        const val SCRATCH_BYTES = 16 * 1_024
        const val LEVEL_EVERY_READS = 4
        const val DEQUEUE_TIMEOUT_MICROS = 10_000L
        const val JOIN_MILLIS = 2_000L
    }
}
