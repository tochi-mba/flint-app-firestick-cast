package com.rextechnologies.flint.receiver.browser

/**
 * Whatever can put a custom view inside one mosaic pane and hold the display awake.
 *
 * Pane-local by default: no Activity immersive mode unless the controller is told theater mode is
 * on (every other pane suspended). Generic over the view type so ordering is testable without a
 * device — production uses `android.view.View`, tests use a string.
 */
interface PaneFullscreenHost<V> {
    fun showInPane(view: V)
    fun hideInPane()

    /** Holds the display awake, because a playing video is not user activity. */
    fun setKeepScreenOn(enabled: Boolean)

    /**
     * Hides or restores the system bars.
     *
     * Only invoked when theater mode is active; ordinary pane fullscreen stays non-immersive so
     * other panes remain visible and reachable.
     */
    fun setImmersive(enabled: Boolean)
}

/**
 * One pane's page-requested fullscreen, honoured exactly once at a time for that pane.
 *
 * Differs from [BrowserFullscreenController] in that immersive chrome is off by default — the
 * custom view fills the pane's bounds only. Activity-wide immersive runs solely when
 * [theaterMode] reports true.
 *
 * A second enter while already active is refused: accepting would orphan the first view and its
 * callback, which shows as a permanently black pane with no way back.
 */
class PaneFullscreenController<V>(
    private val host: PaneFullscreenHost<V>,
    private val theaterMode: () -> Boolean = { false },
    private val canEnter: () -> Boolean = { true },
) : BrowserCustomViewController<V> {
    private var activeView: V? = null
    private var release: (() -> Unit)? = null
    private var immersiveApplied: Boolean = false

    val isActive: Boolean get() = activeView != null

    /**
     * Enters pane-local fullscreen for [view], answering [onReleased] when it ends.
     *
     * Returns false when this controller already has a fullscreen view, or when [canEnter] says no.
     */
    override fun enter(view: V, onReleased: () -> Unit): Boolean {
        if (activeView != null || !canEnter()) {
            return false
        }
        activeView = view
        release = onReleased
        host.showInPane(view)
        val immersive = theaterMode()
        immersiveApplied = immersive
        if (immersive) {
            host.setImmersive(true)
        }
        host.setKeepScreenOn(true)
        return true
    }

    /** Leaves fullscreen and tells the page. Returns whether there was anything to leave. */
    override fun exit(): Boolean = teardown(notifyPage = true)

    /**
     * Leaves fullscreen without telling the page.
     *
     * For a pane or surface that is being destroyed: the callback belongs to a renderer that is
     * going away, but the wake hold (and any theater immersive) are this window's and still have
     * to come back.
     */
    fun abandon(): Boolean = teardown(notifyPage = false)

    private fun teardown(notifyPage: Boolean): Boolean {
        if (activeView == null) {
            return false
        }
        activeView = null
        val pending = release
        release = null
        val wasImmersive = immersiveApplied
        immersiveApplied = false

        host.setKeepScreenOn(false)
        if (wasImmersive) {
            host.setImmersive(false)
        }
        host.hideInPane()
        if (notifyPage) {
            pending?.invoke()
        }
        return true
    }
}
