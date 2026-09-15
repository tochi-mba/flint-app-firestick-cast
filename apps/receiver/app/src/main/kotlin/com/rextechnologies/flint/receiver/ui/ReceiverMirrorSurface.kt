package com.rextechnologies.flint.receiver.ui

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.focusable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
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
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
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
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.input.key.type
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.ReceiverUiState
import kotlinx.coroutines.delay

/**
 * How long the controls stay up before folding away again.
 *
 * Long enough to read the stats and reach a button with a D-pad, short enough that a viewer who
 * summoned them by accident gets their picture back without doing anything.
 */
private const val CONTROLS_AUTO_HIDE_MILLIS = 6_000L

/**
 * The mirrored picture, with controls that stay out of the way until asked for.
 *
 * @param controlsAutoHideMillis Null keeps the controls up indefinitely, which is what UI tests
 *   use so an assertion never races a timer.
 * @param initialControlsVisible Lets a test render the controls directly instead of synthesising
 *   the key press that summons them.
 */
@Composable
internal fun ReceiverMirrorSurface(
    state: ReceiverUiState,
    presentation: Boolean,
    mirrorContent: @Composable () -> Unit,
    onStopMirroring: () -> Unit = {},
    controlsAutoHideMillis: Long? = CONTROLS_AUTO_HIDE_MILLIS,
    initialControlsVisible: Boolean = false,
) {
    var controlsVisible by remember { mutableStateOf(initialControlsVisible) }
    var fitMode by remember { mutableStateOf(MirrorFitMode.FIT) }
    var statsVisible by remember { mutableStateOf(false) }

    // Bumped by anything that should restart the auto-hide countdown. Without it, pressing a
    // button four seconds in would leave the controls closing two seconds later, mid-interaction.
    var activityMarker by remember { mutableIntStateOf(0) }

    val surfaceFocus = remember { FocusRequester() }
    val firstControlFocus = remember { FocusRequester() }

    LaunchedEffect(controlsVisible) {
        // Focus has to move with the controls: onto a button when they appear so the D-pad has
        // somewhere to go, and back to the surface when they leave so it still receives keys.
        if (controlsVisible) firstControlFocus.requestFocus() else surfaceFocus.requestFocus()
    }

    LaunchedEffect(controlsVisible, activityMarker, controlsAutoHideMillis) {
        if (controlsVisible && controlsAutoHideMillis != null) {
            delay(controlsAutoHideMillis)
            controlsVisible = false
        }
    }

    BoxWithConstraints(
        Modifier
            .fillMaxSize()
            .background(Color.Black)
            .focusRequester(surfaceFocus)
            .focusable()
            // Preview rather than ordinary key handling, so the surface sees a press before the
            // focused button does. That is what lets the press which summons the controls be
            // swallowed instead of also landing on whichever button appears under the cursor.
            .onPreviewKeyEvent { event ->
                if (event.type != KeyEventType.KeyDown) {
                    return@onPreviewKeyEvent false
                }
                when (mirrorKeyOutcome(event.nativeKeyEvent.keyCode, controlsVisible)) {
                    MirrorKeyOutcome.REVEAL_CONTROLS -> {
                        controlsVisible = true
                        activityMarker++
                        true
                    }
                    MirrorKeyOutcome.HIDE_CONTROLS -> {
                        controlsVisible = false
                        true
                    }
                    MirrorKeyOutcome.STOP_MIRRORING -> {
                        onStopMirroring()
                        true
                    }
                    MirrorKeyOutcome.IGNORE -> {
                        // A press meant for a button still counts as being present.
                        if (controlsVisible) activityMarker++
                        false
                    }
                }
            },
    ) {
        val aspect = mirrorAspectRatio(state.mirrorWidth, state.mirrorHeight)
        val availableAspect = maxWidth.value / maxHeight.value
        val frameModifier = when {
            aspect == null -> Modifier.fillMaxSize()
            // FILL crops the overflowing edge by matching the *other* axis than FIT would.
            (aspect >= availableAspect) == (fitMode == MirrorFitMode.FIT) ->
                Modifier.fillMaxWidth().aspectRatio(aspect)
            else -> Modifier.fillMaxHeight().aspectRatio(aspect)
        }
        Box(frameModifier.align(Alignment.Center)) {
            mirrorContent()
        }

        val horizontalSafeArea = maxWidth * 0.05f
        val verticalSafeArea = maxHeight * 0.05f

        if (state.error != null) {
            ReceiverFailureOverlay(
                state = state,
                eyebrow = if (presentation) "SECOND SCREEN STOPPED" else "SCREEN MIRROR STOPPED",
                instruction = "Press BACK to close this screen",
                tag = ReceiverTags.MIRROR_ERROR,
            )
            return@BoxWithConstraints
        }

        if (!state.mirrorFrameReceived) {
            MirrorWaitingBadge(
                state = state,
                presentation = presentation,
                modifier = Modifier
                    .align(Alignment.TopStart)
                    .padding(horizontal = horizontalSafeArea, vertical = verticalSafeArea),
            )
        }

        AnimatedVisibility(
            visible = statsVisible,
            enter = fadeIn(),
            exit = fadeOut(),
            modifier = Modifier
                .align(Alignment.TopEnd)
                .padding(horizontal = horizontalSafeArea, vertical = verticalSafeArea),
        ) {
            MirrorStatsPanel(state, fitMode)
        }

        AnimatedVisibility(
            visible = controlsVisible,
            enter = fadeIn() + slideInVertically { it / 3 },
            exit = fadeOut() + slideOutVertically { it / 3 },
            modifier = Modifier.align(Alignment.BottomCenter),
        ) {
            MirrorControls(
                state = state,
                presentation = presentation,
                fitMode = fitMode,
                statsVisible = statsVisible,
                horizontalSafeArea = horizontalSafeArea,
                verticalSafeArea = verticalSafeArea,
                firstControlFocus = firstControlFocus,
                onToggleFit = {
                    fitMode = fitMode.toggled()
                    activityMarker++
                },
                onToggleStats = {
                    statsVisible = !statsVisible
                    activityMarker++
                },
                onStop = onStopMirroring,
            )
        }

        // The one piece of chrome that shows without being asked for. A surface with genuinely no
        // affordances reads as frozen, which is exactly how this screen was reported: "nothing
        // happens when I press the D-pad". One dim line is enough to say the remote does something.
        AnimatedVisibility(
            visible = !controlsVisible && state.mirrorFrameReceived,
            enter = fadeIn(),
            exit = fadeOut(),
            modifier = Modifier
                .align(Alignment.BottomEnd)
                .padding(horizontal = horizontalSafeArea, vertical = verticalSafeArea),
        ) {
            Text(
                text = "Press any arrow for options",
                color = ReceiverColors.Muted.copy(alpha = 0.55f),
                fontSize = 12.sp,
                lineHeight = 15.sp,
                letterSpacing = 0.4.sp,
                modifier = Modifier.testTag(ReceiverTags.MIRROR_HINT),
            )
        }
    }
}

@Composable
private fun MirrorControls(
    state: ReceiverUiState,
    presentation: Boolean,
    fitMode: MirrorFitMode,
    statsVisible: Boolean,
    horizontalSafeArea: androidx.compose.ui.unit.Dp,
    verticalSafeArea: androidx.compose.ui.unit.Dp,
    firstControlFocus: FocusRequester,
    onToggleFit: () -> Unit,
    onToggleStats: () -> Unit,
    onStop: () -> Unit,
) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .background(
                Brush.verticalGradient(
                    colors = listOf(Color.Transparent, ReceiverColors.Ink.copy(alpha = 0.97f)),
                ),
            )
            .padding(
                start = horizontalSafeArea,
                end = horizontalSafeArea,
                top = 64.dp,
                bottom = verticalSafeArea,
            )
            .testTag(ReceiverTags.MIRROR_CONTROLS),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            MirrorLivePill()
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Text(
                    text = if (presentation) "SECOND SCREEN" else "SCREEN MIRROR",
                    color = ReceiverColors.Signal,
                    fontSize = 11.sp,
                    lineHeight = 14.sp,
                    fontWeight = FontWeight.Bold,
                    letterSpacing = 1.3.sp,
                )
                Text(
                    text = state.peerName?.takeIf { it.isNotBlank() }
                        ?: state.title.ifBlank { "Your device" },
                    color = ReceiverColors.Text,
                    fontSize = 22.sp,
                    lineHeight = 28.sp,
                    fontWeight = FontWeight.Bold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            Text(
                text = mirrorStatsLabel(
                    state.mirrorWidth,
                    state.mirrorHeight,
                    state.mirrorFrameReceived,
                ),
                color = ReceiverColors.Muted,
                fontSize = 14.sp,
                lineHeight = 18.sp,
            )
        }

        Spacer(Modifier.height(18.dp))

        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            TvActionButton(
                label = fitMode.actionLabel,
                enabled = true,
                onClick = onToggleFit,
                modifier = Modifier
                    .testTag(ReceiverTags.MIRROR_CONTROLS_FIT)
                    .focusRequester(firstControlFocus),
            )
            TvActionButton(
                label = if (statsVisible) "HIDE STATS" else "SHOW STATS",
                enabled = true,
                onClick = onToggleStats,
                modifier = Modifier.testTag(ReceiverTags.MIRROR_CONTROLS_STATS),
            )
            TvActionButton(
                label = "STOP MIRRORING",
                enabled = true,
                onClick = onStop,
                modifier = Modifier.testTag(ReceiverTags.MIRROR_CONTROLS_STOP),
            )
        }

        Spacer(Modifier.height(12.dp))

        Text(
            text = "Press BACK to hide these controls",
            color = ReceiverColors.Muted,
            fontSize = 12.sp,
            lineHeight = 16.sp,
        )
    }
}

@Composable
private fun MirrorLivePill() {
    Row(
        modifier = Modifier
            .background(ReceiverColors.Signal.copy(alpha = 0.12f), RoundedCornerShape(50))
            .padding(horizontal = 10.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.size(6.dp).clip(CircleShape).background(ReceiverColors.Signal))
        Spacer(Modifier.width(7.dp))
        Text(
            text = "LIVE",
            color = ReceiverColors.Signal,
            fontSize = 10.sp,
            lineHeight = 12.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 1.sp,
        )
    }
}

@Composable
private fun MirrorStatsPanel(state: ReceiverUiState, fitMode: MirrorFitMode) {
    val shape = RoundedCornerShape(14.dp)
    Column(
        modifier = Modifier
            .background(ReceiverColors.Ink.copy(alpha = 0.9f), shape)
            .border(1.dp, ReceiverColors.Line, shape)
            .padding(horizontal = 16.dp, vertical = 13.dp)
            .testTag(ReceiverTags.MIRROR_STATS_PANEL),
    ) {
        StatsRow("SOURCE", state.peerName?.takeIf { it.isNotBlank() } ?: "Unknown device")
        StatsRow(
            "FRAME",
            mirrorStatsLabel(state.mirrorWidth, state.mirrorHeight, state.mirrorFrameReceived),
        )
        StatsRow("SCALING", if (fitMode == MirrorFitMode.FIT) "Fit, nothing cropped" else "Filled, edges cropped")
    }
}

@Composable
private fun StatsRow(label: String, value: String) {
    Row(Modifier.padding(vertical = 3.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(
            text = label,
            color = ReceiverColors.Muted,
            fontSize = 10.sp,
            lineHeight = 13.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 1.1.sp,
            modifier = Modifier.width(78.dp),
        )
        Text(
            text = value,
            color = ReceiverColors.Text,
            fontSize = 13.sp,
            lineHeight = 17.sp,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
    }
}

@Composable
private fun MirrorWaitingBadge(
    state: ReceiverUiState,
    presentation: Boolean,
    modifier: Modifier = Modifier,
) {
    val shape = RoundedCornerShape(14.dp)
    Row(
        modifier = modifier
            .background(ReceiverColors.Ink.copy(alpha = 0.88f), shape)
            .border(1.dp, ReceiverColors.Line, shape)
            .padding(horizontal = 17.dp, vertical = 13.dp)
            .testTag(ReceiverTags.MIRROR_HUD),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        ReceiverSpinner(Modifier.size(26.dp))
        Spacer(Modifier.width(12.dp))
        Column {
            Text(
                text = if (presentation) "SECOND SCREEN" else "SCREEN MIRROR",
                color = ReceiverColors.Signal,
                fontSize = 11.sp,
                lineHeight = 14.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = 1.3.sp,
            )
            Text(
                text = state.title.ifBlank { "Waiting for the first frame" },
                color = ReceiverColors.Text,
                fontSize = 16.sp,
                lineHeight = 21.sp,
                fontWeight = FontWeight.Bold,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}
