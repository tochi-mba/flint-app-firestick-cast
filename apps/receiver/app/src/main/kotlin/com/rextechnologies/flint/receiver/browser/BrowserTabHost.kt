package com.rextechnologies.flint.receiver.browser

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Color
import android.os.Bundle
import android.view.View
import android.view.ViewGroup
import android.webkit.WebView
import android.widget.FrameLayout

/** One live tab: its view and the driver bound to it. */
internal class LiveTab(
    val webView: WebView,
    val driver: BrowserWebViewDriver,
)

/**
 * The container that owns every tab's renderer, and the fullscreen space above them.
 *
 * Replaces the single-page [BrowserViewHost]. Only the foreground tab is attached to the view
 * hierarchy; other live tabs keep their renderer but are detached, and frozen tabs are a
 * [Bundle] of navigation state with no renderer at all.
 *
 * The saved state is capped. Android's binder ceiling is a megabyte across the whole process, and a
 * long browsing history serialises to far more than people expect; a tab whose state will not fit
 * degrades to reloading its address, which is visibly imperfect but never a wrong page.
 */
@SuppressLint("ViewConstructor")
class BrowserTabHost(
    context: Context,
    private val buildDriver: (WebView, Long) -> BrowserWebViewDriver,
    private val onStateTooLarge: (Long) -> Unit = {},
    private val onEditingChanged: (Boolean) -> Unit = {},
) : FrameLayout(context), BrowserFullscreenViewHost {
    override val fullscreenContext: Context get() = context
    companion object {
        /** Comfortably clear of the binder ceiling once eight tabs are counted together. */
        const val MAX_SAVED_STATE_BYTES = 32 * 1024
    }

    private val pageContainer = FrameLayout(context).apply {
        layoutParams = LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT)
        setBackgroundColor(Color.BLACK)
    }

    private val fullscreenContainer = FrameLayout(context).apply {
        layoutParams = LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT)
        setBackgroundColor(Color.BLACK)
        visibility = GONE
    }

    private val live = LinkedHashMap<Long, LiveTab>()
    private val frozen = LinkedHashMap<Long, Bundle>()

    /** The address a frozen tab falls back to when its saved state was too large to keep. */
    private val fallbackUrls = LinkedHashMap<Long, String>()

    private var foreground: Long = 0
    private var pageFocusEnabled: Boolean = true

    init {
        layoutParams = ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT,
        )
        setBackgroundColor(Color.BLACK)
        addView(pageContainer)
        addView(fullscreenContainer)
    }

    /** The driver for the tab currently on the glass, or null before the first one exists. */
    fun activeDriver(): BrowserWebViewDriver? = live[foreground]?.driver

    fun activeId(): Long = foreground

    fun driverFor(id: Long): BrowserWebViewDriver? = live[id]?.driver

    fun liveCount(): Int = live.size

    fun isFrozen(id: Long): Boolean = frozen.containsKey(id) || fallbackUrls.containsKey(id)

    fun create(id: Long, url: String?) {
        if (live.containsKey(id)) {
            return
        }
        val webView = newWebView()
        val driver = buildDriver(webView, id)
        live[id] = LiveTab(webView, driver)
        url?.let { fallbackUrls[id] = it }
    }

    fun show(id: Long) {
        val tab = live[id] ?: return
        pageContainer.removeAllViews()
        pageContainer.addView(
            tab.webView,
            LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT),
        )
        foreground = id
        if (pageFocusEnabled) {
            tab.webView.requestFocus()
        }
    }

    /**
     * Lets Compose chrome take Select without the WebView eating the key at the Android view layer.
     */
    fun setPageFocusEnabled(enabled: Boolean) {
        pageFocusEnabled = enabled
        live.values.forEach { tab ->
            tab.webView.isFocusable = enabled
            tab.webView.isFocusableInTouchMode = enabled
        }
        if (!enabled) {
            pageContainer.findFocus()?.clearFocus()
            clearFocus()
        } else {
            live[foreground]?.webView?.requestFocus()
        }
    }

    /**
     * Saves a tab's place and releases its renderer.
     *
     * Detached from the view hierarchy first: destroying a WebView that is still attached leaves
     * the container holding a dead child, which paints as a black rectangle over the page.
     */
    fun freeze(id: Long) {
        val tab = live.remove(id) ?: return
        val bundle = Bundle()
        val saved = tab.webView.saveState(bundle)
        tab.webView.url?.let { fallbackUrls[id] = it }

        if (saved != null && bundleSize(bundle) <= MAX_SAVED_STATE_BYTES) {
            frozen[id] = bundle
        } else {
            // Reported rather than hidden. Reloading loses scroll position and form contents, and
            // the viewer is owed an explanation for why a tab jumped back to the top.
            onStateTooLarge(id)
        }

        if (tab.webView.parent === pageContainer) {
            pageContainer.removeView(tab.webView)
        }
        BrowserSecurityProfile.detach(tab.webView)
        tab.webView.destroy()
    }

    /** Rebuilds a renderer for a frozen tab, from saved state where it survived. */
    fun restore(id: Long) {
        if (live.containsKey(id)) {
            return
        }
        val webView = newWebView()
        val driver = buildDriver(webView, id)
        live[id] = LiveTab(webView, driver)

        val bundle = frozen.remove(id)
        if (bundle != null) {
            webView.restoreState(bundle)
            return
        }
        // No saved state: the next best thing is the address it was on.
        // The surface binds the driver's epoch/navigation identifiers before reloading this URL.
        // Loading it here would let callbacks escape with epoch 0 and be discarded as stale.
    }

    fun destroy(id: Long) {
        frozen.remove(id)
        fallbackUrls.remove(id)
        val tab = live.remove(id) ?: return
        if (tab.webView.parent === pageContainer) {
            pageContainer.removeView(tab.webView)
        }
        BrowserSecurityProfile.detach(tab.webView)
        tab.webView.destroy()
        if (foreground == id) {
            foreground = 0
        }
    }

    override fun showFullscreen(view: View) {
        fullscreenContainer.removeAllViews()
        fullscreenContainer.addView(
            view,
            LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT),
        )
        fullscreenContainer.visibility = VISIBLE
        // The page keeps compositing behind a fullscreen video otherwise, which on a stick is a
        // second live layer for something nobody can see.
        pageContainer.visibility = INVISIBLE
        fullscreenContainer.requestFocus()
    }

    override fun hideFullscreen() {
        fullscreenContainer.removeAllViews()
        fullscreenContainer.visibility = GONE
        pageContainer.visibility = VISIBLE
        live[foreground]?.webView?.requestFocus()
    }

    /** Releases every renderer. The host is unusable afterwards and must be dropped with it. */
    fun destroyAll() {
        hideFullscreen()
        live.keys.toList().forEach(::destroy)
        frozen.clear()
        fallbackUrls.clear()
    }

    private fun newWebView(): WebView = EditingAwareWebView(context, onEditingChanged).apply {
        layoutParams = LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT)
        isFocusable = pageFocusEnabled
        isFocusableInTouchMode = pageFocusEnabled
    }

    /**
     * How large a saved bundle is on the wire to the system.
     *
     * Measured by marshalling rather than guessed from history length: a page with a large form or
     * many entries is not proportional to anything cheap to count.
     */
    private fun bundleSize(bundle: Bundle): Int {
        val parcel = android.os.Parcel.obtain()
        return try {
            parcel.writeBundle(bundle)
            parcel.dataSize()
        } finally {
            parcel.recycle()
        }
    }
}
