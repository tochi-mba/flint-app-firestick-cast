package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class BrowserFindControllerTest {
    @Test
    fun `starting a search installs the listener before asking the page`() {
        val target = RecordingFindTarget()
        val states = mutableListOf<BrowserFindState>()
        val controller = BrowserFindController(target, states::add)

        controller.start("needle")

        assertEquals(listOf("listener", "find:needle"), target.calls)
        assertEquals("needle", controller.state.query)
        assertTrue(controller.state.active)
        assertEquals(controller.state, states.last())
    }

    @Test
    fun `blank search clears matches instead of asking WebView to find everything`() {
        val target = RecordingFindTarget()
        val controller = BrowserFindController(target)

        controller.start("   ")

        assertEquals(listOf("clear"), target.calls)
        assertFalse(controller.state.active)
    }

    @Test
    fun `WebView zero based result becomes a bounded one based counter`() {
        val target = RecordingFindTarget()
        val controller = BrowserFindController(target)
        controller.start("needle")

        target.emit(active = 1, total = 4, done = true)

        assertEquals(2, controller.state.currentMatch)
        assertEquals(4, controller.state.totalMatches)
        assertTrue(controller.state.doneCounting)
    }

    @Test
    fun `next and previous preserve direction and do nothing without a query`() {
        val target = RecordingFindTarget()
        val controller = BrowserFindController(target)

        controller.next()
        controller.previous()
        assertTrue(target.calls.isEmpty())

        controller.start("needle")
        target.calls.clear()
        controller.next()
        controller.previous()

        assertEquals(listOf("next:true", "next:false"), target.calls)
    }

    @Test
    fun `clearing removes the listener and page highlights`() {
        val target = RecordingFindTarget()
        val controller = BrowserFindController(target)
        controller.start("needle")
        target.calls.clear()

        controller.clear()

        assertEquals(listOf("listener:null", "clear"), target.calls)
        assertEquals(BrowserFindState(), controller.state)
    }

    @Test
    fun `a delayed result from a replaced query cannot overwrite the current counter`() {
        val target = RecordingFindTarget()
        val controller = BrowserFindController(target)
        controller.start("first")
        val firstListener = target.currentListener!!
        controller.start("second")

        firstListener.onFindResult(activeMatchOrdinal = 8, numberOfMatches = 9, doneCounting = true)

        assertEquals("second", controller.state.query)
        assertEquals(0, controller.state.totalMatches)
        target.emit(active = 1, total = 3, done = true)
        assertEquals(2, controller.state.currentMatch)
        assertEquals(3, controller.state.totalMatches)
    }

    @Test
    fun `blanking an active search detaches its listener before clearing highlights`() {
        val target = RecordingFindTarget()
        val controller = BrowserFindController(target)
        controller.start("needle")
        target.calls.clear()

        controller.start("  ")

        assertEquals(listOf("listener:null", "clear"), target.calls)
        assertEquals(BrowserFindState(), controller.state)
    }

    private class RecordingFindTarget : BrowserFindTarget {
        val calls = mutableListOf<String>()
        var currentListener: BrowserFindResultListener? = null
            private set

        override fun setFindListener(listener: BrowserFindResultListener?) {
            currentListener = listener
            calls += if (listener == null) "listener:null" else "listener"
        }

        override fun findAllAsync(query: String) {
            calls += "find:$query"
        }

        override fun findNext(forward: Boolean) {
            calls += "next:$forward"
        }

        override fun clearMatches() {
            calls += "clear"
        }

        fun emit(active: Int, total: Int, done: Boolean) {
            currentListener?.onFindResult(active, total, done)
        }
    }
}
