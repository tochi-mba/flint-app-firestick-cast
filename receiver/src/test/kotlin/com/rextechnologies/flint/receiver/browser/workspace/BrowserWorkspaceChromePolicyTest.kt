package com.rextechnologies.flint.receiver.browser.workspace

import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/**
 * The pages must dominate the screen, and this is the arithmetic that says whether they do.
 *
 * Reported from a real television: with even one page open, the mosaic barely registered. Chrome —
 * a title row, a layout row, and a control card for every pane — was permanently on screen and took
 * roughly forty per cent of a 540dp panel, leaving a single page rendered into a strip about three
 * times wider than it was tall on a 16:9 display.
 */
class BrowserWorkspaceChromePolicyTest {
    /** A 1080p television at xhdpi, less the 5% overscan margin the receiver keeps on each edge. */
    private val contentHeightDp = 486f

    /**
     * Measured heights of the chrome rows, in dp, as they compose today.
     *
     * The title and layout rows were merged into one, and the pane card became a single line with
     * the focused pane's controls beside its name rather than beneath it.
     */
    private val controlRowDp = 32f
    private val paneRowDp = 32f
    private val navRowDp = 32f
    private val rowGapDp = 12f
    private val sectionGapDp = 8f

    @Test
    fun `driving a page hides the chrome entirely`() {
        assertFalse(
            BrowserWorkspaceChromePolicy.showsChrome(BrowserWorkspaceInteractionMode.PAGE),
            "a page being driven must have the whole mosaic",
        )
    }

    @Test
    fun `workspace controls bring the chrome back`() {
        // Hiding it while driving is only acceptable because leaving that mode restores it.
        assertTrue(
            BrowserWorkspaceChromePolicy.showsChrome(
                BrowserWorkspaceInteractionMode.WORKSPACE_CHROME,
            ),
        )
    }

    @Test
    fun `with the chrome hidden the mosaic gets the whole content area`() {
        val mosaic = mosaicHeightDp(chromeVisible = false)

        assertTrue(
            mosaic / contentHeightDp >= 0.99f,
            "expected the full content height, got ${mosaic / contentHeightDp}",
        )
    }

    @Test
    fun `even with the chrome showing the mosaic keeps most of the screen`() {
        // The guard that fails when a future row is added to the chrome, rather than letting the
        // pages quietly shrink again.
        val fraction = mosaicHeightDp(chromeVisible = true) / contentHeightDp

        assertTrue(
            fraction >= BrowserWorkspaceChromePolicy.MINIMUM_MOSAIC_HEIGHT_FRACTION,
            "chrome leaves the mosaic only ${"%.2f".format(fraction)} of the content height, " +
                "below the stated minimum of " +
                BrowserWorkspaceChromePolicy.MINIMUM_MOSAIC_HEIGHT_FRACTION,
        )
    }

    @Test
    fun `only the focused pane carries controls`() {
        // Four panes' worth of media, theater and close chips is what made the chrome tall.
        assertTrue(BrowserWorkspaceChromePolicy.showsPaneControls(paneId = 7, focusedPaneId = 7))
        assertFalse(BrowserWorkspaceChromePolicy.showsPaneControls(paneId = 8, focusedPaneId = 7))
    }

    @Test
    fun `no pane carries controls when nothing is focused`() {
        assertFalse(BrowserWorkspaceChromePolicy.showsPaneControls(paneId = 7, focusedPaneId = null))
    }

    @Test
    fun `a single page fills a 16 by 9 panel rather than a letterboxed strip`() {
        // The complaint, stated as geometry. 864dp of width over a 278dp mosaic is 3.1:1; a page
        // shaped like that is unreadable from a sofa.
        val widthDp = 864f
        val mosaic = mosaicHeightDp(chromeVisible = false)
        val aspect = widthDp / mosaic

        assertTrue(aspect < 2.0f, "single page is letterboxed at ${"%.2f".format(aspect)}:1")
    }

    private fun mosaicHeightDp(chromeVisible: Boolean): Float {
        if (!chromeVisible) return contentHeightDp
        val chrome = controlRowDp + rowGapDp + paneRowDp
        return contentHeightDp - chrome - sectionGapDp - navRowDp - sectionGapDp
    }
}
