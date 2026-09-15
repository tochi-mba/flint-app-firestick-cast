package com.rextechnologies.flint.protocol.media

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class BitrateControllerTest {
    private val healthy = LinkSample(
        receiverQueueDepth = 0,
        decodeLatencyUs = 10_000,
        roundTripTimeUs = 8_000,
        droppedVideoFrames = 0,
    )

    @Test
    fun `a healthy link probes upward only after several good samples`() {
        val controller = BitrateController(initialBitrate = 8_000_000)

        repeat(3) { assertEquals(8_000_000, controller.onSample(healthy).bitrateBitsPerSecond) }
        assertEquals(8_500_000, controller.onSample(healthy).bitrateBitsPerSecond)
    }

    @Test
    fun `a deep receiver queue backs off multiplicatively`() {
        val controller = BitrateController(initialBitrate = 10_000_000)

        val decision = controller.onSample(healthy.copy(receiverQueueDepth = 6))

        assertEquals(LinkHealth.Congested, decision.health)
        assertEquals(7_000_000, decision.bitrateBitsPerSecond)
    }

    @Test
    fun `high decode latency, high round trip, and unflushed bytes each signal congestion`() {
        assertEquals(
            LinkHealth.Congested,
            BitrateController().onSample(healthy.copy(decodeLatencyUs = 200_000)).health,
        )
        assertEquals(
            LinkHealth.Congested,
            BitrateController().onSample(healthy.copy(roundTripTimeUs = 200_000)).health,
        )
        assertEquals(
            LinkHealth.Congested,
            BitrateController().onSample(healthy.copy(pendingSendBytes = 1_000_000)).health,
        )
    }

    @Test
    fun `a moderate link neither probes nor backs off`() {
        val controller = BitrateController(initialBitrate = 6_000_000)

        val decision = controller.onSample(
            healthy.copy(receiverQueueDepth = 2, roundTripTimeUs = 90_000),
        )

        assertEquals(LinkHealth.Stable, decision.health)
        assertEquals(6_000_000, decision.bitrateBitsPerSecond)
    }

    @Test
    fun `a stable sample resets the probe streak`() {
        val controller = BitrateController(initialBitrate = 5_000_000)

        repeat(3) { controller.onSample(healthy) }
        controller.onSample(healthy.copy(receiverQueueDepth = 2, roundTripTimeUs = 90_000))
        repeat(3) { controller.onSample(healthy) }

        assertEquals(5_000_000, controller.currentBitrate)
        assertEquals(5_500_000, controller.onSample(healthy).bitrateBitsPerSecond)
    }

    @Test
    fun `backing off never falls below the floor`() {
        val controller = BitrateController(minimumBitrate = 2_000_000, initialBitrate = 2_400_000)

        repeat(10) { controller.onSample(healthy.copy(receiverQueueDepth = 9)) }

        assertEquals(2_000_000, controller.currentBitrate)
    }

    @Test
    fun `probing never rises above the ceiling`() {
        val controller = BitrateController(maximumBitrate = 9_000_000, initialBitrate = 8_800_000)

        repeat(40) { controller.onSample(healthy) }

        assertEquals(9_000_000, controller.currentBitrate)
    }

    @Test
    fun `only sustained frame loss asks for a key frame`() {
        val controller = BitrateController()

        assertFalse(controller.onSample(healthy.copy(droppedVideoFrames = 1)).requestKeyFrame)
        assertTrue(controller.onSample(healthy.copy(droppedVideoFrames = 6)).requestKeyFrame)
    }

    @Test
    fun `a receiver restart resets the drop baseline instead of counting backwards`() {
        val controller = BitrateController()
        controller.onSample(healthy.copy(droppedVideoFrames = 500))

        val decision = controller.onSample(healthy.copy(droppedVideoFrames = 0))

        assertFalse(decision.requestKeyFrame)
        assertEquals(LinkHealth.Headroom, decision.health)
    }

    @Test
    fun `reset restores a chosen bitrate and clears history`() {
        val controller = BitrateController(initialBitrate = 8_000_000)
        controller.onSample(healthy.copy(droppedVideoFrames = 10))

        controller.reset(6_000_000)

        assertEquals(6_000_000, controller.currentBitrate)
        assertFalse(controller.onSample(healthy.copy(droppedVideoFrames = 1)).requestKeyFrame)
    }

    @Test
    fun `reset clamps into the configured bounds`() {
        val controller = BitrateController(
            minimumBitrate = 2_000_000,
            maximumBitrate = 5_000_000,
            initialBitrate = 4_000_000,
        )

        controller.reset(50_000_000)
        assertEquals(5_000_000, controller.currentBitrate)

        controller.reset(1)
        assertEquals(2_000_000, controller.currentBitrate)
    }

    @Test
    fun `reset without an argument keeps the running bitrate`() {
        val controller = BitrateController(initialBitrate = 8_000_000)
        controller.onSample(healthy.copy(receiverQueueDepth = 9))

        controller.reset()

        assertEquals(5_600_000, controller.currentBitrate)
    }

    @Test
    fun `invalid bounds are refused`() {
        assertFailsWith<IllegalArgumentException> {
            BitrateController(minimumBitrate = 10, maximumBitrate = 5)
        }
        assertFailsWith<IllegalArgumentException> {
            BitrateController(minimumBitrate = 1_000, maximumBitrate = 2_000, initialBitrate = 9_000)
        }
        assertFailsWith<IllegalArgumentException> { LinkSample(-1, 0, 0, 0) }
        assertFailsWith<IllegalArgumentException> { LinkSample(0, -1, 0, 0) }
        assertFailsWith<IllegalArgumentException> { LinkSample(0, 0, -1, 0) }
        assertFailsWith<IllegalArgumentException> { LinkSample(0, 0, 0, -1) }
        assertFailsWith<IllegalArgumentException> { LinkSample(0, 0, 0, 0, pendingSendBytes = -1) }
    }
}
