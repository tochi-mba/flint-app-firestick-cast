package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertIs
import kotlin.test.assertTrue

class BrowserCoordinatorTest {
    @Test
    fun `open publishes loading state and drives the port`() {
        val published = mutableListOf<BrowserState>()
        val port = RecordingPort()
        val coordinator = BrowserCoordinator(publish = { published += it })
        coordinator.attachPort(port)

        val effect = coordinator.handleOpen(1, 1, "https://example.test/start")

        assertIs<BrowserCommandEffect.Open>(effect)
        assertEquals(1, port.openCalls.size)
        assertEquals(BrowserPhase.OPENING, coordinator.snapshot().phase)
        assertTrue(published.isNotEmpty())
    }

    @Test
    fun `rejected url never calls the port`() {
        val port = RecordingPort()
        val coordinator = BrowserCoordinator()
        coordinator.attachPort(port)

        val effect = coordinator.handleOpen(1, 1, "javascript:alert(1)")

        assertIs<BrowserCommandEffect.Rejected>(effect)
        assertTrue(port.openCalls.isEmpty())
        assertEquals(BrowserPhase.IDLE, coordinator.snapshot().phase)
    }

    @Test
    fun `stale command id is ignored before the port is touched`() {
        val port = RecordingPort()
        val coordinator = BrowserCoordinator()
        coordinator.attachPort(port)
        coordinator.handleOpen(1, 2, "https://example.test/a")

        val effect = coordinator.handleNavigate(1, 1, "https://example.test/b")

        assertIs<BrowserCommandEffect.Rejected>(effect)
        assertEquals(1, port.openCalls.size)
        assertTrue(port.navigateCalls.isEmpty())
    }

    @Test
    fun `close returns idle after the port is released`() {
        val port = RecordingPort()
        val coordinator = BrowserCoordinator()
        coordinator.attachPort(port)
        coordinator.handleOpen(1, 1, "https://example.test/a")

        val effect = coordinator.handleClose(1, 2)

        assertIs<BrowserCommandEffect.Close>(effect)
        assertEquals(1, port.closeCalls.size)
        assertEquals(BrowserPhase.CLOSING, coordinator.snapshot().phase)
    }

    @Test
    fun `an open that arrives before the page exists is replayed on attach`() {
        // This is the ordinary first navigation of a session, not an edge case. The host's OPEN is
        // what makes the browser surface appear, so the Activity has not created a WebView yet when
        // it lands. Dropping it there produced a blank page that every layer called a success.
        val coordinator = BrowserCoordinator()
        coordinator.handleOpen(1, 1, "https://example.test/a")

        val port = RecordingPort()
        coordinator.attachPort(port)

        assertEquals(1, port.openCalls.size)
        assertEquals("https://example.test/a", port.openCalls.single().canonicalUrl)
    }

    @Test
    fun `a replayed navigation is not delivered twice`() {
        // Attaching a second time — a configuration change recreates the view — must not reload the
        // page and throw away whatever the user had done on it.
        val coordinator = BrowserCoordinator()
        coordinator.handleOpen(1, 1, "https://example.test/a")

        val first = RecordingPort()
        coordinator.attachPort(first)
        val second = RecordingPort()
        coordinator.attachPort(second)

        assertEquals(1, first.openCalls.size)
        assertEquals(0, second.openCalls.size)
    }

    @Test
    fun `the most recent navigation is the one replayed`() {
        // A host that opens and then immediately navigates should land on the second address, not
        // the first: the newest instruction is the one the user is waiting to see.
        val coordinator = BrowserCoordinator()
        coordinator.handleOpen(1, 1, "https://example.test/a")
        coordinator.handleNavigate(1, 2, "https://example.test/b")

        val port = RecordingPort()
        coordinator.attachPort(port)

        val delivered = port.openCalls.map { it.canonicalUrl } + port.navigateCalls.map { it.canonicalUrl }
        assertEquals(listOf("https://example.test/b"), delivered)
    }

    @Test
    fun `a page attached before any command receives nothing`() {
        // Nothing pending means nothing replayed; an attach must not synthesise a navigation.
        val port = RecordingPort()
        val coordinator = BrowserCoordinator()

        coordinator.attachPort(port)

        assertEquals(0, port.openCalls.size)
        assertEquals(0, port.navigateCalls.size)
    }

    @Test
    fun `an accepted input reaches the attached page`() {
        // The line that makes the whole remote work. Before it existed every reducer accepted
        // input, every layer reported success, and the page never moved.
        val port = RecordingPort()
        val coordinator = BrowserCoordinator()
        coordinator.attachPort(port)

        coordinator.dispatchInput(BrowserNativeInput.KeyStroke(BrowserNativeKey.DOWN))

        assertEquals(1, port.dispatched.size)
        assertEquals(BrowserNativeInput.KeyStroke(BrowserNativeKey.DOWN), port.dispatched.single())
    }

    @Test
    fun `input arriving with no page attached is dropped rather than thrown`() {
        // Ordinary between a session opening and the Activity creating its view. An input from that
        // window is stale by the time a view exists, so dropping it is correct and failing is not.
        val coordinator = BrowserCoordinator()

        coordinator.dispatchInput(BrowserNativeInput.KeyStroke(BrowserNativeKey.SELECT))
    }

    @Test
    fun `input stops reaching a page that has been detached`() {
        // A detached view may already be destroyed; dispatching into it is a crash on the UI thread.
        val port = RecordingPort()
        val coordinator = BrowserCoordinator()
        coordinator.attachPort(port)
        coordinator.detachPort(port)

        coordinator.dispatchInput(BrowserNativeInput.KeyStroke(BrowserNativeKey.UP))

        assertEquals(0, port.dispatched.size)
    }

    @Test
    fun `the viewport is reported from the attached page`() {
        // Pointer placement scales to this, so a guess here misplaces every click.
        val coordinator = BrowserCoordinator()
        coordinator.attachPort(RecordingPort(viewport = 1920 to 1200))

        assertEquals(1920 to 1200, coordinator.viewport())
    }

    @Test
    fun `the viewport is absent when nothing is attached or laid out`() {
        // Absent rather than zero: a caller scaling coordinates then divides by nothing rather than
        // by zero, and rejects the input instead of placing it at the origin.
        val coordinator = BrowserCoordinator()
        assertNull(coordinator.viewport())

        coordinator.attachPort(RecordingPort(viewport = null))
        assertNull(coordinator.viewport())
    }

    @Test
    fun `a session ending clears the page the television is showing`() {
        // Otherwise the last page of a host that has gone stays on the screen indefinitely, which
        // reads as a live session to anyone in the room.
        val published = mutableListOf<BrowserState>()
        val coordinator = BrowserCoordinator(publish = { published += it })
        coordinator.attachPort(RecordingPort())
        coordinator.handleOpen(1, 1, "https://example.test/start")

        coordinator.handleSessionEnded()

        assertEquals(BrowserState(), coordinator.snapshot())
        assertEquals(BrowserState(), published.last())
    }

    @Test
    fun `a new session can open after the previous one died`() {
        // The defect this pins: the surface stayed claimed by a session that no longer existed, so
        // the next OPEN was refused and the television could not be driven again without a restart.
        val port = RecordingPort()
        val coordinator = BrowserCoordinator()
        coordinator.attachPort(port)
        coordinator.handleOpen(1, 1, "https://example.test/first")

        coordinator.handleSessionEnded()
        val effect = coordinator.handleOpen(2, 1, "https://example.test/second")

        assertIs<BrowserCommandEffect.Open>(effect)
        assertEquals(2, port.openCalls.size)
    }

    @Test
    fun `a reconnecting host can open over an orphan browser without session-end cleanup`() {
        // TLS onSessionEnded intentionally leaves TV-owned browsing up. Without reclaim, the next
        // Windows Open is SURFACE_BUSY and the address bar lie ("opened") never reaches the glass.
        val port = RecordingPort()
        val coordinator = BrowserCoordinator()
        coordinator.attachPort(port)
        assertIs<BrowserCommandEffect.Open>(
            coordinator.handleOpen(1, 1, "https://example.test/orphan"),
        )

        val effect = coordinator.handleOpen(2, 1, "https://example.test/search")

        assertIs<BrowserCommandEffect.Open>(effect)
        assertEquals(2, effect.epoch)
        assertEquals(1, effect.commandId)
        assertEquals(
            listOf("https://example.test/orphan", "https://example.test/search"),
            port.openCalls.map { it.canonicalUrl },
        )
        assertEquals(2L, coordinator.snapshot().epoch)
    }

    @Test
    fun `resetHostCommandWatermark lets a reconnecting host start at command id 1`() {
        val coordinator = BrowserCoordinator()
        assertIs<BrowserCommandEffect.Open>(
            coordinator.handleOpen(5, 4, "https://example.test/live"),
        )
        assertIs<BrowserCommandEffect.Control>(coordinator.handleControl(5, 5))

        coordinator.resetHostCommandWatermark()

        assertIs<BrowserCommandEffect.Control>(coordinator.handleControl(5, 1))
        assertEquals(1, coordinator.snapshot().lastAcceptedCommandId)
        assertEquals(5L, coordinator.snapshot().epoch)
    }

    @Test
    fun `a navigation waiting for a view does not survive the session that asked for it`() {
        // A held navigation is replayed the moment a view appears. Held across a session boundary
        // it would open the dead host's page on the next host's surface.
        val coordinator = BrowserCoordinator()
        coordinator.handleOpen(1, 1, "https://example.test/held")

        coordinator.handleSessionEnded()
        val port = RecordingPort()
        coordinator.attachPort(port)

        assertTrue(port.openCalls.isEmpty())
    }

    @Test
    fun `ending a session that never opened anything is harmless`() {
        val coordinator = BrowserCoordinator()

        coordinator.handleSessionEnded()

        assertEquals(BrowserState(), coordinator.snapshot())
    }

    @Test
    fun `history controls reach the attached page in the order they were asked for`() {
        val port = RecordingPort()
        val coordinator = BrowserCoordinator()
        coordinator.attachPort(port)

        coordinator.goBack()
        coordinator.goForward()
        coordinator.reload()
        coordinator.stopLoading()

        assertEquals(listOf("back", "forward", "reload", "stop"), port.historyCalls)
    }

    @Test
    fun `history controls are dropped rather than crashing when no page is attached`() {
        val coordinator = BrowserCoordinator()

        coordinator.goBack()
        coordinator.goForward()
        coordinator.reload()
        coordinator.stopLoading()

        assertEquals(BrowserState(), coordinator.snapshot())
    }

    @Test
    fun `a detached page stops receiving history controls`() {
        val port = RecordingPort()
        val coordinator = BrowserCoordinator()
        coordinator.attachPort(port)
        coordinator.goBack()

        coordinator.detachPort(port)
        coordinator.goForward()

        assertEquals(listOf("back"), port.historyCalls)
    }

    @Test
    fun `blank TV open is immediately ready and sends no synthetic navigation`() {
        val published = mutableListOf<BrowserState>()
        val port = RecordingPort()
        val coordinator = BrowserCoordinator(publish = published::add)
        coordinator.attachPort(port)

        val effect = coordinator.handleOpenBlank(7, 1)

        assertIs<BrowserCommandEffect.OpenBlank>(effect)
        assertEquals(BrowserPhase.READY, coordinator.snapshot().phase)
        assertNull(coordinator.snapshot().address)
        assertTrue(port.openCalls.isEmpty())
        assertTrue(port.navigateCalls.isEmpty())
        assertEquals(coordinator.snapshot(), published.last())
    }

    @Test
    fun `profile reset discards the old page without calling it a navigation`() {
        val port = RecordingPort()
        val coordinator = BrowserCoordinator()
        coordinator.attachPort(port)
        coordinator.handleOpen(7, 1, "https://example.test/private")

        val effect = coordinator.handleResetBlank(7, 2)

        assertIs<BrowserCommandEffect.ResetBlank>(effect)
        assertEquals(BrowserPhase.READY, coordinator.snapshot().phase)
        assertNull(coordinator.snapshot().address)
        assertEquals(2, coordinator.snapshot().navigationId)
        assertTrue(port.navigateCalls.isEmpty())
    }

    @Test
    fun `attach reports whether it replayed the pending open`() {
        val coordinator = BrowserCoordinator()
        coordinator.handleOpen(7, 1, "https://example.test")

        assertTrue(coordinator.attachPort(RecordingPort()))
        assertFalse(coordinator.attachPort(RecordingPort()))
    }

    @Test
    fun `discardPendingNavigation prevents a later attach from replaying the open`() {
        val coordinator = BrowserCoordinator()
        coordinator.handleOpen(7, 1, "https://www.google.com/search?q=first")
        coordinator.discardPendingNavigation()

        val port = RecordingPort()
        assertFalse(coordinator.attachPort(port))
        assertTrue(port.openCalls.isEmpty())
    }

    @Test
    fun `after watermark reset activatePage accepts tab snapshot clamped to zero`() {
        val coordinator = BrowserCoordinator()
        coordinator.attachPort(RecordingPort())
        coordinator.handleOpen(7, 4, "https://one.test")
        coordinator.handleControl(7, 5)
        val orphanTab = BrowserState(
            phase = BrowserPhase.READY,
            epoch = 7,
            navigationId = 4,
            lastAcceptedCommandId = 5,
            address = BrowserAddress("https://one.test/", "one.test"),
        )
        coordinator.resetHostCommandWatermark()
        // Tab layer clamps before activate — same as ReceiverBrowserController on reconnect.
        val clamped = orphanTab.copy(lastAcceptedCommandId = 0)

        assertTrue(coordinator.activatePage(clamped))
        assertEquals(0, coordinator.snapshot().lastAcceptedCommandId)
        assertEquals(7L, coordinator.snapshot().epoch)
    }

    @Test
    fun `a saved page in the active epoch can become foreground without resetting commands`() {
        val published = mutableListOf<BrowserState>()
        val coordinator = BrowserCoordinator(publish = published::add)
        coordinator.attachPort(RecordingPort())
        coordinator.handleOpen(7, 1, "https://one.test")
        coordinator.handleNavigate(7, 2, "https://two.test")
        val saved = BrowserState(
            phase = BrowserPhase.READY,
            epoch = 7,
            navigationId = 1,
            lastAcceptedCommandId = 1,
            address = BrowserAddress("https://one.test/", "one.test"),
        )

        assertTrue(coordinator.activatePage(saved))
        assertEquals(saved, coordinator.snapshot())
        assertEquals(saved, published.last())
        assertIs<BrowserCommandEffect.Navigate>(
            coordinator.handleNavigate(7, 3, "https://three.test"),
        )
    }

    @Test
    fun `a page from another epoch or beyond the command watermark cannot become foreground`() {
        val coordinator = BrowserCoordinator()
        coordinator.attachPort(RecordingPort())
        coordinator.handleOpen(7, 1, "https://one.test")
        val before = coordinator.snapshot()

        assertFalse(coordinator.activatePage(before.copy(epoch = 6)))
        assertFalse(coordinator.activatePage(before.copy(lastAcceptedCommandId = 2)))
        assertEquals(before, coordinator.snapshot())
    }

    private class RecordingPort(
        private val viewport: Pair<Int, Int>? = 1280 to 720,
    ) : BrowserPort {
        val openCalls = mutableListOf<BrowserAddress>()
        val navigateCalls = mutableListOf<BrowserAddress>()
        val closeCalls = mutableListOf<Long>()
        val dispatched = mutableListOf<BrowserNativeInput>()
        val historyCalls = mutableListOf<String>()

        override fun open(epoch: Long, commandId: Long, address: BrowserAddress) {
            openCalls += address
        }

        override fun navigate(epoch: Long, commandId: Long, address: BrowserAddress) {
            navigateCalls += address
        }

        override fun close(epoch: Long, commandId: Long) {
            closeCalls += epoch
        }

        override fun goBack() { historyCalls += "back" }
        override fun goForward() { historyCalls += "forward" }
        override fun reload() { historyCalls += "reload" }
        override fun stop() { historyCalls += "stop" }

        override fun dispatch(input: BrowserNativeInput) {
            dispatched += input
        }

        override fun viewport(): Pair<Int, Int>? = viewport
    }
}
