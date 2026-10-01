package com.rextechnologies.flint.mobile.ui

import android.content.Intent
import android.provider.Settings
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import com.rextechnologies.flint.castcore.capability.ReceiverDevice
import com.rextechnologies.flint.castcore.copy.CastCopy
import com.rextechnologies.flint.castcore.copy.MobileTab
import com.rextechnologies.flint.castcore.copy.Placeholders
import com.rextechnologies.flint.design.FlintColors
import com.rextechnologies.flint.design.FlintSpace
import com.rextechnologies.flint.design.FlintText
import com.rextechnologies.flint.design.FlintType
import com.rextechnologies.flint.design.InfoCard
import com.rextechnologies.flint.design.OutlineAction
import com.rextechnologies.flint.design.PageHeading
import com.rextechnologies.flint.design.Pill
import com.rextechnologies.flint.design.SectionLabel
import com.rextechnologies.flint.design.SignalButton
import com.rextechnologies.flint.design.StatusDot
import com.rextechnologies.flint.design.TextEntry
import com.rextechnologies.flint.design.Tone
import com.rextechnologies.flint.design.flintClickable
import com.rextechnologies.flint.mobile.MobileController
import com.rextechnologies.flint.mobile.MobileUiState
import com.rextechnologies.flint.mobile.state.LookupState

/** Choose the destination first. Network and capability details stay available on demand. */
@Composable
fun CastScreen(state: MobileUiState, controller: MobileController) {
    PageHeading(
        eyebrow = "Your screens, together",
        headline = if (state.isConnected) "Your TV" else "Connect to your TV",
    )
    if (state.showNoNetwork) {
        HotspotCard()
        return
    }
    if (state.isConnected) {
        InfoCard(borderTone = Tone.Signal) {
            FlintText(text = state.selected?.displayName ?: "Connected TV", style = FlintType.TitleLarge)
            FlintText(text = "Choose what to show.", style = FlintType.BodyMedium)
            SignalButton(text = "Share screen", onClick = { controller.selectTab(MobileTab.SCREEN) })
            OutlineAction(text = "Play media", onClick = { controller.selectTab(MobileTab.MEDIA) })
            OutlineAction(text = "Disconnect", onClick = controller::disconnect)
        }
    } else {
        FlintText(
            text = "Open Flint on your TV and connect both devices to the same Wi-Fi or hotspot.",
            style = FlintType.BodyMedium,
        )
        SignalButton(
            text = if (state.isProbing) "Finding TVs…" else "Find TVs",
            onClick = controller::probe,
            enabled = !state.isProbing && !state.isConnecting,
        )
        if (state.receivers.isNotEmpty()) {
            SectionLabel("Nearby TVs")
            Column(verticalArrangement = Arrangement.spacedBy(FlintSpace.CardSpacing)) {
                state.receivers.forEach { device ->
                    ReceiverRow(
                        device = device,
                        selected = device.address == state.selected?.address,
                        paired = false,
                        onSelect = { controller.select(device) },
                    )
                }
            }
        }
        if (state.selected != null) {
            SignalButton(
                text = if (state.isConnecting) "Connecting…" else "Connect to ${state.selected.displayName}",
                onClick = { controller.showPairing(true) },
                enabled = !state.isConnecting,
            )
        }
        if (state.showProbeFailed) ProbeFailedCard(state)
        ManualAddressCard(state, controller)
    }
    if (state.selected != null) {
        DetailSection("Set up or manage Flint on this TV") { ReceiverSetupCard(state, controller) }
    }
    DetailSection("Connection details") {
        NetworkCard(state)
        state.selected?.let { device ->
            FlintText(text = "${device.address}:${device.port}", style = FlintType.BodySmall)
        }
        state.report?.let { report ->
            report.verdicts.forEach { ModeCard(it) }
        }
    }
}

@Composable
private fun NetworkCard(state: MobileUiState) {
    InfoCard(borderTone = CastCopy.networkTone(state.network).toDesignTone()) {
        Row(verticalAlignment = Alignment.Top) {
            StatusDot(
                tone = CastCopy.networkTone(state.network).toDesignTone(),
                modifier = Modifier.padding(top = FlintSpace.Tiny),
            )
            Spacer(Modifier.width(FlintSpace.Small))
            FlintText(
                text = CastCopy.networkLine(state.network),
                style = FlintType.BodyMedium.copy(color = FlintColors.Muted),
            )
        }
    }
}

/**
 * The dead end with a way out of it.
 *
 * There is no API an ordinary app may call to turn a hotspot on - `TetheringManager` is a system
 * API - so this opens the settings page rather than pretending to do it, and says which switch to
 * look for.
 */
@Composable
private fun HotspotCard() {
    val context = LocalContext.current
    InfoCard(borderTone = Tone.Live) {
        FlintText(text = CastCopy.NO_NETWORK_TITLE, style = FlintType.TitleMedium)
        FlintText(
            text = CastCopy.NO_NETWORK_BODY,
            style = FlintType.BodyMedium.copy(color = FlintColors.Muted),
        )
        OutlineAction(
            text = CastCopy.HOTSPOT_ACTION,
            onClick = {
                runCatching {
                    context.startActivity(
                        Intent(Settings.ACTION_WIRELESS_SETTINGS)
                            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
                    )
                }
            },
            tone = Tone.Signal,
        )
    }
}

/** A probe that ran and found nothing, which is a different screen from one that never ran. */
@Composable
private fun ProbeFailedCard(state: MobileUiState) {
    InfoCard(borderTone = Tone.Live) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            FlintText(
                text = CastCopy.PROBE_FAILED_TITLE,
                style = FlintType.TitleMedium,
                modifier = Modifier.weight(1f),
            )
            Pill(text = Placeholders.NO_RECEIVER, tone = Tone.Live)
        }
        FlintText(
            text = CastCopy.probeFailedBody(state.rungsAttempted),
            style = FlintType.BodyMedium.copy(color = FlintColors.Muted),
        )
    }
}

@Composable
private fun ReceiverRow(
    device: ReceiverDevice,
    selected: Boolean,
    paired: Boolean,
    onSelect: () -> Unit,
) {
    InfoCard(
        modifier = Modifier.flintClickable(onClick = onSelect, onClickLabel = device.displayName),
        borderTone = if (selected) Tone.Signal else Tone.Line,
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            FlintText(
                text = device.displayName,
                style = FlintType.TitleMedium,
                modifier = Modifier.weight(1f),
            )
            when {
                paired -> Pill(text = "Paired", tone = Tone.Live)
                selected -> Pill(text = "Selected", tone = Tone.Signal)
            }
        }
    }
}

/**
 * Typing an address in by hand, which is the rung that is always available.
 *
 * It carries its own pending and failed states. Without them a second tap fired a second concurrent
 * probe, and a probe that found nothing looked exactly like one that had not been pressed.
 */
@Composable
private fun ManualAddressCard(state: MobileUiState, controller: MobileController) {
    var address by remember { mutableStateOf("") }
    var open by remember { mutableStateOf(false) }

    Spacer(Modifier.height(FlintSpace.Small))
    if (!open) {
        OutlineAction(text = CastCopy.MANUAL_ACTION, onClick = { open = true })
        return
    }

    val checking = state.manualLookup is LookupState.Running
    InfoCard(borderTone = if (state.manualLookup is LookupState.FoundNothing) Tone.Live else Tone.Line) {
        SectionLabel("Address")
        FlintText(
            text = "On the TV: Settings, then My Fire TV, then About, then Network.",
            style = FlintType.BodySmall,
        )
        TextEntry(
            value = address,
            onValueChange = { address = it },
            placeholder = Placeholders.NONE,
            enabled = !checking,
        )
        if (state.manualLookup is LookupState.FoundNothing) {
            FlintText(
                text = "Nothing answered there.",
                style = FlintType.BodySmall.copy(color = FlintColors.Live),
            )
        }
        Row(horizontalArrangement = Arrangement.spacedBy(FlintSpace.Small)) {
            SignalButton(
                text = if (checking) CastCopy.MANUAL_CHECKING else "Check it",
                onClick = { controller.probeManualAddress(address) },
                enabled = address.isNotBlank() && !checking,
            )
            OutlineAction(text = "Cancel", onClick = { open = false }, enabled = !checking)
        }
    }

    // Closes itself once a television has answered, which is the only outcome that makes the card
    // redundant. A failure leaves it open with the address still in it, to be corrected. In an
    // effect rather than in the body: writing state during composition is a write the recomposition
    // it triggers may or may not see.
    LaunchedEffect(state.manualLookup) {
        if (state.manualLookup is LookupState.Found) open = false
    }
}
