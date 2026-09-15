package com.rextechnologies.flint.receiver

import com.rextechnologies.flint.receiver.browser.BrowserFailure
import com.rextechnologies.flint.receiver.browser.BrowserPhase
import com.rextechnologies.flint.receiver.browser.BrowserState
import com.rextechnologies.flint.receiver.browser.BrowserTab

/**
 * What the television shows about the page it is browsing.
 *
 * A projection of [BrowserState] rather than the state itself. The reducer's state carries epochs,
 * command identifiers and input sequence numbers, none of which mean anything on a screen, and
 * handing the whole thing to a composable makes every one of those fields a recomposition trigger
 * for chrome that could not possibly have changed.
 *
 * Failure is a sentence here rather than an enum. The mapping belongs on this side because the
 * receiver is the last place that knows what the codes mean, and a viewer standing at a television
 * cannot look up `BLOCKED_URL`.
 */
data class BrowserSurfaceUi(
    val url: String = "",
    val title: String = "",
    val progressPercent: Int = 0,
    val isLoading: Boolean = false,
    val failure: String? = null,
    /** Whether a history entry exists behind this page, so Back can show its real state. */
    val canGoBack: Boolean = false,
    /** Whether a history entry exists ahead of this page. */
    val canGoForward: Boolean = false,
    /** Open pages in stable strip order. Renderer ownership remains outside Compose. */
    val tabs: List<BrowserTab> = emptyList(),
    val activeTabId: Long = 0,
    val dialog: BrowserDialogUi? = null,
)

/** One active native page dialog shown on the television. */
data class BrowserDialogUi(
    val dialogId: Long,
    val origin: String,
    val message: String,
    val kind: String,
    val defaultValue: String? = null,
)

/** Reduces the browser's full state to the part a viewer can see. */
fun BrowserState.toSurfaceUi(): BrowserSurfaceUi = BrowserSurfaceUi(
    url = address?.displayUrl.orEmpty(),
    title = title?.value.orEmpty(),
    // Clamped here rather than at the point of drawing: the value originates in a WebView's own
    // progress callback, and a bar is not the only thing that will ever read it.
    progressPercent = progressPercent.coerceIn(0, 100),
    isLoading = phase == BrowserPhase.OPENING || phase == BrowserPhase.LOADING,
    failure = failure?.toViewerSentence(),
    canGoBack = canGoBack,
    canGoForward = canGoForward,
)

/**
 * A failure as something a person across a room can act on.
 *
 * Each one answers "what do I do now", because a code that only names the fault leaves the viewer
 * looking at a television with no next step.
 */
internal fun BrowserFailure.toViewerSentence(): String = when (this) {
    BrowserFailure.BLOCKED_URL -> "That address was blocked. This browser only opens secure https sites."
    BrowserFailure.SITE_NOT_FOUND -> "That site could not be found. Check the address and try again."
    BrowserFailure.NO_CONNECTION -> "That site could not be reached. Check the network and try again."
    BrowserFailure.SITE_ERROR -> "The site returned an error. It may be down for everyone."
    BrowserFailure.CERTIFICATE_REJECTED -> "This site's security certificate could not be trusted."
    BrowserFailure.POPUP_DENIED -> "The page tried to open a second window, which is not allowed here."
    BrowserFailure.PERMISSION_DENIED -> "The page asked for a permission this browser does not grant."
    BrowserFailure.RENDERER_STOPPED -> "The page stopped responding. Reload it from the desktop."
    BrowserFailure.DRIVER_FAILURE -> "The browser could not start. Close it and open it again."
    BrowserFailure.FEATURE_NOT_ENABLED -> "Secure browsing is not enabled on this receiver."
}
