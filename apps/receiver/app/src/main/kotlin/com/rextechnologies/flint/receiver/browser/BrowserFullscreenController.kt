package com.rextechnologies.flint.receiver.browser

/**
 * Whatever can actually put a view over the page and change the window.
 *
 * Generic over the view type so the ordering policy below can be tested without a device: the
 * production binding uses `android.view.View`, the tests use a string.
 */
interface BrowserFullscreenHost<V> {
    fun showFullscreenView(view: V)
    fun hideFullscreenView()

    /** Hides or restores the system bars. */
    fun setImmersive(enabled: Boolean)

    /** Holds the display awake, because a playing video is not user activity. */
    fun setKeepScreenOn(enabled: Boolean)
}

/**
 * The narrow custom-view contract used by WebChromeClient.
 *
 * A normal single-page browser may make the activity immersive, while a browser-workspace pane
 * must keep a site's fullscreen view inside that pane.  WebView does not need to know which policy
 * owns the view; it only needs an exactly-once enter/exit pair.  Keeping that distinction behind
 * this interface prevents the workspace from accidentally reusing the activity-wide fullscreen
 * implementation.
 */
interface BrowserCustomViewController<V> {
    fun enter(view: V, onReleased: () -> Unit): Boolean
    fun exit(): Boolean
}

/**
 * The page's fullscreen request, honoured exactly once at a time.
 *
 * A WebView signals fullscreen by handing the embedder a view plus a callback to invoke when the
 * embedder is finished with it. Nothing in this app implemented that pair, so every fullscreen
 * button on every video site appeared to work and then did nothing — the loudest missing piece of
 * the television browser.
 *
 * Order matters in both directions and is asserted by tests. Going in: attach the view, then take
 * the screen, then hold it awake. Coming out: release the wake hold, give the system bars back,
 * then detach — so the page never becomes visible again underneath a still-immersive window.
 */
class BrowserFullscreenController<V>(
    private val host: BrowserFullscreenHost<V>,
    private val canEnter: () -> Boolean = { true },
) : BrowserCustomViewController<V> {
    private var activeView: V? = null
    private var release: (() -> Unit)? = null

    val isActive: Boolean get() = activeView != null

    /**
     * Enters fullscreen for [view], answering [onReleased] when it ends.
     *
     * Returns false when something is already fullscreen. Chromium is entitled to ask twice, and
     * accepting the second request would orphan the first view along with a callback nobody can
     * answer — which shows as a permanently black screen with no way back.
     */
    override fun enter(view: V, onReleased: () -> Unit): Boolean {
        if (activeView != null || !canEnter()) {
            return false
        }
        activeView = view
        release = onReleased
        host.showFullscreenView(view)
        host.setImmersive(true)
        host.setKeepScreenOn(true)
        return true
    }

    /** Leaves fullscreen and tells the page. Returns whether there was anything to leave. */
    override fun exit(): Boolean = teardown(notifyPage = true)

    /**
     * Leaves fullscreen without telling the page.
     *
     * For a surface that is being destroyed: the callback belongs to a renderer that is going away,
     * but the system bars and the wake hold are this window's and still have to come back.
     */
    fun abandon(): Boolean = teardown(notifyPage = false)

    private fun teardown(notifyPage: Boolean): Boolean {
        if (activeView == null) {
            return false
        }
        activeView = null
        // Taken before the callbacks run so a re-entrant exit cannot answer the page twice, which
        // some WebView builds treat as a crash rather than a no-op.
        val pending = release
        release = null

        host.setKeepScreenOn(false)
        host.setImmersive(false)
        host.hideFullscreenView()
        if (notifyPage) {
            pending?.invoke()
        }
        return true
    }
}
