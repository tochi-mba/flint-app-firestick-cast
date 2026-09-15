package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/**
 * Pane-local fullscreen: custom view fills the pane, not the Activity, unless theater mode.
 */
class PaneFullscreenControllerTest {
    @Test
    fun `entry policy can refuse without touching the host`() {
        val host = RecordingHost()
        val controller = PaneFullscreenController(host, canEnter = { false })

        assertFalse(controller.enter("background video") {})

        assertFalse(controller.isActive)
        assertTrue(host.calls.isEmpty())
    }

    @Test
    fun `entering pane fullscreen shows in pane and holds the screen without immersive by default`() {
        val host = RecordingHost()
        val controller = PaneFullscreenController(host)

        val accepted = controller.enter(VIEW) { }

        assertTrue(accepted)
        assertTrue(controller.isActive)
        assertEquals(
            listOf("showInPane:$VIEW", "keepAwake:true"),
            host.calls,
        )
    }

    @Test
    fun `theater mode applies immersive on enter and clears it on exit`() {
        val host = RecordingHost()
        val controller = PaneFullscreenController(host, theaterMode = { true })
        var released = false
        controller.enter(VIEW) { released = true }
        assertEquals(
            listOf("showInPane:$VIEW", "immersive:true", "keepAwake:true"),
            host.calls,
        )
        host.calls.clear()

        controller.exit()

        assertTrue(released)
        assertEquals(
            listOf("keepAwake:false", "immersive:false", "hideInPane"),
            host.calls,
        )
    }

    @Test
    fun `leaving pane fullscreen reverses wake then hide and answers the page`() {
        val host = RecordingHost()
        val controller = PaneFullscreenController(host)
        var released = false
        controller.enter(VIEW) { released = true }
        host.calls.clear()

        controller.exit()

        assertFalse(controller.isActive)
        assertTrue(released)
        assertEquals(listOf("keepAwake:false", "hideInPane"), host.calls)
    }

    @Test
    fun `a second request while already fullscreen is refused rather than stacking views`() {
        val host = RecordingHost()
        val controller = PaneFullscreenController(host)
        var firstReleased = false
        controller.enter(VIEW) { firstReleased = true }
        host.calls.clear()

        val accepted = controller.enter("second") { }

        assertFalse(accepted)
        assertTrue(controller.isActive)
        assertFalse(firstReleased)
        assertTrue(host.calls.isEmpty())
    }

    @Test
    fun `exiting when nothing is fullscreen does nothing`() {
        val host = RecordingHost()
        val controller = PaneFullscreenController(host)

        assertFalse(controller.exit())
        assertTrue(host.calls.isEmpty())
    }

    @Test
    fun `the page callback is only ever answered once`() {
        val host = RecordingHost()
        val controller = PaneFullscreenController(host)
        var releases = 0
        controller.enter(VIEW) { releases += 1 }

        controller.exit()
        controller.exit()

        assertEquals(1, releases)
    }

    @Test
    fun `abandon leaves fullscreen without asking the page`() {
        val host = RecordingHost()
        val controller = PaneFullscreenController(host)
        var released = false
        controller.enter(VIEW) { released = true }
        host.calls.clear()

        controller.abandon()

        assertFalse(controller.isActive)
        assertFalse(released)
        assertEquals(listOf("keepAwake:false", "hideInPane"), host.calls)
    }

    private companion object {
        const val VIEW = "pane-video-surface"
    }

    private class RecordingHost : PaneFullscreenHost<String> {
        val calls = mutableListOf<String>()

        override fun showInPane(view: String) {
            calls += "showInPane:$view"
        }

        override fun hideInPane() {
            calls += "hideInPane"
        }

        override fun setKeepScreenOn(enabled: Boolean) {
            calls += "keepAwake:$enabled"
        }

        override fun setImmersive(enabled: Boolean) {
            calls += "immersive:$enabled"
        }
    }
}
