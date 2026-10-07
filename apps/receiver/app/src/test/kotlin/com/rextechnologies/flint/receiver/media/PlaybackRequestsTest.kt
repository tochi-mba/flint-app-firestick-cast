package com.rextechnologies.flint.receiver.media

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/** How the TV reads what a sender asks of a picture, and of the volume. */
class PlaybackRequestsTest {
    @Test
    fun `a picture stays up as long as its sender asked`() {
        assertEquals(86_400_000L, pictureDurationMillis(86_400_000L))
        assertEquals(15_000L, pictureDurationMillis(15_000L))
    }

    @Test
    fun `a picture whose sender did not say stays the player's six seconds`() {
        // What every sender before the duration was honoured sends: -1, the field's default.
        assertEquals(DEFAULT_PICTURE_MILLIS, pictureDurationMillis(-1L))
        assertEquals(DEFAULT_PICTURE_MILLIS, pictureDurationMillis(0L))
    }

    @Test
    fun `only image types are pictures`() {
        assertTrue(isPicture("image/png"))
        assertTrue(isPicture("image/jpeg"))
        assertFalse(isPicture("video/mp4"))
        assertFalse(isPicture("audio/mpeg"))
        assertFalse(isPicture(""))
    }

    @Test
    fun `the volume level is the player's own, kept between silent and full`() {
        assertEquals(0.2f, playerVolume(0.2f))
        assertEquals(0f, playerVolume(-0.5f))
        assertEquals(1f, playerVolume(1.7f))
        assertEquals(1f, playerVolume(Float.NaN), "a level that is not a number leaves the player as loud as the TV")
    }
}
