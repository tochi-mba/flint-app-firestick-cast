package com.rextechnologies.flint.receiver

import android.os.Bundle
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.tv.material3.Text
import com.rextechnologies.flint.protocol.wire.PlaybackState
import com.rextechnologies.flint.protocol.wire.SurfaceMode
import com.rextechnologies.flint.receiver.ui.ReceiverColors
import com.rextechnologies.flint.receiver.ui.ReceiverSurface
import com.rextechnologies.flint.receiver.ui.ReceiverTheme

/** Debug-only gallery that renders receiver states without networking or a media decoder. */
class ReceiverPreviewActivity : ComponentActivity() {
    private var previewState by mutableStateOf(ReceiverUiState())
    private lateinit var previewName: String

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        enableEdgeToEdge()
        previewName = intent.getStringExtra(EXTRA_STATE).orEmpty()
        previewState = receiverPreviewState(previewName)
        setContent {
            ReceiverTheme {
                ReceiverSurface(
                    state = previewState,
                    onRefreshCode = {
                        previewState = previewState.copy(
                            pairingCode = if (previewState.pairingCode == "004283") "888888" else "004283",
                        )
                    },
                    onRetry = {
                        previewState = receiverPreviewState(PREVIEW_READY).copy(detail = "Receiver recovered")
                    },
                    onTogglePlayPause = {
                        previewState = previewState.copy(
                            playbackState = if (previewState.playbackState == PlaybackState.PLAYING) {
                                PlaybackState.PAUSED
                            } else {
                                PlaybackState.PLAYING
                            },
                        )
                    },
                    onClosePlayer = {
                        previewState = receiverPreviewState(PREVIEW_CONNECTED)
                    },
                    onBrowse = {
                        previewName = PREVIEW_BROWSER
                        previewState = receiverPreviewState(PREVIEW_BROWSER)
                    },
                    browserContent = {
                        if (previewState.surfaceMode == SurfaceMode.BROWSER) {
                            ReceiverBrowserHarness(
                                marker = "Receiver preview $previewName",
                                start = browserHarnessStart(previewName),
                            )
                        }
                    },
                    playerContent = { PreviewMediaBackdrop() },
                    mirrorContent = {
                        PreviewMirrorBackdrop(previewState.mirrorWidth, previewState.mirrorHeight)
                    },
                    playbackHudAutoHideMillis = null,
                    stateMarker = "Receiver preview $previewName",
                )
            }
        }
    }

    companion object {
        const val EXTRA_STATE = "preview"
        const val PREVIEW_STARTING = "starting"
        const val PREVIEW_NO_NETWORK = "no-network"
        const val PREVIEW_READY = "ready"
        const val PREVIEW_READY_WIDE = "ready-wide"
        const val PREVIEW_CONNECTED = "connected"
        const val PREVIEW_ATTENTION = "attention"
        const val PREVIEW_BUFFERING = "buffering"
        const val PREVIEW_PLAYING = "playing"
        const val PREVIEW_PAUSED = "paused"
        const val PREVIEW_PLAYBACK_ERROR = "playback-error"
        const val PREVIEW_MIRROR_WAITING = "mirror-waiting"
        const val PREVIEW_MIRROR_ACTIVE = "mirror-active"
        const val PREVIEW_MIRROR_ERROR = "mirror-error"
        const val PREVIEW_PRESENTATION = "presentation"
        const val PREVIEW_PRESENTATION_ACTIVE = "presentation-active"
        const val PREVIEW_BROWSER = "browser"
        const val PREVIEW_BROWSER_CHROME = "browser-chrome"
        const val PREVIEW_BROWSER_MENU = "browser-menu"
        const val PREVIEW_BROWSER_TABS = "browser-tabs"
        const val PREVIEW_BROWSER_LEAVE = "browser-leave"
        const val PREVIEW_BROWSER_OMNIBOX = "browser-omnibox"
        const val PREVIEW_BROWSER_FIND = "browser-find"
        const val PREVIEW_BROWSER_BOOKMARKS = "browser-bookmarks"
        const val PREVIEW_BROWSER_HISTORY = "browser-history"
        const val PREVIEW_BROWSER_CLEAR = "browser-clear"
        const val PREVIEW_BROWSER_PROFILES = "browser-profiles"
    }
}

internal fun receiverPreviewState(name: String): ReceiverUiState {
    val ready = ReceiverUiState(
        networkState = ReceiverNetworkState.LISTENING,
        pairingCode = "004283",
        address = "10.221.159.172",
        port = 47855,
        browserPort = 33164,
        browserFingerprint = "BA35-6AA0-4067",
        detail = "Ready on the local network",
    )
    val connected = ready.copy(
        peerName = "TOCHUKWU'S WINDOWS PC",
        detail = "Connected to TOCHUKWU'S WINDOWS PC",
    )
    return when (name) {
        ReceiverPreviewActivity.PREVIEW_NO_NETWORK -> ReceiverUiState(
            networkState = ReceiverNetworkState.WAITING,
            pairingCode = "004283",
            detail = "Connect this TV and your PC to the same network",
        )
        ReceiverPreviewActivity.PREVIEW_READY -> ready
        ReceiverPreviewActivity.PREVIEW_READY_WIDE -> ready.copy(
            pairingCode = "888888",
            address = "255.255.255.255",
            port = 65535,
        )
        ReceiverPreviewActivity.PREVIEW_CONNECTED -> connected.copy(
            peerName = "A VERY LONG WINDOWS COMPUTER NAME THAT MUST NEVER BREAK THIS TELEVISION LAYOUT",
        )
        ReceiverPreviewActivity.PREVIEW_ATTENTION -> ready.copy(
            error = RECEIVER_START_FAILURE_MESSAGE,
            detail = RECEIVER_START_FAILURE_MESSAGE,
        )
        ReceiverPreviewActivity.PREVIEW_BUFFERING -> connected.copy(
            surfaceMode = SurfaceMode.PLAYER,
            title = "new_Adobe 2026_VPro_blade3_1160x870.mp4",
            playbackState = PlaybackState.BUFFERING,
            detail = "Buffering",
        )
        ReceiverPreviewActivity.PREVIEW_PLAYING -> connected.copy(
            surfaceMode = SurfaceMode.PLAYER,
            title = "A deliberately long movie title that must remain inside the television safe area.mp4",
            playbackState = PlaybackState.PLAYING,
            positionMs = 3_754_000,
            durationMs = 7_508_000,
            detail = "Playing on this TV",
        )
        ReceiverPreviewActivity.PREVIEW_PAUSED -> connected.copy(
            surfaceMode = SurfaceMode.PLAYER,
            title = "Paused media",
            playbackState = PlaybackState.PAUSED,
            positionMs = 61_000,
            durationMs = 125_000,
            detail = "Paused",
        )
        ReceiverPreviewActivity.PREVIEW_PLAYBACK_ERROR -> connected.copy(
            surfaceMode = SurfaceMode.PLAYER,
            title = "Media that could not be played",
            playbackState = PlaybackState.ERROR,
            error = friendlyPlaybackError("ERROR_CODE_IO_NETWORK_CONNECTION_FAILED"),
        )
        ReceiverPreviewActivity.PREVIEW_MIRROR_WAITING -> connected.copy(
            surfaceMode = SurfaceMode.MIRROR,
            title = "Laptop display",
            mirrorWidth = 2560,
            mirrorHeight = 1600,
            mirrorFrameReceived = false,
        )
        ReceiverPreviewActivity.PREVIEW_MIRROR_ACTIVE -> connected.copy(
            surfaceMode = SurfaceMode.MIRROR,
            title = "Laptop display",
            mirrorWidth = 2560,
            mirrorHeight = 1600,
            mirrorFrameReceived = true,
        )
        ReceiverPreviewActivity.PREVIEW_MIRROR_ERROR -> connected.copy(
            surfaceMode = SurfaceMode.MIRROR,
            title = "Laptop display",
            error = "The incoming video stream stopped. Try screen mirroring again from Flint.",
        )
        ReceiverPreviewActivity.PREVIEW_PRESENTATION -> connected.copy(
            surfaceMode = SurfaceMode.PRESENTATION,
            title = "Extended desktop",
            mirrorWidth = 1920,
            mirrorHeight = 1080,
        )
        ReceiverPreviewActivity.PREVIEW_PRESENTATION_ACTIVE -> connected.copy(
            surfaceMode = SurfaceMode.PRESENTATION,
            title = "Extended desktop",
            mirrorWidth = 1920,
            mirrorHeight = 1080,
            mirrorFrameReceived = true,
        )
        ReceiverPreviewActivity.PREVIEW_BROWSER,
        ReceiverPreviewActivity.PREVIEW_BROWSER_CHROME,
        ReceiverPreviewActivity.PREVIEW_BROWSER_MENU,
        ReceiverPreviewActivity.PREVIEW_BROWSER_TABS,
        ReceiverPreviewActivity.PREVIEW_BROWSER_LEAVE,
        ReceiverPreviewActivity.PREVIEW_BROWSER_OMNIBOX,
        ReceiverPreviewActivity.PREVIEW_BROWSER_FIND,
        ReceiverPreviewActivity.PREVIEW_BROWSER_BOOKMARKS,
        ReceiverPreviewActivity.PREVIEW_BROWSER_HISTORY,
        ReceiverPreviewActivity.PREVIEW_BROWSER_CLEAR,
        ReceiverPreviewActivity.PREVIEW_BROWSER_PROFILES,
        -> ready.copy(
            surfaceMode = SurfaceMode.BROWSER,
            title = "Harness tab",
            detail = "Browser harness",
        )
        else -> ReceiverUiState()
    }
}

@androidx.compose.runtime.Composable
private fun PreviewMediaBackdrop() {
    Box(
        Modifier
            .fillMaxSize()
            .semantics {
                contentDescription = "Preview media backdrop"
            }
            .background(
                Brush.linearGradient(
                    listOf(Color(0xFF101A24), Color(0xFF182B22), Color(0xFF080A09)),
                ),
            ),
    )
}

@androidx.compose.runtime.Composable
private fun PreviewMirrorBackdrop(width: Int, height: Int) {
    Box(
        Modifier
            .fillMaxSize()
            .semantics {
                // The device test measures this node's bounds to prove the mirror frame keeps the
                // source aspect ratio and stays centered, so it needs the dimensions it was asked
                // to render, not a generic label.
                contentDescription = "Preview frame $width by $height"
            }
            .background(
                Brush.linearGradient(
                    listOf(ReceiverColors.Raised, Color(0xFF23303A), ReceiverColors.Ink),
                ),
            ),
        contentAlignment = Alignment.Center,
    ) {
        Canvas(Modifier.fillMaxSize()) {
            val columns = 8
            val rows = 5
            val palette = listOf(
                Color(0xFF173B45),
                Color(0xFF27364E),
                Color(0xFF37412D),
                Color(0xFF4A332C),
            )
            val cellWidth = size.width / columns
            val cellHeight = size.height / rows
            repeat(rows) { row ->
                repeat(columns) { column ->
                    drawRect(
                        color = palette[(row + column) % palette.size],
                        topLeft = androidx.compose.ui.geometry.Offset(column * cellWidth, row * cellHeight),
                        size = androidx.compose.ui.geometry.Size(cellWidth, cellHeight),
                    )
                }
            }
            drawLine(
                color = ReceiverColors.Signal,
                start = androidx.compose.ui.geometry.Offset.Zero,
                end = androidx.compose.ui.geometry.Offset(size.width, size.height),
                strokeWidth = 4.dp.toPx(),
                cap = StrokeCap.Round,
            )
            drawLine(
                color = ReceiverColors.Signal,
                start = androidx.compose.ui.geometry.Offset(size.width, 0f),
                end = androidx.compose.ui.geometry.Offset(0f, size.height),
                strokeWidth = 4.dp.toPx(),
                cap = StrokeCap.Round,
            )
            drawCircle(
                color = Color.White,
                radius = size.minDimension * 0.18f,
                style = Stroke(width = 5.dp.toPx()),
            )
        }
        val ratio = if (width > 0 && height > 0) "$width x $height" else "16:9"
        Text(
            text = "$ratio  TEST FRAME",
            color = Color.White,
            fontSize = 18.sp,
            lineHeight = 22.sp,
            fontWeight = FontWeight.Bold,
            modifier = Modifier
                .background(Color.Black.copy(alpha = 0.78f))
                .padding(horizontal = 18.dp, vertical = 11.dp),
        )
    }
}

internal fun browserHarnessStart(name: String): BrowserHarnessStart = when (name) {
    ReceiverPreviewActivity.PREVIEW_BROWSER_CHROME -> BrowserHarnessStart.CHROME
    ReceiverPreviewActivity.PREVIEW_BROWSER_MENU -> BrowserHarnessStart.MENU
    ReceiverPreviewActivity.PREVIEW_BROWSER_TABS -> BrowserHarnessStart.TABS
    ReceiverPreviewActivity.PREVIEW_BROWSER_LEAVE -> BrowserHarnessStart.LEAVE
    ReceiverPreviewActivity.PREVIEW_BROWSER_OMNIBOX -> BrowserHarnessStart.OMNIBOX
    ReceiverPreviewActivity.PREVIEW_BROWSER_FIND -> BrowserHarnessStart.FIND
    ReceiverPreviewActivity.PREVIEW_BROWSER_BOOKMARKS -> BrowserHarnessStart.BOOKMARKS
    ReceiverPreviewActivity.PREVIEW_BROWSER_HISTORY -> BrowserHarnessStart.HISTORY
    ReceiverPreviewActivity.PREVIEW_BROWSER_CLEAR -> BrowserHarnessStart.CLEAR_DATA
    ReceiverPreviewActivity.PREVIEW_BROWSER_PROFILES -> BrowserHarnessStart.PROFILES
    else -> BrowserHarnessStart.PAGE
}
