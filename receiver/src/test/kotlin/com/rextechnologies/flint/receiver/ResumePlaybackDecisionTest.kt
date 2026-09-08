package com.rextechnologies.flint.receiver

import androidx.media3.common.Player
import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/**
 * Pins the bug where the remote's play/pause button did nothing after a clip finished: pressing
 * it called `Player.play()`, which is a no-op once `playbackState` is `STATE_ENDED` because there
 * is nothing left to render at the current (end-of-item) position.
 */
class ResumePlaybackDecisionTest {
    @Test
    fun `a finished item needs a seek to the start before play does anything`() {
        assertTrue(needsSeekToStartBeforeResuming(Player.STATE_ENDED))
    }

    @Test
    fun `every other player state can resume in place`() {
        assertFalse(needsSeekToStartBeforeResuming(Player.STATE_IDLE))
        assertFalse(needsSeekToStartBeforeResuming(Player.STATE_BUFFERING))
        assertFalse(needsSeekToStartBeforeResuming(Player.STATE_READY))
    }
}
