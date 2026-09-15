package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class BrowserTabSessionTest {
    private val addressA = BrowserAddress("https://a.test/", "a.test")
    private val addressB = BrowserAddress("https://b.test/", "b.test")

    @Test
    fun `start binds the accepted page to the first tab`() {
        val session = BrowserTabSession()
        val page = page(addressA, commandId = 4)

        val transition = session.start(page)

        assertEquals(1, session.state.tabs.size)
        assertEquals(page, session.activePage())
        assertEquals("https://a.test/", session.state.active?.url)
        assertTrue(transition.effects.any { it is TabEffect.Create })
    }

    @Test
    fun `opening a blank tab preserves the epoch and command watermark`() {
        val session = started(page(addressA, commandId = 9))

        session.open(session.activePage()!!)

        val blank = session.activePage()
        assertNotNull(blank)
        assertEquals(41, blank.epoch)
        assertEquals(9, blank.lastAcceptedCommandId)
        assertEquals(BrowserPhase.READY, blank.phase)
        assertNull(blank.address)
    }

    @Test
    fun `remote new tab binds its accepted address to the shared command watermark`() {
        val session = started(page(addressA, commandId = 9))
        val acceptedControl = page(addressA, commandId = 10)

        session.openAccepted(acceptedControl, addressB)

        val opened = assertNotNull(session.activePage())
        assertEquals(BrowserPhase.LOADING, opened.phase)
        assertEquals(10, opened.navigationId)
        assertEquals(10, opened.lastAcceptedCommandId)
        assertEquals(addressB, opened.address)
    }

    @Test
    fun `duplicate copies only the selected address into a fresh page`() {
        val session = started(page(addressA, commandId = 9, title = "Alpha"))
        val sourceId = session.state.activeId
        val acceptedControl = page(addressA, commandId = 10, title = "Alpha")

        session.duplicate(acceptedControl, sourceId)

        val duplicate = assertNotNull(session.activePage())
        assertEquals(addressA, duplicate.address)
        assertEquals(10, duplicate.navigationId)
        assertNull(duplicate.title)
        assertEquals(2, session.state.tabs.size)
    }

    @Test
    fun `switching restores the exact page state belonging to that tab`() {
        val session = started(page(addressA, commandId = 1, title = "Alpha"))
        val first = session.state.activeId
        session.open(session.activePage()!!, addressB.canonicalUrl)
        val second = session.state.activeId
        session.recordActive(page(addressB, commandId = 2, title = "Beta"))

        session.select(session.activePage()!!, first)

        assertEquals(first, session.state.activeId)
        assertEquals("Alpha", session.activePage()?.title?.value)
        assertEquals("Beta", session.pageFor(second)?.title?.value)
    }

    @Test
    fun `late background callbacks update only their own tab`() {
        val session = started(page(addressA, commandId = 1, title = "Alpha"))
        val first = session.state.activeId
        session.open(session.activePage()!!)
        val activeBefore = session.activePage()

        session.recordEvent(first, BrowserStateEvent.Title(41, 1, "Late alpha"))

        assertEquals(activeBefore, session.activePage())
        assertEquals("Late alpha", session.pageFor(first)?.title?.value)
        assertEquals("Late alpha", session.state.tabs.first { it.id == first }.title)
    }

    @Test
    fun `stale background callbacks are ignored`() {
        val session = started(page(addressA, commandId = 3, title = "Alpha"))
        val id = session.state.activeId

        val after = session.recordEvent(id, BrowserStateEvent.Title(40, 3, "Wrong epoch"))

        assertEquals("Alpha", after?.title?.value)
    }

    @Test
    fun `closing removes the page snapshot and activates its successor`() {
        val session = started(page(addressA, commandId = 1))
        val first = session.state.activeId
        session.open(session.activePage()!!)
        val second = session.state.activeId

        val transition = session.close(session.activePage()!!, second)

        assertNull(session.pageFor(second))
        assertEquals(first, session.state.activeId)
        assertTrue(transition.effects.any { it is TabEffect.Destroy && it.id == second })
    }

    @Test
    fun `clear drops every tab and snapshot`() {
        val session = started(page(addressA, commandId = 1))
        val first = session.state.activeId

        session.clear()

        assertTrue(session.state.tabs.isEmpty())
        assertNull(session.pageFor(first))
        assertFalse(session.state.activeId > 0)
    }

    @Test
    fun `clampCommandWatermark lowers tab pages after host reconnect`() {
        val session = started(page(addressA, commandId = 9, title = "Alpha"))
        session.open(session.activePage()!!, addressB.canonicalUrl)
        session.recordActive(page(addressB, commandId = 12, title = "Beta"))
        val backgroundId = session.state.tabs.first { it.id != session.state.activeId }.id
        assertEquals(9, session.pageFor(backgroundId)!!.lastAcceptedCommandId)
        assertEquals(12, session.activePage()!!.lastAcceptedCommandId)

        session.clampCommandWatermark(0)

        assertEquals(0, session.pageFor(backgroundId)!!.lastAcceptedCommandId)
        assertEquals(0, session.activePage()!!.lastAcceptedCommandId)
        assertEquals(addressB, session.activePage()!!.address)
        assertEquals(41, session.activePage()!!.epoch)
    }

    @Test
    fun `clampCommandWatermark is a no-op when pages are already at or below the watermark`() {
        val session = started(page(addressA, commandId = 1))
        session.clampCommandWatermark(1)
        assertEquals(1, session.activePage()!!.lastAcceptedCommandId)
        session.clampCommandWatermark(0)
        assertEquals(0, session.activePage()!!.lastAcceptedCommandId)
    }

    private fun started(page: BrowserState) = BrowserTabSession().also { it.start(page) }

    private fun page(
        address: BrowserAddress,
        commandId: Long,
        title: String = "",
    ) = BrowserState(
        phase = BrowserPhase.READY,
        epoch = 41,
        navigationId = commandId,
        lastAcceptedCommandId = commandId,
        address = address,
        title = BrowserPageTitle.fromPage(title),
        progressPercent = 100,
    )
}
