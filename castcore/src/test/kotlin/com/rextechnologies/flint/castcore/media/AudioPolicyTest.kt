package com.rextechnologies.flint.castcore.media

import com.rextechnologies.flint.castcore.copy.AudioCopy
import com.rextechnologies.flint.castcore.copy.HonestyRules
import kotlin.math.PI
import kotlin.math.sin
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class AudioPolicyTest {
    private val states = listOf(
        AudioState.Off,
        AudioState.PlatformTooOld,
        AudioState.PermissionDenied,
        AudioState.Capturing,
        AudioState.Sounding,
        AudioState.Failed("the encoder refused the format"),
    )

    @Test
    fun `every state has its own word`() {
        val words = states.map { AudioPolicy.word(it) }
        assertEquals(words.size, words.toSet().size)
    }

    @Test
    fun `sound that is working needs no sentence, and every other state explains itself`() {
        assertNull(AudioPolicy.sentence(AudioState.Sounding, secondScreen = false))
        assertNull(AudioPolicy.sentence(AudioState.Off, secondScreen = false))
        assertEquals(AudioCopy.SECOND_SCREEN_SILENT, AudioPolicy.sentence(AudioState.Off, secondScreen = true))
        listOf(AudioState.PlatformTooOld, AudioState.PermissionDenied, AudioState.Capturing, AudioState.Failed("x"))
            .forEach { state ->
                val sentence = assertNotNull(AudioPolicy.sentence(state, secondScreen = false), state.toString())
                assertTrue(HonestyRules.isCompleteSentence(sentence), sentence)
                assertTrue(HonestyRules.confidentialityClaims(sentence).isEmpty(), sentence)
            }
    }

    @Test
    fun `a failure keeps the platform's own words, and copes without them`() {
        assertTrue(AudioPolicy.sentence(AudioState.Failed("no AAC encoder"), false)!!.contains("no AAC encoder"))
        assertTrue(AudioPolicy.sentence(AudioState.Failed(" "), false)!!.contains("without saying why"))
    }

    @Test
    fun `playback capture is an android 10 feature`() {
        assertEquals(29, AudioPolicy.MINIMUM_API)
    }
}

class AudioLevelTest {
    private fun pcm(samples: IntArray): ByteArray {
        val bytes = ByteArray(samples.size * 2)
        samples.forEachIndexed { index, sample ->
            bytes[index * 2] = (sample and 0xff).toByte()
            bytes[index * 2 + 1] = ((sample shr 8) and 0xff).toByte()
        }
        return bytes
    }

    @Test
    fun `silence is zero and full scale is one`() {
        assertEquals(0.0, AudioLevel.rms(pcm(IntArray(480)), 0, 960))
        val square = pcm(IntArray(480) { if (it % 2 == 0) 32_767 else -32_768 })
        assertTrue(AudioLevel.rms(square, 0, square.size) > 0.999)
    }

    @Test
    fun `a quiet sine is audible and a whisper of noise is not`() {
        val sine = pcm(IntArray(4_800) { (sin(2 * PI * it / 48) * 2_000).toInt() })
        assertTrue(AudioLevel.isAudible(AudioLevel.rms(sine, 0, sine.size)))
        val noise = pcm(IntArray(4_800) { if (it % 7 == 0) 3 else -2 })
        assertFalse(AudioLevel.isAudible(AudioLevel.rms(noise, 0, noise.size)))
    }

    @Test
    fun `the region is honoured and a bad one is refused`() {
        val loud = pcm(IntArray(10) { 20_000 })
        val quiet = ByteArray(20)
        val mixed = quiet + loud
        assertEquals(0.0, AudioLevel.rms(mixed, 0, 20))
        assertTrue(AudioLevel.rms(mixed, 20, 20) > 0.5)
        assertFailsWith<IllegalArgumentException> { AudioLevel.rms(mixed, 30, 20) }
    }
}

class AudioClockTest {
    @Test
    fun `one second of frames is one second of time, from the origin`() {
        val clock = AudioClock(48_000, originMicros = 5_000_000)
        assertEquals(5_000_000, clock.presentationTimeMicros(0))
        assertEquals(6_000_000, clock.presentationTimeMicros(48_000))
        assertEquals(5_500_000, clock.presentationTimeMicros(24_000))
        assertEquals(5_000_000 + 1_024L * 1_000_000 / 48_000, clock.presentationTimeMicros(1_024))
    }

    @Test
    fun `a long session does not overflow`() {
        val clock = AudioClock(48_000, originMicros = 0)
        val eightHours = 48_000L * 3_600 * 8
        assertEquals(8L * 3_600 * 1_000_000, clock.presentationTimeMicros(eightHours))
    }

    @Test
    fun `a bad rate, origin or count is refused`() {
        assertFailsWith<IllegalArgumentException> { AudioClock(0, 0) }
        assertFailsWith<IllegalArgumentException> { AudioClock(48_000, -1) }
        assertFailsWith<IllegalArgumentException> { AudioClock(48_000, 0).presentationTimeMicros(-1) }
    }
}
