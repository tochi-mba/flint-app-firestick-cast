package com.rextechnologies.flint.protocol.media

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

class BitrateCeilingTest {
    private fun headroom() = LinkSample(receiverQueueDepth = 0, decodeLatencyUs = 0, roundTripTimeUs = 0, droppedVideoFrames = 0)

    @Test
    fun `a ceiling below the current rate cuts it, and one above leaves it alone`() {
        val controller = BitrateController(initialBitrate = 8_000_000)
        assertEquals(5_000_000, controller.applyCeiling(5_000_000))
        assertEquals(5_000_000, controller.currentBitrate)
        assertEquals(5_000_000, controller.applyCeiling(12_000_000))
        assertEquals(12_000_000, controller.ceiling)
    }

    @Test
    fun `the probe upward stops at the ceiling rather than the session maximum`() {
        val controller = BitrateController(initialBitrate = 4_000_000)
        controller.applyCeiling(4_600_000)
        repeat(40) { controller.onSample(headroom()) }
        assertEquals(4_600_000, controller.currentBitrate)
        controller.clearCeiling()
        repeat(40) { controller.onSample(headroom()) }
        assertTrue(controller.currentBitrate > 4_600_000)
        assertEquals(controller.maximumBitrate, controller.ceiling)
    }

    @Test
    fun `a ceiling can never raise the rate above the maximum or below the minimum`() {
        val controller = BitrateController(minimumBitrate = 1_000_000, maximumBitrate = 10_000_000, initialBitrate = 5_000_000)
        controller.applyCeiling(50_000_000)
        assertEquals(10_000_000, controller.ceiling)
        assertEquals(5_000_000, controller.currentBitrate)
        controller.applyCeiling(1)
        assertEquals(1_000_000, controller.ceiling)
        assertEquals(1_000_000, controller.currentBitrate)
    }

    @Test
    fun `a reset respects the ceiling in force`() {
        val controller = BitrateController(initialBitrate = 8_000_000)
        controller.applyCeiling(3_000_000)
        controller.reset(9_000_000)
        assertEquals(3_000_000, controller.currentBitrate)
    }
}
