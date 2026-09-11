package com.rextechnologies.flint.mobile.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import com.rextechnologies.flint.castcore.capability.ReceiverPlatform
import com.rextechnologies.flint.castcore.copy.DiagnosticsCopy
import com.rextechnologies.flint.castcore.copy.MobileTab
import com.rextechnologies.flint.castcore.copy.Placeholders
import com.rextechnologies.flint.castcore.copy.SettingsCopy
import com.rextechnologies.flint.castcore.setup.ReceiverSetup
import com.rextechnologies.flint.design.AdvisoryBlock
import com.rextechnologies.flint.design.DiagnosticRow
import com.rextechnologies.flint.design.FlintSpace
import com.rextechnologies.flint.design.FlintText
import com.rextechnologies.flint.design.FlintType
import com.rextechnologies.flint.design.InfoCard
import com.rextechnologies.flint.design.OutlineAction
import com.rextechnologies.flint.design.PageHeading
import com.rextechnologies.flint.design.SectionLabel
import com.rextechnologies.flint.design.Tone
import com.rextechnologies.flint.mobile.BuildConfig
import com.rextechnologies.flint.mobile.MobileActivity
import com.rextechnologies.flint.mobile.MobileController
import com.rextechnologies.flint.mobile.MobileUiState

/** The Settings tab: the receiver on the TV, the checks, the diagnostics and the about card. */
@Composable
fun SettingsScreen(state: MobileUiState, controller: MobileController, activity: MobileActivity) {
    val tab = MobileTab.SETTINGS
    PageHeading(eyebrow = tab.eyebrow, headline = tab.title)

    Spacer(Modifier.height(FlintSpace.Small))
    SectionLabel(SettingsCopy.SECTION_RECEIVER)
    ReceiverSetupCard(state)

    Spacer(Modifier.height(FlintSpace.Small))
    SectionLabel(SettingsCopy.SECTION_CHECKS)
    InfoCard {
        FlintText(
            text = SettingsCopy.SECOND_SCREEN_PROBE_EXPLANATION,
            style = FlintType.BodyMedium,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(FlintSpace.Small)) {
            OutlineAction(
                text = SettingsCopy.RUN_SECOND_SCREEN_PROBE,
                onClick = { controller.runSecondScreenProbe(activity) },
            )
        }
        OutlineAction(
            text = SettingsCopy.RUN_ENCODER_PROBE,
            onClick = controller::runEncoderProbe,
        )
    }

    Spacer(Modifier.height(FlintSpace.Small))
    SectionLabel(DiagnosticsCopy.SECTION_PHONE)
    DiagnosticsCard(state)

    Spacer(Modifier.height(FlintSpace.Small))
    SectionLabel(SettingsCopy.SECTION_ABOUT)
    InfoCard {
        DiagnosticRow(label = "Version", value = BuildConfig.VERSION_NAME)
        DiagnosticRow(label = "Package", value = BuildConfig.APPLICATION_ID)
        DiagnosticRow(label = "Permissions", value = "No location", isLast = true)
        FlintText(text = SettingsCopy.PERMISSIONS_LINE, style = FlintType.BodySmall)
        OutlineAction(
            text = SettingsCopy.REPLAY_INTRODUCTION,
            onClick = controller::replayIntroduction,
        )
    }
}

@Composable
private fun ReceiverSetupCard(state: MobileUiState) {
    val plan = ReceiverSetup.plan(
        platform = state.selected?.platform ?: ReceiverPlatform.UNKNOWN,
        stage = state.installStage,
        bundled = state.bundledReceiver,
        deviceName = state.selected?.displayName ?: "the TV",
        failureDetail = state.installDetail,
    )

    InfoCard(borderTone = if (plan.installAction != null) Tone.Signal else Tone.Line) {
        FlintText(text = plan.headline, style = FlintType.TitleMedium)
        FlintText(text = plan.body, style = FlintType.BodyMedium)

        if (plan.disclosure.isNotEmpty()) {
            AdvisoryBlock(
                heading = "WHAT WILL BE INSTALLED",
                body = plan.disclosure.joinToString(separator = "\n"),
            )
        }

        plan.remedy?.let { AdvisoryBlock(heading = "WHAT TO DO", body = it) }

        // Removal is offered wherever installation is, on the same screen, and both are disabled
        // rather than hidden: installing over ADB is the next slice's work.
        Row(horizontalArrangement = Arrangement.spacedBy(FlintSpace.Small)) {
            plan.installAction?.let {
                OutlineAction(text = it, onClick = { }, enabled = false, tone = Tone.Signal)
            }
            plan.removeAction?.let {
                OutlineAction(text = it, onClick = { }, enabled = false, tone = Tone.Live)
            }
        }
    }
}

@Composable
private fun DiagnosticsCard(state: MobileUiState) {
    val report = state.report
    InfoCard {
        DiagnosticRow(
            label = "Local network",
            value = state.network.boundAddress?.hostAddress ?: Placeholders.NONE,
        )
        DiagnosticRow(
            label = "Encoders",
            value = report?.phone?.hardwareVideoEncoders
                ?.takeIf { it.isNotEmpty() }
                ?.joinToString { codecName(it.value) }
                ?: Placeholders.NOT_PROBED,
        )
        DiagnosticRow(
            label = "Second screen",
            value = report?.phone?.virtualDisplayProbe?.name?.lowercase()?.replace('_', ' ')
                ?: Placeholders.NOT_PROBED,
        )
        DiagnosticRow(
            label = "Round trip",
            value = state.path?.let { "${it.roundTripMs.toLong()} ms" } ?: Placeholders.NOT_MEASURED,
        )
        DiagnosticRow(
            label = "Throughput",
            value = state.path?.throughputLabel ?: Placeholders.NOT_MEASURED,
            isLast = true,
        )
        FlintText(text = DiagnosticsCopy.ROUND_TRIP_SOURCE, style = FlintType.BodySmall)
    }
}

private fun codecName(value: Int): String = when (value) {
    1 -> "H.264"
    2 -> "H.265"
    3 -> "AAC-LC"
    4 -> "Opus"
    5 -> "AV1"
    else -> "Codec $value"
}
