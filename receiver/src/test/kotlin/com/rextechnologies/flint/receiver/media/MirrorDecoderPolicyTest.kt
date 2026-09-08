package com.rextechnologies.flint.receiver.media

import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class MirrorDecoderPolicyTest {
    @Test
    fun `initial surface attach preserves an IDR that arrived while Compose was mounting`() {
        assertTrue(
            preservePendingFramesOnSurfaceChange(
                hadSurface = false,
                hadCodec = false,
                hasSurface = true,
            ),
        )
    }

    @Test
    fun `surface replacement and detach do not retain frames submitted to the old codec`() {
        assertFalse(
            preservePendingFramesOnSurfaceChange(
                hadSurface = true,
                hadCodec = true,
                hasSurface = true,
            ),
        )
        assertFalse(
            preservePendingFramesOnSurfaceChange(
                hadSurface = true,
                hadCodec = true,
                hasSurface = false,
            ),
        )
    }

    @Test
    fun `accepted input keeps output polling alive after pending queue empties`() {
        assertTrue(videoDrainNeedsRetry(pendingFrames = 0, inputsOutstanding = 1))
        assertTrue(videoDrainNeedsRetry(pendingFrames = 1, inputsOutstanding = 0))
        assertFalse(videoDrainNeedsRetry(pendingFrames = 0, inputsOutstanding = 0))
    }
}
