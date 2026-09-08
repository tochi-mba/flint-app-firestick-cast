package com.rextechnologies.flint.receiver.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
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
import androidx.compose.runtime.remember
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.sp
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.ReceiverUiState

@Composable
internal fun ReceiverIdleSurface(
    state: ReceiverUiState,
    onRefreshCode: () -> Unit,
    onRetry: () -> Unit,
    onBrowse: () -> Unit,
    modifier: Modifier = Modifier,
) {
    BoxWithConstraints(modifier.fillMaxSize()) {
        val horizontalSafeArea = maxWidth * 0.05f
        val verticalSafeArea = maxHeight * 0.05f
        val compact = maxWidth < 820.dp

        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(horizontal = horizontalSafeArea, vertical = verticalSafeArea),
        ) {
            ReceiverHeader(state, Modifier.fillMaxWidth())
            Spacer(Modifier.height(if (compact) 24.dp else 32.dp))
            Row(
                modifier = Modifier.fillMaxSize(),
                horizontalArrangement = Arrangement.spacedBy(if (compact) 28.dp else 42.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                ReceiverHero(
                    state = state,
                    modifier = Modifier.weight(if (compact) 0.82f else 0.86f),
                )
                ReceiverPrimaryCard(
                    state = state,
                    onRefreshCode = onRefreshCode,
                    onRetry = onRetry,
                    onBrowse = onBrowse,
                    modifier = Modifier.weight(if (compact) 1.18f else 1.14f),
                )
            }
        }
    }
}

@Composable
private fun ReceiverHero(state: ReceiverUiState, modifier: Modifier = Modifier) {
    val experience = state.idleExperience()
    val eyebrow = when (experience) {
        IdleExperience.STARTING -> "STARTING FLINT"
        IdleExperience.NO_NETWORK -> "SAME NETWORK REQUIRED"
        IdleExperience.READY -> "READY FOR FLINT"
        IdleExperience.CONNECTED -> "PC CONNECTED"
        IdleExperience.ATTENTION -> "NEEDS ATTENTION"
    }
    val title = when (experience) {
        IdleExperience.STARTING -> "Almost ready."
        IdleExperience.NO_NETWORK -> "Bring your screens\ntogether."
        IdleExperience.READY -> "Your TV is ready.\nLet's put it to work."
        IdleExperience.CONNECTED -> "You're connected.\nChoose what comes next."
        IdleExperience.ATTENTION -> "Let's get you\nback on screen."
    }
    val body = when (experience) {
        IdleExperience.STARTING -> "One moment while Flint prepares this TV for your PC."
        IdleExperience.NO_NETWORK -> "Connect this Fire TV and your Windows PC to the same Wi-Fi or hotspot."
        IdleExperience.READY -> "Enter the code shown here in Flint on your Windows PC."
        IdleExperience.CONNECTED -> "Flint will switch this screen automatically when your media or display is ready."
        IdleExperience.ATTENTION -> state.error ?: "The receiver couldn't start on this network."
    }

    Column(
        modifier = modifier
            .fillMaxHeight()
            .testTag(ReceiverTags.IDLE_HERO),
        verticalArrangement = Arrangement.Center,
    ) {
        Text(
            text = eyebrow,
            color = when (experience) {
                IdleExperience.ATTENTION -> ReceiverColors.Live
                IdleExperience.CONNECTED -> ReceiverColors.Success
                IdleExperience.NO_NETWORK -> ReceiverColors.Warning
                else -> ReceiverColors.Signal
            },
            fontSize = 12.sp,
            lineHeight = 15.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 2.sp,
        )
        Text(
            text = title,
            color = ReceiverColors.Text,
            fontSize = 40.sp,
            lineHeight = 44.sp,
            fontWeight = FontWeight.Bold,
            modifier = Modifier.padding(top = 11.dp),
            maxLines = 3,
        )
        Text(
            text = body,
            color = if (experience == IdleExperience.ATTENTION) ReceiverColors.Text else ReceiverColors.Muted,
            fontSize = 16.sp,
            lineHeight = 23.sp,
            modifier = Modifier.padding(top = 17.dp),
        )
        if (experience == IdleExperience.READY) {
            Spacer(Modifier.height(22.dp))
            PairingSteps()
        }
    }
}

@Composable
private fun PairingSteps() {
    Column(verticalArrangement = Arrangement.spacedBy(9.dp)) {
        InstructionStep("1", "Open Flint on your Windows PC")
        InstructionStep("2", "Choose this TV or enter its address")
        InstructionStep("3", "Type the six-digit code")
    }
}

@Composable
private fun InstructionStep(number: String, label: String) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(
            modifier = Modifier
                .size(25.dp)
                .background(ReceiverColors.Raised, CircleShape)
                .border(1.dp, ReceiverColors.Line, CircleShape),
            contentAlignment = Alignment.Center,
        ) {
            Text(
                text = number,
                color = ReceiverColors.Signal,
                fontSize = 11.sp,
                lineHeight = 13.sp,
                fontWeight = FontWeight.Bold,
            )
        }
        Spacer(Modifier.width(10.dp))
        Text(
            text = label,
            color = ReceiverColors.Text,
            fontSize = 14.sp,
            lineHeight = 18.sp,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
    }
}

@Composable
private fun ReceiverPrimaryCard(
    state: ReceiverUiState,
    onRefreshCode: () -> Unit,
    onRetry: () -> Unit,
    onBrowse: () -> Unit,
    modifier: Modifier = Modifier,
) {
    when (state.idleExperience()) {
        IdleExperience.READY -> PairingCard(state, onRefreshCode, onBrowse, modifier)
        IdleExperience.CONNECTED -> ConnectedCard(state, onBrowse, modifier)
        IdleExperience.STARTING -> StatusCard(
            eyebrow = "NETWORK CHECK",
            title = "Finding this TV's address",
            detail = "Your pairing code will appear here as soon as the receiver is ready.",
            modifier = modifier,
            loading = true,
        )
        IdleExperience.NO_NETWORK -> StatusCard(
            eyebrow = "NETWORK NEEDED",
            title = "Connect this Fire TV",
            detail = "Open Settings and join the same Wi-Fi or hotspot as your Windows PC. Flint will continue automatically.",
            modifier = modifier,
        )
        IdleExperience.ATTENTION -> StatusCard(
            eyebrow = "RECEIVER PAUSED",
            title = "Something needs attention",
            detail = state.error ?: state.detail,
            modifier = modifier,
            actionLabel = "TRY AGAIN",
            onAction = onRetry,
        )
    }
}

@Composable
private fun PairingCard(
    state: ReceiverUiState,
    onRefreshCode: () -> Unit,
    onBrowse: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val groups = pairingCodeGroups(state.pairingCode)
    val focusRequester = remember { FocusRequester() }
    LaunchedEffect(state.ready) {
        if (state.ready) {
            withFrameNanos { }
            focusRequester.requestFocus()
        }
    }

    CardFrame(
        modifier = modifier.testTag(ReceiverTags.PAIRING_PANEL),
        accent = ReceiverColors.Signal,
    ) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Text(
                text = "PAIRING CODE",
                color = ReceiverColors.Signal,
                fontSize = 12.sp,
                lineHeight = 15.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = 1.8.sp,
            )
            Spacer(Modifier.weight(1f))
            Text(
                text = "ENTER ON YOUR PC",
                color = ReceiverColors.Muted,
                fontSize = 11.sp,
                lineHeight = 14.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = 1.1.sp,
            )
        }

        Spacer(Modifier.height(13.dp))
        PairingCode(groups)
        if (state.browserSecureReady) {
            Spacer(modifier.height(16.dp))
            Text(
                text = "BROWSER SECURITY CODE",
                color = ReceiverColors.Muted,
                fontSize = 11.sp,
                lineHeight = 14.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = 1.2.sp,
            )
            Spacer(modifier.height(6.dp))
            CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
                Text(
                    text = state.browserFingerprint.orEmpty(),
                    color = ReceiverColors.Signal,
                    fontSize = 22.sp,
                    lineHeight = 28.sp,
                    fontWeight = FontWeight.SemiBold,
                    letterSpacing = 1.6.sp,
                )
            }
            Spacer(Modifier.height(10.dp))
            Text(
                text = "BROWSER PORT",
                color = ReceiverColors.Muted,
                fontSize = 11.sp,
                lineHeight = 14.sp,
                fontWeight = FontWeight.Bold,
                letterSpacing = 1.2.sp,
            )
            Spacer(Modifier.height(4.dp))
            CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
                Text(
                    text = state.browserPort.toString(),
                    color = ReceiverColors.Text,
                    fontFamily = FontFamily.Monospace,
                    fontSize = 18.sp,
                    lineHeight = 24.sp,
                    fontWeight = FontWeight.Bold,
                    modifier = Modifier
                        .testTag(ReceiverTags.BROWSER_PORT)
                        .semantics {
                            contentDescription = "Browser port ${state.browserPort}"
                        },
                )
            }
            Spacer(Modifier.height(6.dp))
            Text(
                text = "Compare this code with Windows before trusting the secure browser session.",
                color = ReceiverColors.Muted,
                fontSize = 12.sp,
                lineHeight = 16.sp,
            )
        }
        Spacer(Modifier.height(21.dp))
        Box(Modifier.fillMaxWidth().height(1.dp).background(ReceiverColors.Line))
        Spacer(Modifier.height(19.dp))

        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            SignalIcon(Modifier.size(44.dp))
            Spacer(Modifier.width(13.dp))
            Column(Modifier.weight(1f)) {
                Text(
                    text = "TV ADDRESS",
                    color = ReceiverColors.Muted,
                    fontSize = 11.sp,
                    lineHeight = 14.sp,
                    fontWeight = FontWeight.Bold,
                    letterSpacing = 1.2.sp,
                )
                CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
                    Text(
                        text = endpointLabel(state).orEmpty(),
                        color = ReceiverColors.Text,
                        fontFamily = FontFamily.Monospace,
                        fontSize = 15.sp,
                        lineHeight = 20.sp,
                        fontWeight = FontWeight.Bold,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.testTag(ReceiverTags.ENDPOINT),
                    )
                }
            }
        }
        Spacer(Modifier.height(16.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small)) {
                TvActionButton(
                    label = "BROWSE ON THIS TV",
                    enabled = state.ready,
                    onClick = onBrowse,
                    modifier = Modifier
                        .testTag(ReceiverTags.BROWSER_ENTRY)
                        .focusRequester(focusRequester),
                )
                TvActionButton(
                    label = "NEW CODE",
                    enabled = state.ready,
                    onClick = onRefreshCode,
                    modifier = Modifier.testTag(ReceiverTags.PRIMARY_ACTION),
                )
        }
        Spacer(Modifier.height(14.dp))
        Text(
            text = "Keep this screen open while Flint connects.",
            color = ReceiverColors.Muted,
            fontSize = 14.sp,
            lineHeight = 19.sp,
        )
    }
}

@Composable
private fun PairingCode(groups: PairingCodeGroups?) {
    val first = groups?.first ?: "———"
    val second = groups?.second ?: "———"
    val description = groups?.let { "Pairing code ${it.spoken}" } ?: "Pairing code is not ready"
    CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .testTag(ReceiverTags.PAIRING_CODE)
                .clearAndSetSemantics {
                    contentDescription = description
                    liveRegion = LiveRegionMode.Polite
                },
            horizontalArrangement = Arrangement.Center,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            PairingCodeGroup(first)
            Spacer(Modifier.width(16.dp))
            Box(
                Modifier
                    .size(7.dp)
                    .background(ReceiverColors.Signal, CircleShape),
            )
            Spacer(Modifier.width(16.dp))
            PairingCodeGroup(second)
        }
    }
}

@Composable
private fun PairingCodeGroup(value: String) {
    Text(
        text = value,
        color = ReceiverColors.Text,
        fontFamily = FontFamily.Monospace,
        fontSize = 52.sp,
        lineHeight = 62.sp,
        fontWeight = FontWeight.Bold,
        letterSpacing = 3.sp,
        textAlign = TextAlign.Center,
        softWrap = false,
        maxLines = 1,
    )
}

@Composable
private fun ConnectedCard(
    state: ReceiverUiState,
    onBrowse: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val browseFocus = remember { FocusRequester() }
    LaunchedEffect(state.ready) {
        if (state.ready) {
            withFrameNanos { }
            browseFocus.requestFocus()
        }
    }
    CardFrame(modifier, ReceiverColors.Success) {
        SignalIcon(color = ReceiverColors.Success, success = true)
        Spacer(Modifier.height(19.dp))
        Text(
            text = "CONNECTED",
            color = ReceiverColors.Success,
            fontSize = 12.sp,
            lineHeight = 15.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 1.8.sp,
        )
        Text(
            text = state.peerName ?: "Windows PC",
            color = ReceiverColors.Text,
            fontSize = 25.sp,
            lineHeight = 31.sp,
            fontWeight = FontWeight.Bold,
            maxLines = 2,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.padding(top = 8.dp),
        )
        Text(
            text = "Ready. Choose something in Flint and it will appear here automatically.",
            color = ReceiverColors.Muted,
            fontSize = 15.sp,
            lineHeight = 22.sp,
            modifier = Modifier.padding(top = 13.dp),
        )
        endpointLabel(state)?.let { endpoint ->
            Text(
                text = endpoint,
                color = ReceiverColors.Muted,
                fontFamily = FontFamily.Monospace,
                fontSize = 13.sp,
                lineHeight = 17.sp,
                modifier = Modifier.padding(top = 18.dp).testTag(ReceiverTags.ENDPOINT),
            )
        }
        Spacer(Modifier.height(22.dp))
        TvActionButton(
            label = "BROWSE ON THIS TV",
            enabled = state.ready,
            onClick = onBrowse,
            modifier = Modifier
                .testTag(ReceiverTags.BROWSER_ENTRY)
                .focusRequester(browseFocus),
        )
    }
}

@Composable
private fun StatusCard(
    eyebrow: String,
    title: String,
    detail: String,
    modifier: Modifier = Modifier,
    loading: Boolean = false,
    actionLabel: String? = null,
    onAction: () -> Unit = {},
) {
    val accent = if (actionLabel != null) ReceiverColors.Live else ReceiverColors.Warning
    val focusRequester = remember { FocusRequester() }
    LaunchedEffect(actionLabel) {
        if (actionLabel != null) focusRequester.requestFocus()
    }
    CardFrame(modifier, accent) {
        if (loading) ReceiverSpinner() else SignalIcon(color = accent)
        Spacer(Modifier.height(20.dp))
        Text(
            text = eyebrow,
            color = accent,
            fontSize = 12.sp,
            lineHeight = 15.sp,
            fontWeight = FontWeight.Bold,
            letterSpacing = 1.8.sp,
        )
        Text(
            text = title,
            color = ReceiverColors.Text,
            fontSize = 27.sp,
            lineHeight = 33.sp,
            fontWeight = FontWeight.Bold,
            modifier = Modifier.padding(top = 8.dp),
        )
        Text(
            text = detail,
            color = ReceiverColors.Muted,
            fontSize = 15.sp,
            lineHeight = 22.sp,
            maxLines = 4,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier
                .padding(top = 13.dp)
                .then(
                    if (actionLabel != null) {
                        Modifier.clearAndSetSemantics {
                            contentDescription = detail
                            liveRegion = LiveRegionMode.Assertive
                        }
                    } else {
                        Modifier
                    },
                ),
        )
        actionLabel?.let {
            TvActionButton(
                label = it,
                enabled = true,
                onClick = onAction,
                modifier = Modifier
                    .testTag(ReceiverTags.PRIMARY_ACTION)
                    .padding(top = 22.dp)
                    .focusRequester(focusRequester),
            )
        }
    }
}

@Composable
private fun CardFrame(
    modifier: Modifier,
    accent: androidx.compose.ui.graphics.Color,
    content: @Composable ColumnScope.() -> Unit,
) {
    val shape = RoundedCornerShape(22.dp)
    Column(
        modifier = modifier
            .background(ReceiverColors.Panel, shape)
            .border(1.dp, accent.copy(alpha = 0.62f), shape)
            .padding(horizontal = 29.dp, vertical = 26.dp),
        verticalArrangement = Arrangement.Center,
        content = content,
    )
}
