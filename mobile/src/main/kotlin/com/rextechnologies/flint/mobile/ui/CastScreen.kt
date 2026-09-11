package com.rextechnologies.flint.mobile.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import com.rextechnologies.flint.castcore.capability.ReceiverDevice
import com.rextechnologies.flint.castcore.copy.CastCopy
import com.rextechnologies.flint.castcore.copy.MobileTab
import com.rextechnologies.flint.castcore.copy.Placeholders
import com.rextechnologies.flint.design.EmptyState
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
import com.rextechnologies.flint.design.Tone
import com.rextechnologies.flint.design.flintClickable
import com.rextechnologies.flint.mobile.MobileController
import com.rextechnologies.flint.mobile.MobileUiState

/** The Cast tab: the network, the television, and the verdict for each mode. */
@Composable
fun CastScreen(state: MobileUiState, controller: MobileController) {
    val tab = MobileTab.CAST
    PageHeading(
        eyebrow = tab.eyebrow,
        headline = tab.title,
        statusText = CastCopy.headingStatus(state.isProbing, state.report),
        statusTone = CastCopy.headingTone(state.report).toDesignTone(),
    )

    Spacer(Modifier.height(FlintSpace.Small))
    SectionLabel(CastCopy.SECTION_NETWORK)
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

    Spacer(Modifier.height(FlintSpace.Small))
    Row(horizontalArrangement = Arrangement.spacedBy(FlintSpace.Small)) {
        SignalButton(
            text = if (state.isProbing) "Probing" else CastCopy.PROBE_ACTION,
            onClick = controller::probe,
            enabled = !state.isProbing,
        )
        OutlineAction(
            text = CastCopy.PAIR_ACTION,
            onClick = { controller.showPairing(true) },
            enabled = state.selected != null,
        )
    }

    ManualAddressCard(controller)

    if (state.showEmptyState) {
        Spacer(Modifier.height(FlintSpace.Small))
        EmptyState(
            glyph = CastCopy.empty.glyph,
            title = CastCopy.empty.title,
            body = CastCopy.empty.body,
        )
        return
    }

    if (state.receivers.isNotEmpty()) {
        Spacer(Modifier.height(FlintSpace.Small))
        SectionLabel(CastCopy.SECTION_RECEIVERS)
        Column(verticalArrangement = Arrangement.spacedBy(FlintSpace.CardSpacing)) {
            state.receivers.forEach { device ->
                ReceiverRow(
                    device = device,
                    selected = device.address == state.selected?.address,
                    onSelect = { controller.select(device) },
                )
            }
        }
    }

    state.report?.let { report ->
        Spacer(Modifier.height(FlintSpace.Small))
        SectionLabel(CastCopy.SECTION_MODES)
        Column(
            modifier = Modifier.semantics { liveRegion = LiveRegionMode.Polite },
            verticalArrangement = Arrangement.spacedBy(FlintSpace.CardSpacing),
        ) {
            report.verdicts.forEach { ModeCard(it) }
        }
    }
}

@Composable
private fun ReceiverRow(device: ReceiverDevice, selected: Boolean, onSelect: () -> Unit) {
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
            if (selected) Pill(text = "Selected", tone = Tone.Signal)
        }
        FlintText(
            text = "${device.address}:${device.port} · ${device.source.name.lowercase().replace('_', ' ')}",
            style = FlintType.BodySmall,
        )
    }
}

@Composable
private fun ManualAddressCard(controller: MobileController) {
    var address by remember { mutableStateOf("") }
    var open by remember { mutableStateOf(false) }

    Spacer(Modifier.height(FlintSpace.Small))
    if (!open) {
        OutlineAction(text = CastCopy.MANUAL_ACTION, onClick = { open = true })
        return
    }

    InfoCard {
        SectionLabel("Address")
        FlintText(
            text = "On the TV: Settings, then My Fire TV, then About, then Network.",
            style = FlintType.BodySmall,
        )
        TextEntry(
            value = address,
            onValueChange = { address = it },
            placeholder = Placeholders.NONE,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(FlintSpace.Small)) {
            SignalButton(
                text = "Check it",
                onClick = {
                    controller.probeManualAddress(address)
                    open = false
                },
                enabled = address.isNotBlank(),
            )
            OutlineAction(text = "Cancel", onClick = { open = false })
        }
    }
}
