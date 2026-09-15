package com.rextechnologies.flint.castcore.screen

import com.rextechnologies.flint.castcore.copy.HonestyRules
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNull
import kotlin.test.assertTrue

class SecondScreenSceneTest {
    @Test
    fun `a dashboard names the television and the phone, or says which is which when it cannot`() {
        val named = SecondScreenScene.Dashboard("Fire TV Stick", "Pixel 8", "Stable")
        assertEquals("Fire TV Stick", named.televisionLabel)
        assertEquals("Pixel 8", named.phoneLabel)
        assertEquals(SecondScreenScene.DASHBOARD_TITLE, named.title)

        val unnamed = SecondScreenScene.Dashboard("", "", "Stable")
        assertEquals(SceneCopy.THIS_TELEVISION, unnamed.televisionLabel)
        assertEquals(SceneCopy.THIS_PHONE, unnamed.phoneLabel)
    }

    @Test
    fun `a dashboard without a word for the link is refused`() {
        assertFailsWith<IllegalArgumentException> { SecondScreenScene.Dashboard("TV", "Phone", " ") }
    }

    @Test
    fun `now playing reports progress only once the duration is known`() {
        val unknown = SecondScreenScene.NowPlaying("holiday.mp4", "Playing", 65_000, SecondScreenScene.UNKNOWN_DURATION)
        assertNull(unknown.progress)
        assertEquals("1:05", unknown.clock)

        val known = SecondScreenScene.NowPlaying("holiday.mp4", "Paused", 65_000, 130_000)
        assertEquals(0.5, known.progress)
        assertEquals("1:05 / 2:10", known.clock)
        assertEquals(SecondScreenScene.NOW_PLAYING_TITLE, known.title)
    }

    @Test
    fun `a position past the end reads as the end rather than as more than everything`() {
        val over = SecondScreenScene.NowPlaying("a", "Ended", 200_000, 130_000)
        assertEquals(1.0, over.progress)
    }

    @Test
    fun `a nameless file is called untitled on the television`() {
        assertEquals(SceneCopy.UNTITLED, SecondScreenScene.NowPlaying("", "Playing", 0, -1).mediaLabel)
    }

    @Test
    fun `now playing refuses a negative position, a duration below unknown, and a blank state`() {
        assertFailsWith<IllegalArgumentException> { SecondScreenScene.NowPlaying("a", "Playing", -1, -1) }
        assertFailsWith<IllegalArgumentException> { SecondScreenScene.NowPlaying("a", "Playing", 0, -2) }
        assertFailsWith<IllegalArgumentException> { SecondScreenScene.NowPlaying("a", " ", 0, -1) }
    }

    @Test
    fun `the television's own copy reads as sentences and makes no confidentiality claim`() {
        assertTrue(HonestyRules.isCompleteSentence(SceneCopy.BOUNDARY))
        assertTrue(HonestyRules.confidentialityClaims(SceneCopy.BOUNDARY).isEmpty())
        assertTrue(SceneCopy.BOUNDARY.contains("phone's home screen"), SceneCopy.BOUNDARY)
    }
}

class PlaybackClockTest {
    @Test
    fun `under an hour reads as minutes and seconds, over an hour adds the hour`() {
        assertEquals("0:00", PlaybackClock.format(0))
        assertEquals("0:09", PlaybackClock.format(9_999))
        assertEquals("1:05", PlaybackClock.format(65_000))
        assertEquals("59:59", PlaybackClock.format(3_599_000))
        assertEquals("1:00:00", PlaybackClock.format(3_600_000))
        assertEquals("1:24:07", PlaybackClock.format(5_047_000))
    }

    @Test
    fun `a negative time is refused`() {
        assertFailsWith<IllegalArgumentException> { PlaybackClock.format(-1) }
    }
}
