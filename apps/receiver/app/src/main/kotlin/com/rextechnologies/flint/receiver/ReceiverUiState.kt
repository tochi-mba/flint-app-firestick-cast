package com.rextechnologies.flint.receiver

import com.rextechnologies.flint.protocol.wire.PlaybackState
import com.rextechnologies.flint.protocol.wire.SurfaceMode
import com.rextechnologies.flint.receiver.net.ReceiverServer
import com.rextechnologies.flint.receiver.browser.BrowserLibraryState
import com.rextechnologies.flint.receiver.browser.BrowserProfilesUiState
import com.rextechnologies.flint.receiver.browser.BrowserViewState

enum class ReceiverNetworkState {
    STARTING,
    WAITING,
    LISTENING,
}

data class ReceiverUiState(
    val networkState: ReceiverNetworkState = ReceiverNetworkState.STARTING,
    val pairingCode: String = "------",
    val address: String? = null,
    val port: Int = ReceiverServer.DEFAULT_PORT,
    val browserPort: Int = 0,
    val browserFingerprint: String? = null,
    val peerName: String? = null,
    val surfaceMode: SurfaceMode = SurfaceMode.IDLE,
    val title: String = "",
    val playbackState: PlaybackState = PlaybackState.IDLE,
    val positionMs: Long = 0,
    val durationMs: Long = -1,
    val mirrorWidth: Int = 0,
    val mirrorHeight: Int = 0,
    val mirrorFrameReceived: Boolean = false,
    val browser: BrowserSurfaceUi = BrowserSurfaceUi(),
    /** A workspace replaces the tab surface; it never runs additional hidden tab renderers. */
    val browserWorkspaceVisible: Boolean = false,
    /** Host touchpad aim point drawn over the WebView so a sofa viewer can see the click target. */
    val browserCursor: BrowserCursorUi = BrowserCursorUi(),
    /** A short message about something the browser refused to do, or null when there is none. */
    val browserNotice: BrowserNoticeUi? = null,
    /** Whether the page is currently filling the screen at its own request. */
    val browserFullscreen: Boolean = false,
    val browserView: BrowserViewState = BrowserViewState(),
    val browserLibrary: BrowserLibraryState = BrowserLibraryState(),
    /** Named TV profiles plus the optional authenticated device projection. */
    val browserProfiles: BrowserProfilesUiState = BrowserProfilesUiState(),
    val detail: String = "Starting receiver",
    val error: String? = null,
) {
    val connected: Boolean get() = !peerName.isNullOrBlank()
    val ready: Boolean get() = networkState == ReceiverNetworkState.LISTENING && address != null && error == null
    val browserSecureReady: Boolean get() = browserPort > 0 && !browserFingerprint.isNullOrBlank()
}

/** Where the Windows touchpad is aiming on the television page. */
data class BrowserCursorUi(
    val visible: Boolean = false,
    /** Horizontal aim as a fraction of the WebView width, 0 at left and 1 at right. */
    val xFraction: Float = 0.5f,
    /** Vertical aim as a fraction of the WebView height, 0 at top and 1 at bottom. */
    val yFraction: Float = 0.5f,
    /** Whether the primary button is currently held. */
    val pressed: Boolean = false,
)

/**
 * One short refusal message on the television.
 *
 * Carries an id so the surface can tell a replacement from a repeat and restart its own timer
 * without the reducer having to know about frames.
 */
data class BrowserNoticeUi(
    val id: Long,
    val message: String,
)
