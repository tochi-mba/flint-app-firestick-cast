package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.protocol.wire.BrowserLoadState
import com.rextechnologies.flint.protocol.wire.BrowserPreviewState
import com.rextechnologies.flint.protocol.wire.BrowserStateMessage
import com.rextechnologies.flint.receiver.toViewerSentence

/**
 * Projects reducer state onto the host-facing wire snapshot.
 *
 * Without this, the desktop stays on "Nothing open" while the television is already loading —
 * state existed locally but never crossed the pinned TLS session.
 */
fun BrowserState.toWireMessage(
    viewportWidth: Int = 0,
    viewportHeight: Int = 0,
    previewState: BrowserPreviewState = BrowserPreviewState.DISABLED,
): BrowserStateMessage {
    val loadState = when (phase) {
        BrowserPhase.IDLE -> BrowserLoadState.IDLE
        BrowserPhase.OPENING, BrowserPhase.LOADING -> BrowserLoadState.LOADING
        BrowserPhase.READY -> BrowserLoadState.LOADED
        BrowserPhase.CLOSING -> BrowserLoadState.CLOSED
        BrowserPhase.ERROR -> BrowserLoadState.FAILED
    }
    return BrowserStateMessage(
        epoch = epoch ?: 0L,
        revision = revision,
        navigationId = navigationId,
        lastAcceptedCommandId = lastAcceptedCommandId,
        lastAcceptedInputSequence = lastAcceptedInputSequence,
        loadState = loadState,
        url = address?.displayUrl.orEmpty(),
        title = title?.value.orEmpty(),
        progress = progressPercent.coerceIn(0, 100),
        canGoBack = canGoBack,
        canGoForward = canGoForward,
        viewportWidth = viewportWidth.coerceAtLeast(0),
        viewportHeight = viewportHeight.coerceAtLeast(0),
        previewState = previewState,
        errorDetail = failure?.toViewerSentence().orEmpty(),
    )
}
