package com.rextechnologies.flint.receiver.browser

import android.graphics.Bitmap
import android.net.Uri
import android.os.Message
import android.view.View
import android.webkit.GeolocationPermissions
import android.webkit.JsPromptResult
import android.webkit.JsResult
import android.webkit.PermissionRequest
import android.webkit.ValueCallback
import android.webkit.WebChromeClient
import android.webkit.WebView

/**
 * Everything the page asks of its embedder: dialogs, fullscreen, favicons, and the requests this
 * browser refuses.
 *
 * The WebView keeps the [JsResult] until the dialog reducer resolves it. Nothing is confirmed by
 * default: Cancel is the safe answer when the host disconnects, the dialog expires, or the surface
 * closes.
 *
 * Every refusal is reported rather than dropped. A popup, an upload chooser or a camera request
 * that silently does nothing is indistinguishable from a broken browser; naming what was refused is
 * the difference between a policy and a bug.
 */
class BrowserChromeClient(
    private val onDialog: (PendingJsDialog) -> Unit,
    private val chrome: WebChromeClient = WebChromeClient(),
    private val fullscreen: BrowserCustomViewController<View>? = null,
    private val onFavicon: (Bitmap) -> Unit = {},
    private val onRefused: (BrowserRefusal) -> Unit = {},
) : WebChromeClient() {
    override fun onProgressChanged(view: WebView, newProgress: Int) =
        chrome.onProgressChanged(view, newProgress)

    override fun onReceivedTitle(view: WebView, title: String?) =
        chrome.onReceivedTitle(view, title)

    override fun onReceivedIcon(view: WebView, icon: Bitmap?) {
        icon?.let(onFavicon)
    }

    /**
     * The page wants the whole screen — a video going fullscreen, almost always.
     *
     * Nothing implemented this pair before, which is why fullscreen buttons across every video site
     * appeared to do nothing at all.
     */
    override fun onShowCustomView(view: View?, callback: CustomViewCallback?) {
        if (view == null) {
            callback?.onCustomViewHidden()
            return
        }
        val controller = fullscreen
        if (controller == null || !controller.enter(view) { callback?.onCustomViewHidden() }) {
            // Refused rather than left half-entered: telling the page immediately lets it put its
            // own inline player back instead of waiting for a fullscreen that will never arrive.
            callback?.onCustomViewHidden()
        }
    }

    override fun onHideCustomView() {
        fullscreen?.exit()
    }

    /**
     * A second window. Denied by policy, and reported so the viewer knows why a link did nothing.
     */
    override fun onCreateWindow(
        view: WebView,
        isDialog: Boolean,
        isUserGesture: Boolean,
        resultMsg: Message?,
    ): Boolean {
        onRefused(BrowserRefusal.POPUP)
        return false
    }

    override fun onPermissionRequest(request: PermissionRequest?) {
        onRefused(BrowserRefusal.PERMISSION)
        request?.deny()
    }

    override fun onGeolocationPermissionsShowPrompt(
        origin: String?,
        callback: GeolocationPermissions.Callback?,
    ) {
        onRefused(BrowserRefusal.LOCATION)
        callback?.invoke(origin, false, false)
    }

    override fun onShowFileChooser(
        webView: WebView,
        filePathCallback: ValueCallback<Array<Uri>>?,
        fileChooserParams: FileChooserParams?,
    ): Boolean {
        onRefused(BrowserRefusal.UPLOAD)
        // Answered with nothing rather than left hanging: an unanswered chooser callback wedges the
        // page's file input for the rest of the session.
        filePathCallback?.onReceiveValue(null)
        return true
    }

    override fun onJsAlert(view: WebView, url: String?, message: String?, result: JsResult): Boolean {
        onDialog(
            PendingJsDialog(
                kind = BrowserDialogKind.ALERT,
                originUrl = url.orEmpty(),
                message = message.orEmpty(),
                defaultValue = null,
                resolve = { answer ->
                    if (answer is BrowserDialogAnswer.Cancel) result.cancel() else result.confirm()
                },
            ),
        )
        return true
    }

    override fun onJsConfirm(view: WebView, url: String?, message: String?, result: JsResult): Boolean {
        onDialog(
            PendingJsDialog(
                kind = BrowserDialogKind.CONFIRM,
                originUrl = url.orEmpty(),
                message = message.orEmpty(),
                defaultValue = null,
                resolve = { answer ->
                    if (answer is BrowserDialogAnswer.Confirm) result.confirm() else result.cancel()
                },
            ),
        )
        return true
    }

    override fun onJsPrompt(
        view: WebView,
        url: String?,
        message: String?,
        defaultValue: String?,
        result: JsPromptResult,
    ): Boolean {
        onDialog(
            PendingJsDialog(
                kind = BrowserDialogKind.PROMPT,
                originUrl = url.orEmpty(),
                message = message.orEmpty(),
                defaultValue = defaultValue,
                resolve = { answer ->
                    when (answer) {
                        is BrowserDialogAnswer.Prompt -> result.confirm(answer.text)
                        BrowserDialogAnswer.Confirm -> result.confirm(defaultValue.orEmpty())
                        BrowserDialogAnswer.Cancel -> result.cancel()
                    }
                },
            ),
        )
        return true
    }

    override fun onJsBeforeUnload(view: WebView, url: String?, message: String?, result: JsResult): Boolean {
        onDialog(
            PendingJsDialog(
                kind = BrowserDialogKind.BEFORE_UNLOAD,
                originUrl = url.orEmpty(),
                message = message.orEmpty(),
                defaultValue = null,
                resolve = { answer ->
                    // Before-unload defaults to stay (cancel navigation) unless explicitly confirmed.
                    if (answer is BrowserDialogAnswer.Confirm) result.confirm() else result.cancel()
                },
            ),
        )
        return true
    }
}

/** A page dialog waiting for the TV or the pinned host to answer. */
data class PendingJsDialog(
    val kind: BrowserDialogKind,
    val originUrl: String,
    val message: String,
    val defaultValue: String?,
    val resolve: (BrowserDialogAnswer) -> Unit,
)

/**
 * Something the page asked for that this browser does not do.
 *
 * Each value is a sentence the television can show. The policy behind them is ADR-0006; what this
 * type adds is that the viewer finds out, instead of watching a link do nothing.
 */
enum class BrowserRefusal(val viewerSentence: String) {
    POPUP("That link wanted a new window, which this browser does not open."),
    UPLOAD("This page asked to upload a file. Uploads are not available here."),
    DOWNLOAD("Downloads are not available in this browser."),
    PERMISSION("This page asked for the camera or microphone, which is not granted here."),
    LOCATION("This page asked for your location, which is not shared."),
    EXTERNAL_APP("That link opens another app, which this browser does not launch."),
    BLOCKED_ADDRESS("Blocked — this browser only opens secure https addresses."),
}
