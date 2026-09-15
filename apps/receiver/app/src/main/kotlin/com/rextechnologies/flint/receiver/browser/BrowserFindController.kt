package com.rextechnologies.flint.receiver.browser

import android.webkit.WebView

/** Listener shape kept free of Android so find ordering can be unit tested. */
fun interface BrowserFindResultListener {
    fun onFindResult(activeMatchOrdinal: Int, numberOfMatches: Int, doneCounting: Boolean)
}

/** The four WebView find operations used by the controller. */
interface BrowserFindTarget {
    fun setFindListener(listener: BrowserFindResultListener?)
    fun findAllAsync(query: String)
    fun findNext(forward: Boolean)
    fun clearMatches()
}

/** Production adapter; [BrowserFindController] itself stays platform-independent. */
class WebViewFindTarget(private val webView: WebView) : BrowserFindTarget {
    override fun setFindListener(listener: BrowserFindResultListener?) {
        webView.setFindListener(
            listener?.let { target ->
                WebView.FindListener { active, total, done ->
                    target.onFindResult(active, total, done)
                }
            },
        )
    }

    override fun findAllAsync(query: String) = webView.findAllAsync(query)

    override fun findNext(forward: Boolean) = webView.findNext(forward)

    override fun clearMatches() = webView.clearMatches()
}

/**
 * Drives asynchronous find-in-page while exposing only bounded display state.
 *
 * The listener is installed before a query is submitted. WebView is allowed to answer immediately,
 * and installing it afterwards loses the only result callback for short pages.
 */
class BrowserFindController(
    private val target: BrowserFindTarget,
    private val onChanged: (BrowserFindState) -> Unit = {},
) {
    var state: BrowserFindState = BrowserFindState()
        private set

    private var listenerAttached = false
    private var requestGeneration = 0L

    fun start(rawQuery: String) {
        val started = BrowserFindState.started(rawQuery)
        if (!started.active) {
            requestGeneration += 1
            if (listenerAttached) target.setFindListener(null)
            listenerAttached = false
            target.clearMatches()
            publish(BrowserFindState())
            return
        }

        publish(started)
        val generation = ++requestGeneration
        target.setFindListener { active, total, done ->
            onFindResult(generation, active, total, done)
        }
        listenerAttached = true
        target.findAllAsync(started.query)
    }

    fun next() {
        if (state.active) target.findNext(true)
    }

    fun previous() {
        if (state.active) target.findNext(false)
    }

    fun clear() {
        requestGeneration += 1
        target.setFindListener(null)
        listenerAttached = false
        target.clearMatches()
        publish(BrowserFindState())
    }

    private fun onFindResult(generation: Long, active: Int, total: Int, done: Boolean) {
        if (generation != requestGeneration || !state.active) return
        publish(state.withResult(active, total, done))
    }

    private fun publish(next: BrowserFindState) {
        state = next
        onChanged(next)
    }
}
