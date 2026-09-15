package com.rextechnologies.flint.receiver.browser

import android.annotation.SuppressLint
import android.graphics.Bitmap
import android.net.http.SslError
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.view.InputDevice
import android.view.KeyCharacterMap
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.View
import android.webkit.RenderProcessGoneDetail
import android.webkit.SslErrorHandler
import android.webkit.WebChromeClient
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.annotation.RequiresApi
import androidx.webkit.WebViewCompat
import androidx.webkit.WebViewFeature
import com.rextechnologies.flint.protocol.wire.BrowserPointerAction

/** Result of a requested per-WebView audio mute change. It is never a global stream mute. */
enum class BrowserPaneAudioMuteResult {
    APPLIED,
    UNSUPPORTED,
    FAILED,
}

/**
 * UI-thread WebView driver. The Activity owns the [WebView]; this adapter never escapes the UI
 * thread and never injects a JavaScript bridge.
 */
class BrowserWebViewDriver(
    private val webView: WebView,
    private val onEvent: (BrowserStateEvent) -> Unit,
    private val onPageDialog: (PendingJsDialog) -> Unit = {},
    private val fullscreen: BrowserCustomViewController<View>? = null,
    private val onFavicon: (Bitmap) -> Unit = {},
    private val onRefused: (BrowserRefusal) -> Unit = {},
    private val errorClassifier: BrowserErrorClassifier = BrowserErrorClassifier(),
    private val onFindChanged: (BrowserFindState) -> Unit = {},
) : BrowserPort {
    private companion object {
        /**
         * Fallback notch size when a host still speaks in wheel notches rather than pixels.
         * Kept for documentation/tests; live scroll now applies pixel deltas via [WebView.scrollBy].
         */
        const val SCROLL_PIXELS_PER_NOTCH = 60f
    }

    private val mainHandler = Handler(Looper.getMainLooper())
    private val urlPolicy = BrowserUrlPolicy()
    private val platformDefaultUserAgent = webView.settings.userAgentString.orEmpty()
    private val findController = BrowserFindController(WebViewFindTarget(webView), onFindChanged)
    private var activeEpoch: Long = 0
    private var activeNavigationId: Long = 0

    /** Down-time for the in-flight touch gesture; required for double-tap recognition. */
    private var gestureDownTime: Long = 0

    @Volatile
    private var capabilities = BrowserWebViewCapabilities(algorithmicDarkeningAvailable = false)

    init {
        capabilities = BrowserSecurityProfile.apply(
            settings = webView.settings,
            platformDefaultUserAgent = platformDefaultUserAgent,
        )
        // Zero asks WebView to choose its overview scale. Together with the wide viewport settings
        // this gives desktop layouts a usable first paint without weakening text zoom.
        webView.setInitialScale(0)
        webView.webViewClient = object : WebViewClient() {
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
                val url = request.url?.toString().orEmpty()
                return when (urlPolicy.evaluate(url)) {
                    is BrowserUrlResult.Accepted -> false
                    is BrowserUrlResult.Rejected -> {
                        // Reported, not just refused. A blocked link previously produced no
                        // feedback whatsoever: the page simply did not move, which reads as a
                        // broken browser rather than a policy the viewer could understand.
                        onRefused(refusalFor(request.url?.scheme))
                        true
                    }
                }
            }

            override fun onPageStarted(view: WebView, url: String?, favicon: Bitmap?) {
                onEvent(BrowserStateEvent.Progress(activeEpoch, activeNavigationId, 0))
            }

            override fun onPageFinished(view: WebView, url: String?) {
                onEvent(
                    BrowserStateEvent.PageFinished(
                        activeEpoch,
                        activeNavigationId,
                        view.canGoBack(),
                        view.canGoForward(),
                    ),
                )
            }

            override fun onReceivedError(
                view: WebView,
                request: WebResourceRequest?,
                error: WebResourceError?,
            ) {
                if (request?.isForMainFrame != true) return
                // Classified rather than collapsed. Every main-frame error used to arrive as
                // DRIVER_FAILURE, so a typo and a dead network gave the same unhelpful sentence.
                val code = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                    error?.errorCode ?: WebViewClient.ERROR_UNKNOWN
                } else {
                    WebViewClient.ERROR_UNKNOWN
                }
                onEvent(
                    BrowserStateEvent.Failed(
                        activeEpoch,
                        activeNavigationId,
                        errorClassifier.fromResourceError(code),
                    ),
                )
            }

            override fun onReceivedHttpError(
                view: WebView,
                request: WebResourceRequest?,
                errorResponse: WebResourceResponse?,
            ) {
                if (request?.isForMainFrame != true) return
                val failure = errorClassifier.fromHttpStatus(errorResponse?.statusCode ?: 0) ?: return
                onEvent(BrowserStateEvent.Failed(activeEpoch, activeNavigationId, failure))
            }

            /**
             * A certificate this browser will not trust.
             *
             * Cancelled unconditionally, with no "continue anyway" affordance. ADR-0006 is explicit
             * that no user consent converts a website certificate error into trust, and a remote
             * with five buttons is the worst possible place to make that decision.
             */
            override fun onReceivedSslError(
                view: WebView,
                handler: SslErrorHandler?,
                error: SslError?,
            ) {
                handler?.cancel()
                onEvent(
                    BrowserStateEvent.Failed(
                        activeEpoch,
                        activeNavigationId,
                        BrowserFailure.CERTIFICATE_REJECTED,
                    ),
                )
            }

            /**
             * The page's renderer process died.
             *
             * Returning true claims the crash so the whole app is not taken down with it — without
             * this, a page that runs out of memory kills the receiver. The API arrived in 26 and
             * the floor here is 25, so it is gated rather than assumed.
             */
            @RequiresApi(Build.VERSION_CODES.O)
            override fun onRenderProcessGone(view: WebView, detail: RenderProcessGoneDetail?): Boolean {
                onEvent(
                    BrowserStateEvent.Failed(
                        activeEpoch,
                        activeNavigationId,
                        BrowserFailure.RENDERER_STOPPED,
                    ),
                )
                return true
            }
        }
        // Downloads are refused by policy, but silently dropping one looks exactly like a broken
        // link. Naming it is the difference between a decision and a defect.
        webView.setDownloadListener { _, _, _, _, _ -> onRefused(BrowserRefusal.DOWNLOAD) }
        webView.webChromeClient = BrowserChromeClient(
            onDialog = onPageDialog,
            fullscreen = fullscreen,
            onFavicon = onFavicon,
            onRefused = onRefused,
            chrome = object : WebChromeClient() {
                override fun onProgressChanged(view: WebView, newProgress: Int) {
                    onEvent(BrowserStateEvent.Progress(activeEpoch, activeNavigationId, newProgress.coerceIn(0, 100)))
                }

                override fun onReceivedTitle(view: WebView, title: String?) {
                    onEvent(BrowserStateEvent.Title(activeEpoch, activeNavigationId, title.orEmpty()))
                }
            },
        )
    }

    /**
     * Which refusal a blocked navigation is.
     *
     * A `market:` or `intent:` link is a deliberate handoff to another app rather than a bad
     * address, and saying so stops the viewer retyping a URL that was never the problem.
     */
    private fun refusalFor(scheme: String?): BrowserRefusal = when (scheme?.lowercase()) {
        null, "https", "http" -> BrowserRefusal.BLOCKED_ADDRESS
        else -> BrowserRefusal.EXTERNAL_APP
    }

    /** The WebView this driver owns; used by preview capture and data clear. */
    fun webView(): WebView = webView

    fun activeEpoch(): Long = activeEpoch
    fun activeNavigationId(): Long = activeNavigationId

    /** The result of the latest feature-gated WebView preference application. */
    fun viewCapabilities(): BrowserWebViewCapabilities = capabilities

    fun applyViewSettings(state: BrowserViewState) {
        runOnUi {
            capabilities = BrowserSecurityProfile.applyViewSettings(
                settings = webView.settings,
                state = state,
                platformDefaultUserAgent = platformDefaultUserAgent,
            )
            BrowserSecurityProfile.assertHardened(webView.settings)
        }
    }

    fun startFind(query: String) = runOnUi { findController.start(query) }

    fun findNext() = runOnUi { findController.next() }

    fun findPrevious() = runOnUi { findController.previous() }

    fun clearFind() = runOnUi { findController.clear() }

    /**
     * Rebinds a renderer restored for a tab to the state it previously owned.
     *
     * `WebView.restoreState` restores Chromium, not this adapter's epoch guards. Without restoring
     * both identifiers every subsequent callback carries zero and is correctly rejected as stale.
     * If Android could not retain the renderer/history bundle, the canonical saved address is
     * reloaded only after those guards are in place.
     */
    fun resume(state: BrowserState) {
        runOnUi {
            activeEpoch = state.epoch ?: 0
            activeNavigationId = state.navigationId
            state.address?.let { address ->
                val current = webView.url
                // Load when blank *or* when a pending OPEN replay left the wrong URL on this
                // renderer (sibling Google searches after session restore).
                if (current.isNullOrBlank() || current != address.canonicalUrl) {
                    webView.loadUrl(address.canonicalUrl)
                }
            }
        }
    }

    override fun open(epoch: Long, commandId: Long, address: BrowserAddress) {
        runOnUi {
            activeEpoch = epoch
            activeNavigationId = commandId
            webView.loadUrl(address.canonicalUrl)
        }
    }

    override fun navigate(epoch: Long, commandId: Long, address: BrowserAddress) {
        runOnUi {
            activeEpoch = epoch
            activeNavigationId = commandId
            webView.loadUrl(address.canonicalUrl)
        }
    }

    override fun close(epoch: Long, commandId: Long) {
        runOnUi {
            findController.clear()
            BrowserSecurityProfile.detach(webView)
            onEvent(BrowserStateEvent.Closed(epoch, commandId))
        }
    }

    override fun goBack() = runOnUi { if (webView.canGoBack()) webView.goBack() }
    override fun goForward() = runOnUi { if (webView.canGoForward()) webView.goForward() }
    override fun reload() = runOnUi { webView.reload() }
    override fun stop() = runOnUi { webView.stopLoading() }

    override fun viewport(): Pair<Int, Int>? {
        val width = webView.width
        val height = webView.height
        // Zero until the first layout pass. Reported as absent rather than as 0x0 so a caller
        // scaling pointer coordinates divides by nothing rather than by zero.
        return if (width > 0 && height > 0) width to height else null
    }

    override fun dispatch(input: BrowserNativeInput) {
        runOnUi {
            when (input) {
                is BrowserNativeInput.KeyStroke -> dispatchKey(input)
                is BrowserNativeInput.ComposedText -> dispatchText(input)
                is BrowserNativeInput.Pointer -> dispatchPointer(input)
                is BrowserNativeInput.Scroll -> dispatchScroll(input)
            }
        }
    }

    /**
     * Requests play/pause from this workspace pane's WebView only.
     *
     * Site-dependent — Chromium maps the media key onto the focused media element when one exists,
     * so dispatch is never presented as proof that playback changed.
     */
    fun dispatchPlayPause() {
        runOnUi {
            val now = SystemClock.uptimeMillis()
            webView.dispatchKeyEvent(
                KeyEvent(now, now, KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE, 0),
            )
            webView.dispatchKeyEvent(
                KeyEvent(now, now, KeyEvent.ACTION_UP, KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE, 0),
            )
        }
    }

    /**
     * Applies a mute state to this WebView only when its provider exposes the AndroidX feature.
     *
     * This is deliberately not `AudioManager`: muting the MUSIC stream would mute every pane and
     * every other app on the television.  `MUTE_AUDIO` is a provider capability, so an unsupported
     * Fire OS WebView reports [BrowserPaneAudioMuteResult.UNSUPPORTED] instead of painting a lying
     * mute badge. The callback runs on the main thread.
     */
    fun setAudioMuted(muted: Boolean, onResult: (BrowserPaneAudioMuteResult) -> Unit) {
        runOnUi {
            if (!WebViewFeature.isFeatureSupported(WebViewFeature.MUTE_AUDIO)) {
                onResult(BrowserPaneAudioMuteResult.UNSUPPORTED)
                return@runOnUi
            }
            val result = runCatching {
                WebViewCompat.setAudioMuted(webView, muted)
                check(WebViewCompat.isAudioMuted(webView) == muted)
            }.fold(
                onSuccess = { BrowserPaneAudioMuteResult.APPLIED },
                onFailure = { BrowserPaneAudioMuteResult.FAILED },
            )
            onResult(result)
        }
    }

    /**
     * Legacy key delivery kept for non-workspace callers. New pane UI must use [setAudioMuted],
     * whose result is feature-gated and truthful.
     */
    fun dispatchMuteToggle() {
        runOnUi {
            val now = SystemClock.uptimeMillis()
            webView.dispatchKeyEvent(KeyEvent(now, now, KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_MUTE, 0))
            webView.dispatchKeyEvent(KeyEvent(now, now, KeyEvent.ACTION_UP, KeyEvent.KEYCODE_MUTE, 0))
        }
    }

    /**
     * Sends a key as a matched down/up pair.
     *
     * Both halves are required: a page that only sees the press leaves the key latched, and one
     * that only sees the release ignores it. The same event time is used for both so the platform
     * does not read the gap as a long press.
     */
    private fun dispatchKey(stroke: BrowserNativeInput.KeyStroke) {
        val code = BrowserKeyCodes.androidKeyCode(stroke.key)
        val meta = BrowserKeyCodes.metaState(stroke.shift)
        val now = SystemClock.uptimeMillis()
        webView.dispatchKeyEvent(
            KeyEvent(now, now, KeyEvent.ACTION_DOWN, code, 0, meta),
        )
        webView.dispatchKeyEvent(
            KeyEvent(now, now, KeyEvent.ACTION_UP, code, 0, meta),
        )
    }

    /**
     * Types text into whatever the page has focused.
     *
     * `KeyCharacterMap` turns a string into the key events a real keyboard would have produced,
     * which is what a web input field listens for. There is no API to hand a string to a WebView
     * directly, and the alternatives — a JavaScript bridge, or reaching for the input connection —
     * are respectively a security hole this project has ruled out and undefined across Android
     * versions.
     *
     * Characters the device's key map cannot express are skipped rather than mangled.
     */
    private fun dispatchText(composed: BrowserNativeInput.ComposedText) {
        val characters = composed.text.value
        if (characters.isEmpty()) {
            return
        }
        val keyMap = KeyCharacterMap.load(KeyCharacterMap.VIRTUAL_KEYBOARD)
        val events = keyMap.getEvents(characters.toCharArray()) ?: return
        for (event in events) {
            webView.dispatchKeyEvent(event)
        }
    }

    /**
     * Places a pointer event on the page.
     *
     * Coordinates arrive already resolved to page pixels by the caller, which is the only layer
     * that knows how the host's preview maps onto this view.
     *
     * Hover moves (MOVE with no button) are mouse [ACTION_HOVER_MOVE] events, not touch MOVE —
     * touch MOVE would latch a gesture and break double-tap, but without a hover the page never
     * sees the TV cursor and `:hover` UI (video controls, menus) stays hidden until a click.
     */
    private fun dispatchPointer(pointer: BrowserNativeInput.Pointer) {
        if (pointer.action == BrowserPointerAction.MOVE && pointer.buttons == 0) {
            dispatchHover(pointer.x.toFloat(), pointer.y.toFloat())
            return
        }

        val action = when (pointer.action) {
            BrowserPointerAction.DOWN -> MotionEvent.ACTION_DOWN
            BrowserPointerAction.MOVE -> MotionEvent.ACTION_MOVE
            BrowserPointerAction.UP -> MotionEvent.ACTION_UP
            BrowserPointerAction.CANCEL -> MotionEvent.ACTION_CANCEL
        }
        val now = SystemClock.uptimeMillis()
        val downTime = when (pointer.action) {
            BrowserPointerAction.DOWN -> {
                gestureDownTime = now
                now
            }
            BrowserPointerAction.MOVE -> {
                if (gestureDownTime == 0L) {
                    gestureDownTime = now
                }
                gestureDownTime
            }
            BrowserPointerAction.UP, BrowserPointerAction.CANCEL -> {
                val started = if (gestureDownTime == 0L) now else gestureDownTime
                gestureDownTime = 0L
                started
            }
        }
        val event = MotionEvent.obtain(
            downTime,
            now,
            action,
            pointer.x.toFloat(),
            pointer.y.toFloat(),
            0,
        )
        // Declared as a touch device: WebView routes touch through the same path a finger takes, so
        // pages that only handle touch events — which on a television is most of them — respond.
        event.source = InputDevice.SOURCE_TOUCHSCREEN
        try {
            webView.dispatchTouchEvent(event)
        } finally {
            // Recycled explicitly. These come from a shared pool, and a session that leaks one per
            // pointer event exhausts it during ordinary scrolling.
            event.recycle()
        }
    }

    private fun dispatchHover(x: Float, y: Float) {
        val now = SystemClock.uptimeMillis()
        val event = MotionEvent.obtain(now, now, MotionEvent.ACTION_HOVER_MOVE, x, y, 0)
        event.source = InputDevice.SOURCE_MOUSE
        try {
            webView.dispatchGenericMotionEvent(event)
        } finally {
            event.recycle()
        }
    }

    /**
     * Scrolls the page with a short touch drag.
     *
     * Fire OS WebViews routinely ignore synthesised mouse-wheel ACTION_SCROLL events, and
     * [WebView.scrollBy] only moves the outer viewport — nested page scrollers stay put. A brief
     * touch drag is what those pages already handle for a finger on the glass.
     */
    private fun dispatchScroll(scroll: BrowserNativeInput.Scroll) {
        val downTime = SystemClock.uptimeMillis()
        val startX = scroll.x.toFloat()
        val startY = scroll.y.toFloat()
        // Host delta matches Avalonia (positive Y = scroll up). A finger swipe moves the opposite
        // way: scroll down means the touch travels upward on screen.
        val endX = startX + scroll.deltaX
        val endY = startY + scroll.deltaY
        val heldGesture = gestureDownTime
        try {
            gestureDownTime = 0L
            dispatchTouch(downTime, downTime, MotionEvent.ACTION_DOWN, startX, startY)
            dispatchTouch(downTime, downTime + 8, MotionEvent.ACTION_MOVE, endX, endY)
            dispatchTouch(downTime, downTime + 16, MotionEvent.ACTION_UP, endX, endY)
        } finally {
            gestureDownTime = heldGesture
        }
    }

    private fun dispatchTouch(downTime: Long, eventTime: Long, action: Int, x: Float, y: Float) {
        val event = MotionEvent.obtain(downTime, eventTime, action, x, y, 0)
        event.source = InputDevice.SOURCE_TOUCHSCREEN
        try {
            webView.dispatchTouchEvent(event)
        } finally {
            event.recycle()
        }
    }

    @SuppressLint("SetJavaScriptEnabled")
    private fun runOnUi(action: () -> Unit) {
        if (Looper.myLooper() == Looper.getMainLooper()) {
            action()
        } else {
            mainHandler.post(action)
        }
    }
}
