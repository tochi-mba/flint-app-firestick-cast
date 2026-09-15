package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * Short-lived messages about things the browser refused to do.
 *
 * Bounded on purpose: a page that fires a hundred blocked popups must not be able to queue a
 * hundred notices, and the newest one is always the one worth reading.
 */
class BrowserNoticeReducerTest {
    private val reducer = BrowserNoticeReducer()

    @Test
    fun `a refusal becomes a sentence the viewer can read`() {
        val state = reducer.show(BrowserNoticeState(), BrowserRefusal.DOWNLOAD, nowMs = 1_000)

        assertEquals(BrowserRefusal.DOWNLOAD.viewerSentence, state.active?.message)
        assertTrue(state.active!!.id > 0)
    }

    @Test
    fun `a newer notice replaces an older one rather than queueing behind it`() {
        val first = reducer.show(BrowserNoticeState(), BrowserRefusal.POPUP, nowMs = 1_000)

        val second = reducer.show(first, BrowserRefusal.UPLOAD, nowMs = 1_200)

        assertEquals(BrowserRefusal.UPLOAD.viewerSentence, second.active?.message)
        assertNotEquals(first.active!!.id, second.active!!.id)
    }

    @Test
    fun `the same refusal repeated does not restart as a new notice`() {
        // A page can fire the same blocked request in a loop. Re-issuing the identical sentence
        // would make it flash on every attempt and never finish expiring.
        val first = reducer.show(BrowserNoticeState(), BrowserRefusal.POPUP, nowMs = 1_000)

        val second = reducer.show(first, BrowserRefusal.POPUP, nowMs = 1_100)

        assertEquals(first.active!!.id, second.active!!.id)
        assertEquals(first.active.shownAtMs, second.active.shownAtMs)
    }

    @Test
    fun `a notice withdraws itself once its time is up`() {
        val shown = reducer.show(BrowserNoticeState(), BrowserRefusal.LOCATION, nowMs = 1_000)

        val expired = reducer.expire(shown, nowMs = 1_000 + BrowserNoticeReducer.VISIBLE_MILLIS)

        assertNull(expired.active)
    }

    @Test
    fun `a notice stays put until its time is actually up`() {
        val shown = reducer.show(BrowserNoticeState(), BrowserRefusal.LOCATION, nowMs = 1_000)

        val still = reducer.expire(shown, nowMs = 1_000 + BrowserNoticeReducer.VISIBLE_MILLIS - 1)

        assertEquals(shown.active, still.active)
    }

    @Test
    fun `dismissing clears whatever is showing and is safe when nothing is`() {
        val shown = reducer.show(BrowserNoticeState(), BrowserRefusal.PERMISSION, nowMs = 1_000)

        assertNull(reducer.dismiss(shown).active)
        assertNull(reducer.dismiss(BrowserNoticeState()).active)
    }

    @Test
    fun `expiring an empty state is not an event`() {
        val empty = BrowserNoticeState()

        assertEquals(empty, reducer.expire(empty, nowMs = Long.MAX_VALUE))
    }

    @Test
    fun `every refusal carries a sentence rather than a name`() {
        BrowserRefusal.entries.forEach { refusal ->
            assertTrue(refusal.viewerSentence.isNotBlank(), "$refusal has no sentence")
            assertTrue(refusal.viewerSentence.none { it == '_' }, "$refusal leaks its name")
        }
    }
}
