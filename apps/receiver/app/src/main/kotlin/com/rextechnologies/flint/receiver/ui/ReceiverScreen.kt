package com.rextechnologies.flint.receiver.ui
import androidx.compose.ui.Alignment

import android.view.KeyEvent
import android.view.SurfaceHolder
import android.view.SurfaceView
import android.view.ViewGroup
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.viewinterop.AndroidView
import androidx.media3.common.util.UnstableApi
import androidx.media3.ui.AspectRatioFrameLayout
import androidx.media3.ui.PlayerView
import com.rextechnologies.flint.protocol.wire.SurfaceMode
import com.rextechnologies.flint.receiver.ReceiverService
import com.rextechnologies.flint.receiver.ReceiverUiState
import kotlinx.coroutines.flow.collectLatest

@androidx.annotation.OptIn(UnstableApi::class)
@Composable
fun ReceiverScreen(service: ReceiverService?) {
    var state by remember { mutableStateOf(ReceiverUiState()) }
    LaunchedEffect(service) {
        if (service == null) {
            state = ReceiverUiState()
        } else {
            service.uiState.collectLatest { state = it }
        }
    }

    ReceiverSurface(
        state = state,
        onRefreshCode = { service?.refreshPairingCode() },
        onRetry = { service?.retryConnection() },
        onBrowse = { service?.openBrowserFromTv() },
        onTogglePlayPause = { service?.dispatchTvKey(KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE) },
        onClosePlayer = { service?.dispatchTvKey(KeyEvent.KEYCODE_BACK) },
        onStopMirroring = { service?.dispatchTvKey(KeyEvent.KEYCODE_BACK) },
        playerContent = { service?.let { ReceiverPlayerView(it) } },
        mirrorContent = {
            service?.let { ReceiverMirrorView(it, state.mirrorWidth, state.mirrorHeight) }
        },
        browserContent = {
            if (state.surfaceMode == SurfaceMode.BROWSER) {
                ReceiverBrowserSurface(service, state)
            }
        },
    )
}

/** Pure receiver composition used by device tests to render every state deterministically. */
@Composable
internal fun ReceiverSurface(
    state: ReceiverUiState,
    onRefreshCode: () -> Unit = {},
    onRetry: () -> Unit = {},
    onBrowse: () -> Unit = {},
    onTogglePlayPause: () -> Unit = {},
    onClosePlayer: () -> Unit = {},
    onStopMirroring: () -> Unit = {},
    playerContent: @Composable () -> Unit = {},
    mirrorContent: @Composable () -> Unit = {},
    browserContent: @Composable () -> Unit = {},
    playbackHudAutoHideMillis: Long? = 4_000L,
    mirrorControlsAutoHideMillis: Long? = 6_000L,
    initialMirrorControlsVisible: Boolean = false,
    stateMarker: String? = null,
) {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(ReceiverColors.Ink)
            .testTag(ReceiverTags.ROOT)
            .semantics {
                stateMarker?.let { contentDescription = it }
            },
    ) {
        ReceiverBackdrop(Modifier.fillMaxSize())
        when (state.surfaceMode) {
            SurfaceMode.IDLE -> ReceiverIdleSurface(state, onRefreshCode, onRetry, onBrowse)
            SurfaceMode.PLAYER -> ReceiverPlaybackSurface(
                state,
                playerContent,
                playbackHudAutoHideMillis,
                onTogglePlayPause,
                onClosePlayer,
            )
            SurfaceMode.MIRROR, SurfaceMode.PRESENTATION -> ReceiverMirrorSurface(
                state = state,
                presentation = state.surfaceMode == SurfaceMode.PRESENTATION,
                mirrorContent = mirrorContent,
                onStopMirroring = onStopMirroring,
                controlsAutoHideMillis = mirrorControlsAutoHideMillis,
                initialControlsVisible = initialMirrorControlsVisible,
            )
            SurfaceMode.BROWSER -> browserContent()
        }
        if (state.surfaceMode == SurfaceMode.IDLE) {
            Box(Modifier.fillMaxSize(ReceiverOverscan.CONTENT_FRACTION).align(Alignment.Center)) {
                ReceiverHelp(Modifier.align(Alignment.BottomEnd))
            }
        }
    }
}

@androidx.annotation.OptIn(UnstableApi::class)
@Composable
private fun ReceiverPlayerView(service: ReceiverService) {
    AndroidView(
        factory = { context ->
            PlayerView(context).apply {
                useController = false
                resizeMode = AspectRatioFrameLayout.RESIZE_MODE_FIT
                setShowBuffering(PlayerView.SHOW_BUFFERING_NEVER)
                keepScreenOn = true
                player = service.player()
            }
        },
        update = { it.player = service.player() },
        modifier = Modifier.fillMaxSize(),
    )
}

@Composable
@androidx.annotation.OptIn(UnstableApi::class)
private fun ReceiverMirrorView(service: ReceiverService, width: Int, height: Int) {
    DisposableEffect(service) {
        onDispose { service.attachMirrorSurface(null) }
    }
    AndroidView(
        factory = { context ->
            AspectRatioFrameLayout(context).apply {
                resizeMode = AspectRatioFrameLayout.RESIZE_MODE_FIT
                SurfaceView(context).also { surfaceView ->
                    surfaceView.keepScreenOn = true
                    surfaceView.holder.addCallback(object : SurfaceHolder.Callback {
                        override fun surfaceCreated(holder: SurfaceHolder) {
                            service.attachMirrorSurface(holder.surface)
                        }

                        override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {
                            service.attachMirrorSurface(holder.surface)
                        }

                        override fun surfaceDestroyed(holder: SurfaceHolder) {
                            service.attachMirrorSurface(null)
                        }
                    })
                    addView(
                        surfaceView,
                        ViewGroup.LayoutParams(
                            ViewGroup.LayoutParams.MATCH_PARENT,
                            ViewGroup.LayoutParams.MATCH_PARENT,
                        ),
                    )
                }
            }
        },
        update = { frame ->
            frame.setAspectRatio(if (width > 0 && height > 0) width.toFloat() / height else 16f / 9f)
        },
        modifier = Modifier.fillMaxSize().background(Color.Black),
    )
}
