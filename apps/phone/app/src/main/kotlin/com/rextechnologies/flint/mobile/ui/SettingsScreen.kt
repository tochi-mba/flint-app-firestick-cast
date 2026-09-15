package com.rextechnologies.flint.mobile.ui

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import com.rextechnologies.flint.castcore.capability.ReceiverPlatform
import com.rextechnologies.flint.castcore.copy.DiagnosticsCopy
import com.rextechnologies.flint.castcore.copy.FailureCopy
import com.rextechnologies.flint.castcore.copy.MobileTab
import com.rextechnologies.flint.castcore.copy.Placeholders
import com.rextechnologies.flint.castcore.copy.SettingsCopy
import com.rextechnologies.flint.castcore.media.CodecNames
import com.rextechnologies.flint.castcore.setup.ReceiverSetup
import com.rextechnologies.flint.design.AdvisoryBlock
import com.rextechnologies.flint.design.DiagnosticRow
import com.rextechnologies.flint.design.EmptyState
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
import com.rextechnologies.flint.mobile.platform.CrashLog
import com.rextechnologies.flint.protocol.text.Decimal

/** The Settings tab: the receiver on the TV, the checks, the diagnostics and the about card. */
@Composable
fun SettingsScreen(state: MobileUiState, controller: MobileController, activity: MobileActivity) {
    val tab = MobileTab.SETTINGS
    PageHeading(eyebrow = tab.eyebrow, headline = tab.title)

    Spacer(Modifier.height(FlintSpace.Small))
    SectionLabel(SettingsCopy.SECTION_RECEIVER)
    ReceiverSetupCard(state, controller)

    Spacer(Modifier.height(FlintSpace.Small))
    SectionLabel(SettingsCopy.SECTION_CHECKS)
    InfoCard {
        FlintText(
            text = SettingsCopy.SECOND_SCREEN_PROBE_EXPLANATION,
            style = FlintType.BodyMedium,
        )
        // Each button says while it is running rather than looking untouched for a second, and each
        // one's result is announced through the banner rather than only changing a diagnostics row.
        Row(horizontalArrangement = Arrangement.spacedBy(FlintSpace.Small)) {
            OutlineAction(
                text = if (state.secondScreenProbeRunning) {
                    SettingsCopy.PROBE_RUNNING
                } else {
                    SettingsCopy.RUN_SECOND_SCREEN_PROBE
                },
                onClick = { controller.runSecondScreenProbe(activity) },
                enabled = !state.secondScreenProbeRunning,
            )
        }
        FlintText(
            text = SettingsCopy.ENCODER_PROBE_EXPLANATION,
            style = FlintType.BodyMedium,
        )
        OutlineAction(
            text = if (state.encoderProbeRunning) {
                SettingsCopy.PROBE_RUNNING
            } else {
                SettingsCopy.RUN_ENCODER_PROBE
            },
            onClick = { controller.runEncoderProbe(activity) },
            enabled = !state.encoderProbeRunning,
        )
    }

    Spacer(Modifier.height(FlintSpace.Small))
    SectionLabel(DiagnosticsCopy.SECTION_PHONE)
    DiagnosticsCard(state)

    LastFailureCard(activity)

    Spacer(Modifier.height(FlintSpace.Small))
    SectionLabel(SettingsCopy.SECTION_ABOUT)
    InfoCard {
        DiagnosticRow(label = "Version", value = BuildConfig.VERSION_NAME)
        DiagnosticRow(label = "Package", value = BuildConfig.APPLICATION_ID)
        DiagnosticRow(label = "Permissions", value = "No location", isLast = true)
        FlintText(text = SettingsCopy.PERMISSIONS_LINE, style = FlintType.BodySmall)
        Row(horizontalArrangement = Arrangement.spacedBy(FlintSpace.Small)) {
            OutlineAction(
                text = SettingsCopy.REPLAY_INTRODUCTION,
                onClick = controller::replayIntroduction,
            )
        }
        // Removal is offered wherever something is stored, on the same screen as the thing that
        // stores it. A phone that is lent out carries a standing proof that it may cast to somebody
        // else's television until this is pressed.
        OutlineAction(
            text = SettingsCopy.FORGET_PAIRINGS,
            onClick = controller::forgetEveryPairing,
            tone = Tone.Live,
        )
    }
}

/**
 * The last failure this phone recorded, if there was one.
 *
 * Absent entirely when nothing has failed, which is the ordinary case and should not take up a
 * section. Present, it is the only route a fault found on somebody's phone has back to the people
 * who could fix it: there is no crash service behind this app and no way to read its log without a
 * computer, so the text on this card is the whole report.
 */
@Composable
internal fun LastFailureCard(activity: Context) {
    val failures = remember(activity) { CrashLog.observe(activity) }
    val recorded by failures.collectAsState(initial = CrashLog.last(activity))
    val text = recorded ?: return
    var copied by remember(text) { mutableStateOf(false) }

    Spacer(Modifier.height(FlintSpace.Small))
    SectionLabel(FailureCopy.SECTION, tone = Tone.Live)
    InfoCard {
        FlintText(text = FailureCopy.TITLE, style = FlintType.TitleMedium)
        FlintText(text = FailureCopy.BODY, style = FlintType.BodySmall)
        Spacer(Modifier.height(FlintSpace.Small))
        FlintText(text = text, style = FlintType.BodySmall)
        Spacer(Modifier.height(FlintSpace.Small))
        Row(horizontalArrangement = Arrangement.spacedBy(FlintSpace.Small)) {
            OutlineAction(
                text = if (copied) FailureCopy.COPIED else FailureCopy.COPY_ACTION,
                onClick = {
                    val clipboard = activity.getSystemService(ClipboardManager::class.java)
                    clipboard?.setPrimaryClip(ClipData.newPlainText("Flint failure", text))
                    copied = true
                },
            )
            OutlineAction(
                text = FailureCopy.CLEAR_ACTION,
                onClick = {
                    CrashLog.clear(activity)
                },
                tone = Tone.Live,
            )
        }
    }
}

@Composable
private fun ReceiverSetupCard(state: MobileUiState, controller: MobileController) {
    val selected = state.selected
    val deviceName = selected?.displayName ?: "the TV"
    val plan = ReceiverSetup.plan(
        platform = selected?.platform ?: ReceiverPlatform.UNKNOWN,
        stage = state.installStage,
        bundled = state.bundledReceiver,
        deviceName = deviceName,
    )
    val busy = state.setupBusy
    val hasTelevision = selected != null
    // The second press lives here rather than in the state: it is a question to the person holding
    // the phone, and it is withdrawn the moment the television it was about changes.
    var confirmingRemoval by remember(selected?.address) { mutableStateOf(false) }

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
        if (!hasTelevision) FlintText(text = ReceiverSetup.NOTHING_SELECTED, style = FlintType.BodySmall)

        if (confirmingRemoval) {
            AdvisoryBlock(
                heading = "BEFORE REMOVING",
                body = ReceiverSetup.removeConfirmation(
                    packageName = state.installedReceiverPackage ?: state.bundledReceiver?.packageName ?: "Flint",
                    deviceName = deviceName,
                ),
            )
            Row(horizontalArrangement = Arrangement.spacedBy(FlintSpace.Small)) {
                OutlineAction(
                    text = ReceiverSetup.CONFIRM_REMOVE_ACTION,
                    onClick = {
                        confirmingRemoval = false
                        controller.removeReceiver()
                    },
                    enabled = !busy,
                    tone = Tone.Live,
                )
                OutlineAction(text = ReceiverSetup.KEEP_ACTION, onClick = { confirmingRemoval = false })
            }
        } else {
            // Removal is offered wherever installation is, on the same screen. Both wait while the
            // phone is talking to the television rather than starting a second conversation.
            Row(horizontalArrangement = Arrangement.spacedBy(FlintSpace.Small)) {
                plan.installAction?.let {
                    OutlineAction(
                        text = if (busy) ReceiverSetup.WORKING else it,
                        onClick = controller::installReceiver,
                        enabled = !busy && hasTelevision,
                        tone = Tone.Signal,
                    )
                }
                plan.removeAction?.let {
                    OutlineAction(
                        text = it,
                        onClick = { confirmingRemoval = true },
                        enabled = !busy && hasTelevision,
                        tone = Tone.Live,
                    )
                }
            }
            plan.identifyAction?.let {
                OutlineAction(
                    text = if (busy) ReceiverSetup.WORKING else it,
                    onClick = controller::identifyReceiver,
                    enabled = !busy && hasTelevision,
                )
            }
        }
    }
}

@Composable
private fun DiagnosticsCard(state: MobileUiState) {
    val report = state.report
    if (report == null && state.path == null && state.network.boundAddress == null) {
        // Nothing has been measured, and a stack of five "not measured" rows is a worse way of
        // saying so than one sentence that also says what to do about it.
        EmptyState(
            glyph = DiagnosticsCopy.empty.glyph,
            title = DiagnosticsCopy.empty.title,
            body = DiagnosticsCopy.empty.body,
        )
        return
    }

    InfoCard {
        DiagnosticRow(
            label = "Local network",
            value = state.network.boundAddress?.hostAddress ?: Placeholders.NONE,
        )
        DiagnosticRow(
            label = "Encoders",
            value = report?.phone?.hardwareVideoEncoders
                ?.takeIf { it.isNotEmpty() }
                ?.joinToString { CodecNames.label(it) }
                ?: Placeholders.NOT_PROBED,
        )
        DiagnosticRow(
            label = "Encoder check",
            value = report?.phone?.encoderRoundTrip?.let { SettingsCopy.roundTripWord(it) }
                ?: Placeholders.NOT_PROBED,
        )
        DiagnosticRow(
            label = "Second screen",
            value = report?.phone?.virtualDisplayProbe?.name?.lowercase()?.replace('_', ' ')
                ?: Placeholders.NOT_PROBED,
        )
        DiagnosticRow(
            label = "Round trip",
            value = state.path?.let { "${Decimal.whole(it.roundTripMs)} ms" }
                ?: Placeholders.NOT_MEASURED,
        )
        DiagnosticRow(
            label = "Session",
            value = when {
                state.isConnected -> "Connected"
                state.isConnecting -> "Connecting"
                state.sessionFailure != null -> "Ended"
                else -> Placeholders.NONE
            },
        )
        DiagnosticRow(
            label = "Throughput",
            value = state.path?.throughputLabel ?: Placeholders.NOT_MEASURED,
            isLast = true,
        )
        // The round trip's own sentence, when it has one. A row can hold a word; what a decoder
        // made of the frame takes a sentence, and it is the sentence somebody reporting a fault
        // would want to quote.
        report?.phone?.roundTripDetail?.takeIf { it.isNotBlank() }?.let {
            FlintText(text = it, style = FlintType.BodySmall)
        }
        FlintText(text = DiagnosticsCopy.ROUND_TRIP_SOURCE, style = FlintType.BodySmall)
    }
}
