package com.rextechnologies.flint.receiver.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.LocalLayoutDirection
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.LayoutDirection
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.ReceiverUiState

/** Waiting is one task: connect a device. Technical details stay in an explicit remote-accessible sheet. */
@Composable
internal fun ReceiverIdleSurface(
    state: ReceiverUiState,
    onRefreshCode: () -> Unit,
    onRetry: () -> Unit,
    onBrowse: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val experience = state.idleExperience()
    var details by remember(experience) { mutableStateOf(false) }
    val primaryFocus = remember { FocusRequester() }
    val detailsFocus = remember { FocusRequester() }
    // Waiting: the details button is the only control, so it holds focus. Connected or in trouble:
    // the one action on the screen does.
    val primaryOnScreen = experience == IdleExperience.CONNECTED || experience == IdleExperience.ATTENTION
    LaunchedEffect(experience) {
        if (state.ready || experience == IdleExperience.ATTENTION) {
            withFrameNanos { }
            primaryFocus.requestFocus()
        }
    }
    BoxWithConstraints(modifier.fillMaxSize()) {
        Column(
            Modifier.fillMaxSize().padding(horizontal = maxWidth * 0.05f, vertical = maxHeight * 0.05f),
        ) {
            Text("Flint", color = ReceiverColors.Text, fontSize = 22.sp, fontWeight = FontWeight.Bold)
            Column(
                Modifier.weight(1f).fillMaxWidth().testTag(ReceiverTags.IDLE_HERO),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center,
            ) {
                Text(
                    text = when (experience) {
                        IdleExperience.STARTING -> "Getting ready"
                        IdleExperience.NO_NETWORK -> "Connect to your network"
                        IdleExperience.READY -> "Ready to connect"
                        IdleExperience.CONNECTED -> "Connected to ${state.peerName ?: "your device"}"
                        IdleExperience.ATTENTION -> "Let's reconnect"
                    },
                    color = ReceiverColors.Text,
                    fontSize = 32.sp,
                    lineHeight = 39.sp,
                    fontWeight = FontWeight.Bold,
                    textAlign = TextAlign.Center,
                    modifier = Modifier.widthIn(max = 680.dp),
                )
                Spacer(Modifier.height(16.dp))
                Text(
                    text = when (experience) {
                        IdleExperience.STARTING -> "Your connection code will appear in a moment."
                        IdleExperience.NO_NETWORK ->
                            "Connect this TV and your phone or PC to the same Wi-Fi or hotspot."
                        IdleExperience.READY -> "Choose this TV in Flint on your phone or PC, then enter this code."
                        IdleExperience.CONNECTED -> "Choose something to show in Flint."
                        IdleExperience.ATTENTION -> state.error ?: state.detail
                    },
                    color = ReceiverColors.Muted,
                    fontSize = 17.sp,
                    lineHeight = 25.sp,
                    textAlign = TextAlign.Center,
                    modifier = Modifier.widthIn(max = 580.dp).semantics { liveRegion = LiveRegionMode.Polite },
                )
                if (experience == IdleExperience.READY) {
                    Spacer(Modifier.height(24.dp))
                    Box(Modifier.widthIn(max = 420.dp).testTag(ReceiverTags.PAIRING_PANEL)) {
                        PairingCode(pairingCodeGroups(state.pairingCode))
                    }
                }
                if (experience == IdleExperience.ATTENTION) {
                    Spacer(Modifier.height(24.dp))
                    TvActionButton(
                        label = "Try again",
                        enabled = true,
                        onClick = onRetry,
                        modifier = Modifier.testTag(ReceiverTags.PRIMARY_ACTION).focusRequester(primaryFocus),
                    )
                }
                if (experience == IdleExperience.CONNECTED) {
                    // The one thing a person needs from this screen while a device holds it. It
                    // ends that session and shows a fresh code, so another device can take over.
                    Spacer(Modifier.height(24.dp))
                    TvActionButton(
                        label = "Disconnect",
                        enabled = true,
                        onClick = onRefreshCode,
                        modifier = Modifier.testTag(ReceiverTags.PRIMARY_ACTION).focusRequester(primaryFocus),
                    )
                }
            }
            TvActionButton(
                label = "Connection details",
                enabled = true,
                onClick = { details = true },
                modifier = Modifier
                    .testTag("receiver-connection-details")
                    .focusRequester(if (state.ready && !primaryOnScreen) primaryFocus else detailsFocus),
            )
        }
    }
    if (details) {
        ReceiverConnectionDetails(
            state = state,
            onBrowse = onBrowse,
            onRefreshCode = onRefreshCode,
            onClose = {
                details = false
                if (state.ready && !primaryOnScreen) primaryFocus.requestFocus() else detailsFocus.requestFocus()
            },
        )
    }
}

/** The security fingerprint stays available for comparison without competing with the pairing code. */
@Composable
private fun ReceiverConnectionDetails(
    state: ReceiverUiState,
    onBrowse: () -> Unit,
    onRefreshCode: () -> Unit,
    onClose: () -> Unit,
) {
    val closeFocus = remember { FocusRequester() }
    Dialog(
        onDismissRequest = onClose,
        properties = DialogProperties(usePlatformDefaultWidth = false, dismissOnClickOutside = false),
    ) {
        LaunchedEffect(Unit) { closeFocus.requestFocus() }
        Column(
            Modifier.fillMaxSize(0.8f).background(ReceiverColors.Panel, ReceiverShapes.Medium)
                .verticalScroll(rememberScrollState()).padding(28.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Text("Connection details", color = ReceiverColors.Text, fontSize = 24.sp, fontWeight = FontWeight.Bold)
            Text("Use these details only if Flint asks for them.", color = ReceiverColors.Muted, fontSize = 16.sp)
            CompositionLocalProvider(LocalLayoutDirection provides LayoutDirection.Ltr) {
                Text(
                    "TV address: ${endpointLabel(state) ?: "Not connected to a network"}",
                    color = ReceiverColors.Text,
                    fontSize = 18.sp,
                    modifier = Modifier.testTag(ReceiverTags.ENDPOINT),
                )
                if (state.browserSecureReady) {
                    Text("Browser security code", color = ReceiverColors.Muted, fontSize = 16.sp)
                    Text(
                        state.browserFingerprint.orEmpty(),
                        color = ReceiverColors.Signal,
                        fontSize = 26.sp,
                        fontWeight = FontWeight.Bold,
                        modifier = Modifier.testTag("receiver-security-code"),
                    )
                    Text(
                        "Compare this code with Windows before trusting the browser connection.",
                        color = ReceiverColors.Muted,
                        fontSize = 16.sp,
                    )
                    Text(
                        "Browser port: ${state.browserPort}",
                        color = ReceiverColors.Text,
                        fontSize = 16.sp,
                        modifier = Modifier.testTag(ReceiverTags.BROWSER_PORT),
                    )
                }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small)) {
                TvActionButton("Close", true, onClose, Modifier.focusRequester(closeFocus))
                if (state.ready) {
                    TvActionButton(
                        "Browse on TV",
                        true,
                        onBrowse,
                        Modifier.testTag(ReceiverTags.BROWSER_ENTRY),
                    )
                }
                // Disconnecting lives on the connected screen itself; here the only code action is
                // a fresh one for a TV nobody holds.
                if (state.ready && !state.connected) {
                    TvActionButton(
                        "New code",
                        true,
                        onRefreshCode,
                        Modifier.testTag(ReceiverTags.PRIMARY_ACTION),
                    )
                }
            }
        }
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
