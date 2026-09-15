package com.rextechnologies.flint.receiver.browser.workspace

/**
 * How much of the screen the workspace chrome may take, and when it may take it.
 *
 * The pages are the point. Chrome that is permanently on screen was costing roughly forty per cent
 * of a 540dp television: a single page ended up rendered into a strip about three times wider than
 * it was tall, which reads as the mosaic barely being there at all.
 *
 * Two rules follow, and they are stated here rather than inside a composable so they can be tested
 * without a screen:
 *
 *  * While someone is driving a page, chrome is not on screen. Returning to workspace controls
 *    brings it back — the same bargain the single-page browser's omnibar already makes.
 *  * Per-pane controls belong to the focused pane only. Four panes' worth of media, theater and
 *    close chips stacked above the mosaic is what made the chrome tall in the first place.
 */
object BrowserWorkspaceChromePolicy {
    /**
     * The least share of the content height the mosaic may be left with.
     *
     * Not a layout instruction — a stated intent that [BrowserWorkspaceChromePolicyTest] holds the
     * chrome to, so a future row added to the chrome fails a test rather than quietly shrinking the
     * pages.
     */
    const val MINIMUM_MOSAIC_HEIGHT_FRACTION: Float = 0.72f

    /** Whether the chrome should be composed at all. */
    fun showsChrome(mode: BrowserWorkspaceInteractionMode): Boolean =
        mode != BrowserWorkspaceInteractionMode.PAGE

    /** Whether a pane gets its own controls, rather than only a chip naming it. */
    fun showsPaneControls(paneId: Long, focusedPaneId: Long?): Boolean = paneId == focusedPaneId
}
