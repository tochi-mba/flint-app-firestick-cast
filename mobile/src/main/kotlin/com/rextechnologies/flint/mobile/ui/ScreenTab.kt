package com.rextechnologies.flint.mobile.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import com.rextechnologies.flint.castcore.capability.CastMode
import com.rextechnologies.flint.castcore.capability.ModePresentation
import com.rextechnologies.flint.castcore.copy.DiagnosticsCopy
import com.rextechnologies.flint.castcore.copy.MobileTab
import com.rextechnologies.flint.castcore.copy.ScreenCopy
import com.rextechnologies.flint.castcore.media.CodecNames
import com.rextechnologies.flint.castcore.media.SessionDiagnostics
import com.rextechnologies.flint.castcore.screen.PlaybackClock
import com.rextechnologies.flint.design.AdvisoryBlock
import com.rextechnologies.flint.design.DiagnosticRow
import com.rextechnologies.flint.design.EmptyState
import com.rextechnologies.flint.design.FlintColors
import com.rextechnologies.flint.design.FlintSpace
import com.rextechnologies.flint.design.FlintText
import com.rextechnologies.flint.design.FlintType
import com.rextechnologies.flint.design.InfoCard
import com.rextechnologies.flint.design.OutlineAction
import com.rextechnologies.flint.design.PageHeading
import com.rextechnologies.flint.design.Pill
import com.rextechnologies.flint.design.Readout
import com.rextechnologies.flint.design.SectionLabel
import com.rextechnologies.flint.design.Tone
import com.rextechnologies.flint.mobile.LiveOutput
import com.rextechnologies.flint.mobile.MobileActivity
import com.rextechnologies.flint.mobile.MobileController
import com.rextechnologies.flint.mobile.MobileUiState
import com.rextechnologies.flint.mobile.OutputMode
import com.rextechnologies.flint.protocol.text.Decimal

/** The Screen tab: mirror and second screen, their live strip, and their stopped states. */
@Composable
fun ScreenTab(
    state: MobileUiState,
    controller: MobileController,
    activity: MobileActivity,
    onRequestMirror: () -> Unit,
) {
    val tab = MobileTab.SCREEN
    PageHeading(
        eyebrow = tab.eyebrow,
        headline = tab.title,
        statusText = state.output?.let { ScreenCopy.LIVE_PILL } ?: "Idle",
        statusTone = if (state.output != null) Tone.Live else Tone.Neutral,
    )

    val report = state.report
    if (report == null) {
        Spacer(Modifier.height(FlintSpace.Small))
        EmptyState(
            glyph = ScreenCopy.empty.glyph,
            title = ScreenCopy.empty.title,
            body = ScreenCopy.empty.body,
        )
        return
    }

    state.output?.let { output ->
        Spacer(Modifier.height(FlintSpace.Small))
        LiveStrip(output, controller)
        return
    }

    if (!state.isConnected) {
        // The one thing missing is the session, and saying which one it is beats letting somebody
        // read three Blocked cards to work it out.
        Spacer(Modifier.height(FlintSpace.Small))
        InfoCard(borderTone = Tone.Line) {
            FlintText(text = "Not connected", style = FlintType.TitleMedium)
            FlintText(
                text = ScreenCopy.NOT_PAIRED,
                style = FlintType.BodyMedium.copy(color = FlintColors.Muted),
            )
        }
    }

    Spacer(Modifier.height(FlintSpace.Small))
    SectionLabel(ScreenCopy.SECOND_SCREEN_TITLE)
    ModeStarter(
        presentation = ModePresentation.of(report[CastMode.SECOND_SCREEN]),
        action = ScreenCopy.START_SECOND_SCREEN,
        note = ScreenCopy.SECOND_SCREEN_NO_CONSENT,
        enabled = state.isConnected,
        onStart = { controller.startSecondScreen(activity) },
    )

    Spacer(Modifier.height(FlintSpace.Small))
    SectionLabel("Mirror")
    ModeStarter(
        presentation = ModePresentation.of(report[CastMode.MIRROR]),
        action = ScreenCopy.START_MIRROR,
        note = ScreenCopy.MIRROR_CONSENT,
        enabled = state.isConnected,
        onStart = onRequestMirror,
    )
}

/**
 * One startable mode.
 *
 * The button's enabled state is the verdict's, and whether a session is open. A second,
 * independently-maintained gate here is exactly how a page ends up offering a control the capability
 * report already knows is empty — so the only thing added to the verdict is the one fact the verdict
 * does not carry.
 */
@Composable
private fun ModeStarter(
    presentation: ModePresentation,
    action: String,
    note: String,
    enabled: Boolean,
    onStart: () -> Unit,
) {
    InfoCard(borderTone = presentation.tone.toDesignTone()) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            FlintText(
                text = presentation.title,
                style = FlintType.TitleMedium,
                modifier = Modifier.weight(1f),
            )
            Pill(text = presentation.statusWord, tone = presentation.tone.toDesignTone())
        }
        FlintText(
            text = presentation.reason,
            style = FlintType.BodyMedium.copy(color = FlintColors.Muted),
        )
        presentation.advisoryHeading?.let {
            AdvisoryBlock(heading = it, body = presentation.advisoryBody)
        }
        if (presentation.isOfferable) {
            AdvisoryBlock(heading = "BEFORE YOU START", body = note)
            OutlineAction(
                text = action,
                onClick = onStart,
                enabled = enabled,
                tone = Tone.Signal,
            )
        }
    }
}

/**
 * The numbers behind the link word: what was decided, and from what. No latency figure, because
 * none has been measured on a phone and a number here would be read as one.
 */
@Composable
private fun SessionRows(diagnostics: SessionDiagnostics) {
    if (diagnostics.ceilingApplied) {
        DiagnosticRow(
            label = DiagnosticsCopy.ROW_CEILING,
            value = SessionDiagnostics.megabits(diagnostics.bitrateCeiling),
        )
    }
    DiagnosticRow(label = DiagnosticsCopy.ROW_RECEIVER_QUEUE, value = "${diagnostics.receiverQueueDepth}")
    DiagnosticRow(label = DiagnosticsCopy.ROW_PENDING, value = SessionDiagnostics.bytes(diagnostics.pendingSendBytes))
    DiagnosticRow(label = DiagnosticsCopy.ROW_DROPPED, value = "${diagnostics.droppedFramesDelta}")
    DiagnosticRow(label = DiagnosticsCopy.ROW_LAST_DECISION, value = diagnostics.lastDecision)
    DiagnosticRow(
        label = DiagnosticsCopy.ROW_THERMAL,
        value = SessionDiagnostics.thermalWord(diagnostics.thermalLevel),
        isLast = true,
    )
}

/**
 * The live strip.
 *
 * Elapsed time, the size actually being encoded, the link's own word for how it is coping, and one
 * unmistakable Stop. Nothing here is a latency figure: none has ever been measured on a phone, and
 * a number on this strip would be read as one.
 */
@Composable
private fun LiveStrip(output: LiveOutput, controller: MobileController) {
    val modeName = when (output.mode) {
        OutputMode.MIRROR -> ScreenCopy.MIRROR_TITLE
        OutputMode.SECOND_SCREEN -> ScreenCopy.SECOND_SCREEN_TITLE
        OutputMode.NONE -> ScreenCopy.MIRROR_TITLE
    }

    InfoCard(borderTone = Tone.Live) {
        Row(modifier = Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            FlintText(text = modeName, style = FlintType.TitleMedium, modifier = Modifier.weight(1f))
            Pill(text = ScreenCopy.LIVE_PILL, tone = Tone.Live)
        }

        FlintText(
            text = ScreenCopy.cockpitLine(modeName.lowercase(), output.deviceName),
            style = FlintType.BodyMedium.copy(color = FlintColors.Muted),
        )

        Row(
            modifier = Modifier
                .fillMaxWidth()
                .semantics { liveRegion = LiveRegionMode.Polite },
            horizontalArrangement = Arrangement.spacedBy(FlintSpace.Large),
        ) {
            Readout(value = PlaybackClock.format(output.elapsedSeconds * 1_000))
            Readout(value = "${output.width}×${output.height}")
            Readout(
                value = ScreenCopy.linkHealthWord(output.health),
                tone = ScreenCopy.linkHealthTone(output.health).toDesignTone(),
            )
        }

        output.degradedReason?.let { AdvisoryBlock(heading = "WHAT IS HAPPENING", body = it) }

        // The cockpit: what the television is showing, when this phone is drawing it.
        output.scene?.let { DiagnosticRow(label = "Showing", value = it.title) }
        DiagnosticRow(label = "Codec", value = CodecNames.label(output.codec))
        if (output.keyFrameFallback) DiagnosticRow(label = "Key frames", value = "Every few seconds")
        DiagnosticRow(
            label = "Bitrate",
            // One decimal place rather than integer division, which printed "0 Mbit/s" for every
            // bitrate the controller can back off to.
            value = "${Decimal.oneDecimal(output.bitrateBitsPerSecond / 1_000_000.0)} Mbit/s",
        )
        SessionRows(output.diagnostics)

        OutlineAction(
            text = when (output.mode) {
                OutputMode.SECOND_SCREEN -> ScreenCopy.STOP_SECOND_SCREEN
                else -> ScreenCopy.STOP_MIRROR
            },
            onClick = controller::stopOutput,
            tone = Tone.Live,
        )
    }
}
