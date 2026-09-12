package com.rextechnologies.flint.mobile

import com.rextechnologies.flint.castcore.capability.CapabilityReport
import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.NetworkPath
import com.rextechnologies.flint.castcore.capability.ReceiverDevice
import com.rextechnologies.flint.castcore.copy.MobileTab
import com.rextechnologies.flint.castcore.discovery.DiscoveryRung
import com.rextechnologies.flint.castcore.media.MediaState
import com.rextechnologies.flint.castcore.media.SessionDiagnostics
import com.rextechnologies.flint.castcore.screen.SecondScreenScene
import com.rextechnologies.flint.castcore.session.SessionFailure
import com.rextechnologies.flint.castcore.setup.BundledReceiver
import com.rextechnologies.flint.castcore.setup.ReceiverInstallStage
import com.rextechnologies.flint.mobile.state.LinkState
import com.rextechnologies.flint.mobile.state.LookupState
import com.rextechnologies.flint.protocol.media.LinkHealth
import com.rextechnologies.flint.protocol.wire.CodecId

/** What the phone is sending, if anything. */
enum class OutputMode {
    NONE,
    MIRROR,
    SECOND_SCREEN,
}

/** The live telemetry strip, which only exists while something is being sent. */
data class LiveOutput(
    val mode: OutputMode,
    val deviceName: String,
    val width: Int,
    val height: Int,
    /** The codec the frames are going out in, chosen by policy rather than by a set's order. */
    val codec: CodecId,
    val elapsedSeconds: Long,
    val health: LinkHealth,
    val bitrateBitsPerSecond: Int,
    val degradedReason: String? = null,
    /** What the television is showing, for a second screen; `null` for a mirror. */
    val scene: SecondScreenScene? = null,
    /** Whether this session has fallen back to a bounded key-frame interval. Never goes back. */
    val keyFrameFallback: Boolean = false,
    /** The numbers behind the link word and the bitrate, for the strip's diagnostic rows. */
    val diagnostics: SessionDiagnostics,
)

/**
 * Everything on screen, in one value.
 *
 * Assembled from the coordinators rather than mutated by them. Each of those owns one question --
 * what the network is, what discovery found, what the phone can do, whether a session is open, what
 * is being sent -- and this is the single place their answers meet, so no screen has to combine them
 * and no two screens can combine them differently.
 */
data class MobileUiState(
    val tab: MobileTab = MobileTab.CAST,
    /** `null` until the stored answer has been read. Neither screen shows while it is unknown. */
    val introductionSeen: Boolean? = null,
    val network: LocalNetwork = LocalNetwork.NoLocalNetwork,
    val lookup: LookupState = LookupState.Idle,
    val manualLookup: LookupState = LookupState.Idle,
    val report: CapabilityReport? = null,
    val receivers: List<ReceiverDevice> = emptyList(),
    val selected: ReceiverDevice? = null,
    val rungsAttempted: List<DiscoveryRung> = emptyList(),
    val path: NetworkPath? = null,
    val link: LinkState = LinkState.Idle,
    val pairingVisible: Boolean = false,
    val pairingError: String? = null,
    val encoderProbeRunning: Boolean = false,
    val secondScreenProbeRunning: Boolean = false,
    val output: LiveOutput? = null,
    val media: MediaState = MediaState.Idle,
    val bundledReceiver: BundledReceiver? = null,
    val installStage: ReceiverInstallStage = ReceiverInstallStage.Unknown,
    val notice: String? = null,
) {
    val isProbing: Boolean
        get() = lookup is LookupState.Running || manualLookup is LookupState.Running

    /** A session is open right now, which is the strongest evidence of reachability there is. */
    val isConnected: Boolean
        get() = link is LinkState.Connected

    val isConnecting: Boolean
        get() = link is LinkState.Connecting

    val sessionFailure: SessionFailure?
        get() = (link as? LinkState.Closed)?.failure

    /**
     * Whether the phone has nothing to show yet.
     *
     * Deliberately not "the list is empty": a probe that ran and found nothing is a different screen
     * from one that has never run, and collapsing the two would lose the only thing worth saying.
     */
    val showEmptyState: Boolean
        get() = report == null && !isProbing && lookup is LookupState.Idle

    /** A probe that ran, tried something, and found nothing. Its own panel, not the empty state. */
    val showProbeFailed: Boolean
        get() = lookup is LookupState.FoundNothing && receivers.isEmpty()

    /** There is no local network at all, which has one thing to do about it and no probe to run. */
    val showNoNetwork: Boolean
        get() = network is LocalNetwork.NoLocalNetwork
}
