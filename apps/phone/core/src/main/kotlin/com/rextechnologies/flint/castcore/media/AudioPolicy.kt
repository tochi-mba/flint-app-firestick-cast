package com.rextechnologies.flint.castcore.media

import com.rextechnologies.flint.castcore.copy.AudioCopy
import kotlin.math.sqrt

/**
 * Where a mirror's sound has got to.
 *
 * Six states because there are six different truths, and the strip has to name the one that holds:
 * a mirror can be silent because the platform cannot capture, because the person said no, because
 * the app playing has opted out, because the encoder died, or because nothing is playing. Only one
 * of those is a fault in Flint.
 */
sealed interface AudioState {
    /** No sound was attempted. A second screen, or a mirror before capture has started. */
    data object Off : AudioState

    /** Playback capture arrived in Android 10. */
    data object PlatformTooOld : AudioState

    /** The record permission, which is what Android puts playback capture behind, was refused. */
    data object PermissionDenied : AudioState

    /** Capture is running and nothing audible has come through yet. */
    data object Capturing : AudioState

    /** Capture is running and sound has been heard on it. */
    data object Sounding : AudioState

    data class Failed(val detail: String) : AudioState
}

object AudioPolicy {
    /** `AudioPlaybackCaptureConfiguration` exists from API 29. */
    const val MINIMUM_API: Int = 29

    fun word(state: AudioState): String = when (state) {
        AudioState.Off -> AudioCopy.OFF
        AudioState.PlatformTooOld -> AudioCopy.PLATFORM_TOO_OLD
        AudioState.PermissionDenied -> AudioCopy.PERMISSION_DENIED
        AudioState.Capturing -> AudioCopy.CAPTURING
        AudioState.Sounding -> AudioCopy.SOUNDING
        is AudioState.Failed -> AudioCopy.FAILED
    }

    /** The sentence under the row, or `null` when the word says everything. */
    fun sentence(state: AudioState, secondScreen: Boolean): String? = when (state) {
        AudioState.Off -> if (secondScreen) AudioCopy.SECOND_SCREEN_SILENT else null
        AudioState.PlatformTooOld -> AudioCopy.PLATFORM_TOO_OLD_SENTENCE
        AudioState.PermissionDenied -> AudioCopy.PERMISSION_DENIED_SENTENCE
        AudioState.Capturing -> AudioCopy.CAPTURING_SENTENCE
        AudioState.Sounding -> null
        is AudioState.Failed -> AudioCopy.failedSentence(state.detail)
    }
}

/**
 * How loud a run of 16-bit little-endian PCM is, as a fraction of full scale.
 *
 * The one measurement that tells "capturing silence" from "capturing sound", so the strip can say
 * that an app has opted out rather than that Flint is broken. Root mean square rather than peak,
 * because a single stray sample is not sound.
 */
object AudioLevel {
    /** Quiet room noise sits well below this; anything a person would call audio sits well above. */
    const val AUDIBLE_RMS: Double = 0.002

    fun rms(bytes: ByteArray, offset: Int, length: Int): Double {
        require(offset >= 0 && length >= 0 && offset + length <= bytes.size)
        val samples = length / 2
        if (samples == 0) return 0.0
        var sum = 0.0
        var at = offset
        repeat(samples) {
            val low = bytes[at].toInt() and 0xff
            val high = bytes[at + 1].toInt()
            val sample = ((high shl 8) or low) / FULL_SCALE
            sum += sample * sample
            at += 2
        }
        return sqrt(sum / samples)
    }

    fun isAudible(rms: Double): Boolean = rms >= AUDIBLE_RMS

    private const val FULL_SCALE = 32_768.0
}

/**
 * Presentation times for audio, on the same clock as the video.
 *
 * Video frames are stamped by the platform on the monotonic clock. Audio has no such stamp: it is a
 * stream of frames at a known rate, so its time is the origin plus the frames delivered so far. The
 * origin is read from that same monotonic clock when capture starts, which is what keeps a decoder
 * on the far side able to line the two up. Integer arithmetic in the order that cannot overflow for
 * any session a person will sit through.
 */
class AudioClock(private val sampleRateHz: Int, private val originMicros: Long) {
    init {
        require(sampleRateHz > 0)
        require(originMicros >= 0)
    }

    fun presentationTimeMicros(framesSoFar: Long): Long {
        require(framesSoFar >= 0)
        val seconds = framesSoFar / sampleRateHz
        val remainder = framesSoFar % sampleRateHz
        return originMicros + seconds * MICROS_PER_SECOND + remainder * MICROS_PER_SECOND / sampleRateHz
    }

    private companion object {
        const val MICROS_PER_SECOND = 1_000_000L
    }
}
