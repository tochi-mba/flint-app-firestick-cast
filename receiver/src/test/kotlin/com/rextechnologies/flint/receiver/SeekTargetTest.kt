package com.rextechnologies.flint.receiver

import kotlin.test.Test
import kotlin.test.assertEquals

/** The clamping math behind D-pad and hardware-key scrubbing during playback. */
class SeekTargetTest {
    @Test
    fun `an ordinary scrub lands where the step says`() {
        assertEquals(15_000, seekTargetMs(positionMs = 10_000, deltaMs = 5_000, durationMs = 60_000))
        assertEquals(5_000, seekTargetMs(positionMs = 10_000, deltaMs = -5_000, durationMs = 60_000))
    }

    @Test
    fun `rewinding past the start clamps to zero rather than going negative`() {
        assertEquals(0, seekTargetMs(positionMs = 2_000, deltaMs = -10_000, durationMs = 60_000))
    }

    @Test
    fun `fast-forwarding past the end clamps to the duration rather than overshooting`() {
        assertEquals(60_000, seekTargetMs(positionMs = 58_000, deltaMs = 10_000, durationMs = 60_000))
    }

    @Test
    fun `an unknown duration sanitized to MAX_VALUE still lets fast-forward move forward`() {
        // ReceiverService.Long.safeDuration() maps an unset/unknown duration to Long.MAX_VALUE
        // before it ever reaches this function — clamping to a genuinely negative upper bound
        // would make every fast-forward silently do nothing, which is what this pins.
        assertEquals(15_000, seekTargetMs(positionMs = 10_000, deltaMs = 5_000, durationMs = Long.MAX_VALUE))
    }
}
