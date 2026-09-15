package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class BrowserStateReducerTest {
    private val reducer = BrowserStateReducer()
    private val url = (
        BrowserUrlPolicy().evaluate(
            "https://example.com/private?secret=yes",
        ) as BrowserUrlResult.Accepted
        ).url

    @Test
    fun `accepted navigation produces monotonic safe browser state`() {
        val opening = reducer.reduce(BrowserState(), BrowserStateEvent.OpenAccepted(5, 1, url))
        assertEquals(BrowserPhase.OPENING, opening.phase)
        assertEquals(1, opening.revision)
        assertEquals("https://example.com/private", opening.address?.displayUrl)

        val loading = reducer.reduce(opening, BrowserStateEvent.NavigationAccepted(5, 2, url))
        val progress = reducer.reduce(loading, BrowserStateEvent.Progress(5, 2, 55))
        val titled = reducer.reduce(progress, BrowserStateEvent.Title(5, 2, "A page\nwith controls"))
        val ready = reducer.reduce(titled, BrowserStateEvent.PageFinished(5, 2, canGoBack = true, canGoForward = false))

        assertEquals(BrowserPhase.READY, ready.phase)
        assertEquals(5, ready.revision)
        assertEquals(100, ready.progressPercent)
        assertEquals("A page with controls", ready.title?.value)
        assertTrue(ready.canGoBack)
        assertFalse(ready.canGoForward)
    }

    @Test
    fun `pageFinished snaps progress to 100 even when chrome stalled mid-load`() {
        val opening = reducer.reduce(BrowserState(), BrowserStateEvent.OpenAccepted(5, 1, url))
        val progress = reducer.reduce(opening, BrowserStateEvent.Progress(5, 1, 21))
        val ready = reducer.reduce(
            progress,
            BrowserStateEvent.PageFinished(5, 1, canGoBack = false, canGoForward = false),
        )

        assertEquals(BrowserPhase.READY, ready.phase)
        assertEquals(100, ready.progressPercent)
    }

    @Test
    fun `blank open and profile reset produce bounded ready states`() {
        val blank = reducer.reduce(BrowserState(), BrowserStateEvent.BlankOpened(5, 1))
        assertEquals(BrowserPhase.READY, blank.phase)
        assertNull(blank.address)
        assertEquals(1, blank.navigationId)

        val loading = reducer.reduce(blank, BrowserStateEvent.NavigationAccepted(5, 2, url))
        val reset = reducer.reduce(loading, BrowserStateEvent.BlankReset(5, 3))

        assertEquals(BrowserPhase.READY, reset.phase)
        assertEquals(3, reset.navigationId)
        assertEquals(3, reset.lastAcceptedCommandId)
        assertNull(reset.address)
        assertNull(reset.title)
        assertEquals(0, reset.progressPercent)
    }

    @Test
    fun `stale callbacks and invalid progress cannot overwrite newer state or revision`() {
        val loading = reducer.reduce(BrowserState(), BrowserStateEvent.OpenAccepted(5, 1, url))
        val newer = reducer.reduce(loading, BrowserStateEvent.NavigationAccepted(5, 2, url))

        val stale = reducer.reduce(newer, BrowserStateEvent.PageFinished(5, 1, true, true))
        val invalid = reducer.reduce(newer, BrowserStateEvent.Progress(5, 2, 101))
        assertEquals(newer, stale)
        assertEquals(newer, invalid)
    }

    @Test
    fun `safe failures and close clear active page without raw details`() {
        val loading = reducer.reduce(BrowserState(), BrowserStateEvent.OpenAccepted(5, 1, url))
        val failed = reducer.reduce(loading, BrowserStateEvent.Failed(5, 1, BrowserFailure.CERTIFICATE_REJECTED))
        assertEquals(BrowserPhase.ERROR, failed.phase)
        assertEquals(BrowserFailure.CERTIFICATE_REJECTED, failed.failure)

        val closing = reducer.reduce(failed, BrowserStateEvent.CloseAccepted(5, 2))
        val closed = reducer.reduce(closing, BrowserStateEvent.Closed(5, 2))
        assertEquals(BrowserPhase.IDLE, closed.phase)
        assertNull(closed.epoch)
        assertNull(closed.address)
        assertNull(closed.failure)
        assertEquals(2, closed.lastAcceptedCommandId)
    }

    @Test
    fun `newer openAccepted reclaims a live page that was never closed`() {
        // Without this, command reclaim succeeds but the visible BrowserState keeps the orphan
        // address — Windows shows a new URL while the TV still paints the old one.
        val orphan = reducer.reduce(BrowserState(), BrowserStateEvent.OpenAccepted(4, 1, url))
        val ready = reducer.reduce(
            orphan,
            BrowserStateEvent.PageFinished(4, 1, canGoBack = false, canGoForward = false),
        )
        val search = (
            BrowserUrlPolicy().evaluate(
                "https://www.google.com/search?q=weather",
            ) as BrowserUrlResult.Accepted
            ).url

        val reclaimed = reducer.reduce(ready, BrowserStateEvent.OpenAccepted(5, 1, search))

        assertEquals(BrowserPhase.OPENING, reclaimed.phase)
        assertEquals(5L, reclaimed.epoch)
        assertEquals(1, reclaimed.navigationId)
        assertEquals(search.displayUrl, reclaimed.address?.displayUrl)
        assertNull(reclaimed.title)
        assertEquals(0, reclaimed.progressPercent)
    }

    @Test
    fun `openAccepted reclaim refuses equal or older epochs`() {
        val live = reducer.reduce(BrowserState(), BrowserStateEvent.OpenAccepted(5, 1, url))
        assertEquals(live, reducer.reduce(live, BrowserStateEvent.OpenAccepted(5, 2, url)))
        assertEquals(live, reducer.reduce(live, BrowserStateEvent.OpenAccepted(4, 1, url)))
    }

    @Test
    fun `newer blankOpened reclaims a live page`() {
        val live = reducer.reduce(BrowserState(), BrowserStateEvent.OpenAccepted(3, 1, url))
        val blank = reducer.reduce(live, BrowserStateEvent.BlankOpened(8, 1))

        assertEquals(BrowserPhase.READY, blank.phase)
        assertEquals(8L, blank.epoch)
        assertNull(blank.address)
    }
}
