package com.rextechnologies.flint.castcore.media

import com.rextechnologies.flint.protocol.wire.CodecId
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertTrue

class EncoderConfigTest {
    private fun config(
        width: Int = 1080,
        height: Int = 1920,
        frameRate: Int = 60,
        bitrate: Int = 8_000_000,
        bFrames: Int = 0,
    ) = EncoderConfig(
        codec = CodecId.H264,
        width = width,
        height = height,
        frameRate = frameRate,
        bitrateBitsPerSecond = bitrate,
        keyFrameStrategy = KeyFrameStrategy.OnDemand,
        lowLatencyRequested = true,
        maxBFrames = bFrames,
    )

    @Test
    fun `no b-frames, ever`() {
        // A B-frame cannot be emitted until a later frame has been encoded, which is latency by
        // construction. It is a constant rather than an option for exactly that reason.
        assertFailsWith<IllegalArgumentException> { config(bFrames = 1) }
        assertEquals(0, config().maxBFrames)
    }

    @Test
    fun `odd dimensions are refused rather than quietly rounded`() {
        assertFailsWith<IllegalArgumentException> { config(width = 1081) }
        assertFailsWith<IllegalArgumentException> { config(height = 1921) }
    }

    @Test
    fun `nonsense is refused`() {
        assertFailsWith<IllegalArgumentException> { config(width = 0) }
        assertFailsWith<IllegalArgumentException> { config(height = 0) }
        assertFailsWith<IllegalArgumentException> { config(frameRate = 0) }
        assertFailsWith<IllegalArgumentException> { config(frameRate = 121) }
        assertFailsWith<IllegalArgumentException> { config(bitrate = 0) }
    }

    @Test
    fun `a fallback interval outside a few seconds is not a fallback`() {
        assertFailsWith<IllegalArgumentException> { KeyFrameStrategy.BoundedInterval(0) }
        assertFailsWith<IllegalArgumentException> { KeyFrameStrategy.BoundedInterval(11) }
        assertEquals(2, KeyFrameStrategy.BoundedInterval(2).seconds)
    }
}

class EncoderPolicyTest {
    @Test
    fun `a source already inside the cap is left alone`() {
        assertEquals(1080 to 1920, EncoderPolicy.scaleToFit(1080, 1920, 1920))
    }

    @Test
    fun `a source above the cap keeps its aspect ratio`() {
        val (width, height) = EncoderPolicy.scaleToFit(2560, 1440, 1920)
        assertEquals(1920, width)
        assertEquals(1080, height)
    }

    @Test
    fun `the long edge is whichever edge is longer, not the width`() {
        val (width, height) = EncoderPolicy.scaleToFit(1440, 2560, 1920)
        assertEquals(1920, height)
        assertEquals(1080, width)
    }

    @Test
    fun `everything comes back even`() {
        listOf(1081 to 1921, 999 to 333, 3841 to 2161).forEach { (w, h) ->
            val (width, height) = EncoderPolicy.scaleToFit(w, h, 1920)
            assertEquals(0, width % 2, "$w -> $width")
            assertEquals(0, height % 2, "$h -> $height")
        }
    }

    @Test
    fun `a tiny source never scales below a usable size`() {
        assertEquals(2 to 2, EncoderPolicy.scaleToFit(1, 1, 1920))
    }

    @Test
    fun `an impossible request is refused`() {
        assertFailsWith<IllegalArgumentException> { EncoderPolicy.scaleToFit(0, 100, 1920) }
        assertFailsWith<IllegalArgumentException> { EncoderPolicy.scaleToFit(100, 0, 1920) }
        assertFailsWith<IllegalArgumentException> { EncoderPolicy.scaleToFit(100, 100, 1) }
    }

    @Test
    fun `low latency is only asked for where the platform has the knob`() {
        assertFalse(
            EncoderPolicy.forSession(CodecId.H265, 1080, 1920, 8_000_000, apiLevel = 29)
                .lowLatencyRequested,
        )
        assertTrue(
            EncoderPolicy.forSession(CodecId.H265, 1080, 1920, 8_000_000, apiLevel = 30)
                .lowLatencyRequested,
        )
    }

    @Test
    fun `a session config carries the negotiated codec and the scaled size`() {
        val config = EncoderPolicy.forSession(CodecId.H265, 2560, 1440, 6_000_000, apiLevel = 34)
        assertEquals(CodecId.H265, config.codec)
        assertEquals(1920, config.width)
        assertEquals(1080, config.height)
        assertEquals(6_000_000, config.bitrateBitsPerSecond)
        assertEquals(EncoderPolicy.FRAME_RATE, config.frameRate)
        assertIs<KeyFrameStrategy.OnDemand>(config.keyFrameStrategy)
    }

    @Test
    fun `the cap is the same one the windows host uses`() {
        assertEquals(1920, EncoderPolicy.MAXIMUM_LONG_EDGE)
    }
}

class KeyFrameGovernorTest {
    @Test
    fun `on demand is the preferred setting and the starting one`() {
        assertIs<KeyFrameStrategy.OnDemand>(KeyFrameGovernor().strategy)
    }

    @Test
    fun `an honoured request keeps the preferred setting`() {
        val governor = KeyFrameGovernor(framesBeforeFallback = 3)
        governor.onSyncFrameRequested()
        governor.onFrameEmitted(keyFrame = false)
        assertIs<KeyFrameStrategy.OnDemand>(governor.onFrameEmitted(keyFrame = true))
        repeat(50) { governor.onFrameEmitted(keyFrame = false) }
        assertIs<KeyFrameStrategy.OnDemand>(governor.strategy)
    }

    @Test
    fun `an ignored request falls back to a bounded interval, and says so`() {
        val governor = KeyFrameGovernor(framesBeforeFallback = 3)
        governor.onSyncFrameRequested()
        governor.onFrameEmitted(keyFrame = false)
        governor.onFrameEmitted(keyFrame = false)
        val strategy = governor.onFrameEmitted(keyFrame = false)
        val bounded = assertIs<KeyFrameStrategy.BoundedInterval>(strategy)
        assertEquals(2, bounded.seconds)
    }

    @Test
    fun `frames without a request never trigger a fallback`() {
        val governor = KeyFrameGovernor(framesBeforeFallback = 2)
        repeat(100) { governor.onFrameEmitted(keyFrame = false) }
        assertIs<KeyFrameStrategy.OnDemand>(governor.strategy)
    }

    @Test
    fun `an encoder that ignored one request is not asked to prove itself again`() {
        val governor = KeyFrameGovernor(framesBeforeFallback = 1)
        governor.onSyncFrameRequested()
        assertIs<KeyFrameStrategy.BoundedInterval>(governor.onFrameEmitted(keyFrame = false))

        governor.onSyncFrameRequested()
        assertIs<KeyFrameStrategy.BoundedInterval>(governor.onFrameEmitted(keyFrame = true))
        assertIs<KeyFrameStrategy.BoundedInterval>(governor.strategy)
    }

    @Test
    fun `a receiver that keeps asking does not keep the watchdog from firing`() {
        // The case the class exists for, and the one it used to miss: an encoder that ignores the
        // request, and a receiver that re-asks just before the threshold. Restarting the count on
        // every request meant the fallback never engaged on exactly that device.
        val governor = KeyFrameGovernor(framesBeforeFallback = 10)
        repeat(9) {
            governor.onSyncFrameRequested()
            governor.onFrameEmitted(keyFrame = false)
        }
        governor.onSyncFrameRequested()
        assertIs<KeyFrameStrategy.BoundedInterval>(governor.onFrameEmitted(keyFrame = false))
    }

    @Test
    fun `a request after an honoured one starts a fresh count`() {
        val governor = KeyFrameGovernor(framesBeforeFallback = 3)
        governor.onSyncFrameRequested()
        governor.onFrameEmitted(keyFrame = false)
        governor.onFrameEmitted(keyFrame = true)

        governor.onSyncFrameRequested()
        governor.onFrameEmitted(keyFrame = false)
        governor.onFrameEmitted(keyFrame = false)
        assertIs<KeyFrameStrategy.OnDemand>(governor.strategy)
        assertIs<KeyFrameStrategy.BoundedInterval>(governor.onFrameEmitted(keyFrame = false))
    }

    @Test
    fun `a governor with an absurd threshold is refused`() {
        assertFailsWith<IllegalArgumentException> { KeyFrameGovernor(framesBeforeFallback = 0) }
        assertFailsWith<IllegalArgumentException> { KeyFrameGovernor(framesBeforeFallback = 601) }
    }

    @Test
    fun `a fallback interval that is not a fallback is refused at construction`() {
        // Not at the moment it engages, which would be mid-cast on the codec callback thread.
        assertFailsWith<IllegalArgumentException> { KeyFrameGovernor(fallbackSeconds = 0) }
        assertFailsWith<IllegalArgumentException> { KeyFrameGovernor(fallbackSeconds = 11) }
    }

    @Test
    fun `the two entry points can be called from different threads`() {
        // Both are, and always were: a request arrives on the control-socket reader and a frame on
        // the codec callback. Without the lock this races on two plain vars; with it the outcome is
        // the same one a single thread would reach.
        repeat(20) {
            val governor = KeyFrameGovernor(framesBeforeFallback = 4)
            val requester = Thread {
                repeat(500) { governor.onSyncFrameRequested() }
            }
            val emitter = Thread {
                repeat(500) { governor.onFrameEmitted(keyFrame = false) }
            }
            requester.start()
            emitter.start()
            requester.join()
            emitter.join()
            // 500 unhonoured frames against a threshold of four: whatever the interleaving, the
            // watchdog has had every chance to fire and the strategy is stable afterwards.
            assertIs<KeyFrameStrategy.BoundedInterval>(governor.strategy)
        }
    }
}

class VideoConfigPolicyTest {
    @Test
    fun `a reconfigure is always followed by a key frame`() {
        // Every VIDEO_CONFIG is a hard decoder reset on the receiver: it releases the codec, discards
        // its whole pending queue and re-arms its key-frame gate. The next packet has to be an intra
        // frame or it is counted as dropped and thrown away, and the picture freezes.
        assertTrue(VideoConfigPolicy.requiresKeyFrameAfter())
    }
}
