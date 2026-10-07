package com.rextechnologies.flint.receiver.ui

import com.rextechnologies.flint.protocol.wire.PlaybackState
import com.rextechnologies.flint.receiver.ReceiverUiState
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/** What the playback surface draws over the player, and what its bar shows. */
class ReceiverPlaybackSurfaceTest {
    @Test
    fun `an item that has not started yet gets the full screen wait`() {
        assertEquals(PlaybackOverlay.LOADING, playbackOverlay(state(PlaybackState.BUFFERING), started = false))
        assertEquals(PlaybackOverlay.LOADING, playbackOverlay(state(PlaybackState.IDLE), started = false))
    }

    @Test
    fun `a seek in an item already playing keeps the picture and shows the bar`() {
        // Found on a Fire TV: seeking dimmed the whole screen and said it was connecting to the PC.
        assertEquals(PlaybackOverlay.HUD, playbackOverlay(state(PlaybackState.BUFFERING), started = true))
    }

    @Test
    fun `playing, paused and finished items show the bar`() {
        for (playback in listOf(PlaybackState.PLAYING, PlaybackState.PAUSED, PlaybackState.ENDED)) {
            assertEquals(PlaybackOverlay.HUD, playbackOverlay(state(playback), started = false), "$playback")
        }
    }

    @Test
    fun `a failure is said whatever else is true`() {
        assertEquals(PlaybackOverlay.FAILURE, playbackOverlay(state(PlaybackState.ERROR), started = true))
        assertEquals(
            PlaybackOverlay.FAILURE,
            playbackOverlay(state(PlaybackState.PLAYING).copy(error = "unsupported codec"), started = true),
        )
    }

    @Test
    fun `the bar shows time and progress only for something that plays and has a length`() {
        assertTrue(hudShowsProgress(state(PlaybackState.PLAYING).copy(durationMs = 60_000)))
        assertFalse(hudShowsProgress(state(PlaybackState.PLAYING).copy(durationMs = -1)))
        assertFalse(
            hudShowsProgress(state(PlaybackState.PLAYING).copy(durationMs = 86_400_000, isPicture = true)),
            "a picture shown until the viewer moves on is not twenty-four hours long",
        )
    }

    private fun state(playback: PlaybackState) = ReceiverUiState(title = "Holiday", playbackState = playback)
}
