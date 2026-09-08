package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/**
 * The page's own fullscreen button.
 *
 * A WebView asks for fullscreen by handing the embedder a view and a callback. Nothing was
 * listening, so every fullscreen control on every video site did nothing at all — the single most
 * visible thing missing from the television browser.
 */
class BrowserFullscreenControllerTest {
    @Test
    fun `entry policy can refuse a background tab without touching the host`() {
        val host = RecordingHost()
        val controller = BrowserFullscreenController(host, canEnter = { false })

        assertFalse(controller.enter("background video") {})

        assertFalse(controller.isActive)
        assertTrue(host.calls.isEmpty())
    }

    @Test
    fun `entering fullscreen shows the view, hides chrome, and holds the screen awake`() {
        val host = RecordingHost()
        val controller = BrowserFullscreenController(host)

        val accepted = controller.enter(VIEW) { }

        assertTrue(accepted)
        assertTrue(controller.isActive)
        assertEquals(
            listOf("show:$VIEW", "immersive:true", "keepAwake:true"),
            host.calls,
        )
    }

    @Test
    fun `leaving fullscreen reverses every step in order and answers the page`() {
        val host = RecordingHost()
        val controller = BrowserFullscreenController(host)
        var released = false
        controller.enter(VIEW) { released = true }
        host.calls.clear()

        controller.exit()

        assertFalse(controller.isActive)
        assertTrue(released)
        assertEquals(
            listOf("keepAwake:false", "immersive:false", "hide"),
            host.calls,
        )
    }

    @Test
    fun `a second request while already fullscreen is refused rather than stacking views`() {
        // Chromium is entitled to call this again; accepting would orphan the first view and leave
        // a callback nobody can answer, which is a permanently black screen.
        val host = RecordingHost()
        val controller = BrowserFullscreenController(host)
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
        val controller = BrowserFullscreenController(host)

        val exited = controller.exit()

        assertFalse(exited)
        assertTrue(host.calls.isEmpty())
    }

    @Test
    fun `the page's callback is only ever answered once`() {
        // Back and onHideCustomView can both arrive for the same fullscreen session. Answering
        // twice is a crash in some WebView builds.
        val host = RecordingHost()
        val controller = BrowserFullscreenController(host)
        var releases = 0
        controller.enter(VIEW) { releases += 1 }

        controller.exit()
        controller.exit()

        assertEquals(1, releases)
    }

    @Test
    fun `tearing the surface down leaves fullscreen without asking the page`() {
        // The WebView is going away. Its callback belongs to a renderer that is being destroyed, so
        // the chrome and wake lock still have to be restored locally.
        val host = RecordingHost()
        val controller = BrowserFullscreenController(host)
        var released = false
        controller.enter(VIEW) { released = true }
        host.calls.clear()

        controller.abandon()

        assertFalse(controller.isActive)
        assertFalse(released)
        assertEquals(listOf("keepAwake:false", "immersive:false", "hide"), host.calls)
    }

    private companion object {
        const val VIEW = "video-surface"
    }

    private class RecordingHost : BrowserFullscreenHost<String> {
        val calls = mutableListOf<String>()

        override fun showFullscreenView(view: String) {
            calls += "show:$view"
        }

        override fun hideFullscreenView() {
            calls += "hide"
        }

        override fun setImmersive(enabled: Boolean) {
            calls += "immersive:$enabled"
        }

        override fun setKeepScreenOn(enabled: Boolean) {
            calls += "keepAwake:$enabled"
        }
    }
}
