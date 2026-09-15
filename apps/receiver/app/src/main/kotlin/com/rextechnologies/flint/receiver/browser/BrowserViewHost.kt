package com.rextechnologies.flint.receiver.browser

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Color
import android.view.View
import android.view.ViewGroup
import android.webkit.WebView
import android.widget.FrameLayout

/**
 * The container the browser actually lives in: the page, with room above it for fullscreen.
 *
 * A `WebView` alone cannot host fullscreen. Chromium signals it by handing the embedder a view to
 * display *over* the page, which needs a real `ViewGroup` — and the surface used to hand Compose the
 * bare `WebView`, so there was nowhere for that view to go and every fullscreen button silently did
 * nothing.
 *
 * Kept deliberately thin. Ownership stays with the Activity, and this class holds no callback, no
 * session state, and nothing that outlives the view itself.
 */
@SuppressLint("ViewConstructor")
class BrowserViewHost(context: Context) : FrameLayout(context), BrowserFullscreenViewHost {
    override val fullscreenContext: Context get() = context
    val webView: WebView = WebView(context).apply {
        layoutParams = LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT)
        isFocusable = true
        isFocusableInTouchMode = true
    }

    /**
     * Where a page's fullscreen view goes.
     *
     * Always present and always on top, so entering fullscreen is an add rather than a re-layout.
     * Gone while empty so it cannot swallow input meant for the page.
     */
    private val fullscreenContainer: FrameLayout = FrameLayout(context).apply {
        layoutParams = LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT)
        setBackgroundColor(Color.BLACK)
        visibility = GONE
    }

    init {
        layoutParams = ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT,
        )
        setBackgroundColor(Color.BLACK)
        addView(webView)
        addView(fullscreenContainer)
    }

    override fun showFullscreen(view: View) {
        fullscreenContainer.removeAllViews()
        fullscreenContainer.addView(
            view,
            LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT),
        )
        fullscreenContainer.visibility = VISIBLE
        // The page keeps rendering behind a fullscreen video otherwise, which on a Fire TV stick is
        // a second live compositor layer for something nobody can see.
        webView.visibility = INVISIBLE
        fullscreenContainer.requestFocus()
    }

    override fun hideFullscreen() {
        fullscreenContainer.removeAllViews()
        fullscreenContainer.visibility = GONE
        webView.visibility = VISIBLE
        webView.requestFocus()
    }

    /** Releases the page. The host is unusable afterwards and must be dropped with it. */
    fun destroy() {
        hideFullscreen()
        removeView(webView)
        webView.destroy()
    }
}
