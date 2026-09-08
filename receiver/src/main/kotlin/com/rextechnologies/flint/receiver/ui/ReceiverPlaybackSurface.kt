package com.rextechnologies.flint.receiver.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.sp
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.tv.material3.Text
import com.rextechnologies.flint.protocol.wire.PlaybackState
import com.rextechnologies.flint.receiver.ReceiverUiState
import kotlinx.coroutines.delay

@Composable
internal fun ReceiverPlaybackSurface(
    state: ReceiverUiState,
    playerContent: @Composable () -> Unit,
    hudAutoHideMillis: Long? = 4_000L,
    onTogglePlayPause: () -> Unit = {},
    onClose: () -> Unit = {},
) {
    Box(Modifier.fillMaxSize().background(Color.Black)) {
        playerContent()
        when {
            state.error != null || state.playbackState == PlaybackState.ERROR -> ReceiverFailureOverlay(
                state = state,
                eyebrow = "PLAYBACK STOPPED",
                instruction = "Press BACK to close playback",
                tag = ReceiverTags.PLAYBACK_ERROR,
            )
            state.playbackState == PlaybackState.BUFFERING || state.playbackState == PlaybackState.IDLE ->
                PlaybackLoadingOverlay(state)
            else -> PlaybackHud(state, hudAutoHideMillis, onTogglePlayPause, onClose)
        }
    }
}

@Composable
private fun PlaybackLoadingOverlay(state: ReceiverUiState) {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(ReceiverColors.Scrim),
        contentAlignment = Alignment.Center,
    ) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            ReceiverSpinner(Modifier.size(42.dp))
            Text(
                text = "GETTING IT READY",
                color = ReceiverColors.Signal,
                fontSize = 12.sp,
                lineHeight = 15.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = 1.8.sp,
                modifier = Modifier.padding(top = 22.dp),
            )
            Text(
                text = displayTitle(state.title),
                color = ReceiverColors.Text,
                fontSize = 28.sp,
                lineHeight = 34.sp,
                fontWeight = FontWeight.Bold,
                textAlign = TextAlign.Center,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.fillMaxWidth(0.8f).padding(top = 8.dp),
            )
            Text(
                text = "Connecting to your PC…",
                color = ReceiverColors.Muted,
                fontSize = 15.sp,
                lineHeight = 20.sp,
                modifier = Modifier.padding(top = 9.dp),
            )
        }
    }
}

@Composable
internal fun ReceiverFailureOverlay(
    state: ReceiverUiState,
    eyebrow: String,
    instruction: String,
    tag: String,
) {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(ReceiverColors.Scrim)
            .testTag(tag)
            .clearAndSetSemantics {
                contentDescription = "$eyebrow. ${state.error ?: "Playback stopped unexpectedly."}. $instruction"
                liveRegion = LiveRegionMode.Assertive
            },
        contentAlignment = Alignment.Center,
    ) {
        val shape = RoundedCornerShape(22.dp)
        Column(
            modifier = Modifier
                .fillMaxWidth(0.56f)
                .background(ReceiverColors.Panel, shape)
                .border(1.dp, ReceiverColors.Live.copy(alpha = 0.75f), shape)
                .padding(horizontal = 34.dp, vertical = 30.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Box(
                Modifier
                    .size(46.dp)
                    .background(ReceiverColors.Live.copy(alpha = 0.10f), CircleShape),
                contentAlignment = Alignment.Center,
            ) {
                Text("!", color = ReceiverColors.Live, fontSize = 24.sp, fontWeight = FontWeight.Black)
            }
            Text(
                text = eyebrow,
                color = ReceiverColors.Live,
                fontSize = 12.sp,
                lineHeight = 15.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = 1.8.sp,
                modifier = Modifier.padding(top = 16.dp),
            )
            Text(
                text = state.error ?: "Playback stopped unexpectedly.",
                color = ReceiverColors.Text,
                fontSize = 18.sp,
                lineHeight = 26.sp,
                maxLines = 4,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.padding(top = 10.dp),
            )
            Text(
                text = instruction,
                color = ReceiverColors.Muted,
                fontSize = 13.sp,
                lineHeight = 18.sp,
                modifier = Modifier.padding(top = 18.dp),
            )
        }
    }
}

@Composable
private fun PlaybackHud(
    state: ReceiverUiState,
    autoHideMillis: Long?,
    onTogglePlayPause: () -> Unit,
    onClose: () -> Unit,
) {
    var visible by remember(state.title) { mutableStateOf(true) }
    LaunchedEffect(state.title, state.playbackState) {
        visible = true
        if (state.playbackState == PlaybackState.PLAYING && autoHideMillis != null) {
            delay(autoHideMillis)
            visible = false
        }
    }
    if (!visible) return

    BoxWithConstraints(Modifier.fillMaxSize().testTag(ReceiverTags.PLAYBACK_HUD)) {
        val horizontalSafeArea = maxWidth * 0.05f
        val verticalSafeArea = maxHeight * 0.05f
        Column(
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .fillMaxWidth()
                .background(
                    Brush.verticalGradient(
                        colors = listOf(Color.Transparent, ReceiverColors.Ink.copy(alpha = 0.96f)),
                    ),
                )
                .padding(
                    start = horizontalSafeArea,
                    end = horizontalSafeArea,
                    top = 56.dp,
                    bottom = verticalSafeArea,
                ),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                PlaybackStatePill(state.playbackState)
                Spacer(Modifier.width(12.dp))
                Text(
                    text = displayTitle(state.title, "Flint media"),
                    color = ReceiverColors.Text,
                    fontSize = 20.sp,
                    lineHeight = 25.sp,
                    fontWeight = FontWeight.Bold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f),
                )
                if (state.durationMs > 0) {
                    CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
                        Text(
                            text = "${elapsedLabel(state.positionMs)}  /  ${elapsedLabel(state.durationMs)}",
                            color = ReceiverColors.Muted,
                            fontSize = 14.sp,
                            lineHeight = 18.sp,
                        )
                    }
                }
            }
            if (state.durationMs > 0) {
                Spacer(Modifier.height(12.dp))
                PlaybackProgress(progressFraction(state.positionMs, state.durationMs))
            }
            Spacer(Modifier.height(11.dp))
            val playPauseFocusRequester = remember { FocusRequester() }
            LaunchedEffect(state.title) { playPauseFocusRequester.requestFocus() }
            Row {
                TvActionButton(
                    label = if (state.playbackState == PlaybackState.PLAYING) "PAUSE" else "PLAY",
                    enabled = true,
                    onClick = onTogglePlayPause,
                    modifier = Modifier
                        .testTag(ReceiverTags.PLAYBACK_PLAY_PAUSE)
                        .focusRequester(playPauseFocusRequester),
                )
                Spacer(Modifier.width(12.dp))
                TvActionButton(
                    label = "CLOSE",
                    enabled = true,
                    onClick = onClose,
                    modifier = Modifier.testTag(ReceiverTags.PLAYBACK_CLOSE),
                )
            }
        }
    }
}

@Composable
private fun PlaybackStatePill(state: PlaybackState) {
    val (label, color) = when (state) {
        PlaybackState.PLAYING -> "PLAYING" to ReceiverColors.Signal
        PlaybackState.PAUSED -> "PAUSED" to ReceiverColors.Warning
        PlaybackState.ENDED -> "FINISHED" to ReceiverColors.Success
        PlaybackState.BUFFERING -> "BUFFERING" to ReceiverColors.Signal
        PlaybackState.ERROR -> "ERROR" to ReceiverColors.Live
        PlaybackState.IDLE -> "READY" to ReceiverColors.Muted
    }
    Row(
        modifier = Modifier
            .background(color.copy(alpha = 0.12f), RoundedCornerShape(50))
            .padding(horizontal = 10.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.size(6.dp).clip(CircleShape).background(color))
        Spacer(Modifier.width(7.dp))
        Text(
            text = label,
            color = color,
            fontSize = 10.sp,
            lineHeight = 12.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 1.sp,
        )
    }
}

@Composable
private fun PlaybackProgress(fraction: Float) {
    Box(Modifier.fillMaxWidth().height(3.dp).clip(CircleShape).background(ReceiverColors.Line)) {
        Box(
            Modifier
                .fillMaxWidth(fraction)
                .height(3.dp)
                .clip(CircleShape)
                .background(ReceiverColors.Signal),
        )
    }
}

