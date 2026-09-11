package com.rextechnologies.flint.mobile

import com.rextechnologies.flint.castcore.capability.CapabilityReport
import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.NetworkPath
import com.rextechnologies.flint.castcore.capability.ReceiverDevice
import com.rextechnologies.flint.castcore.copy.MobileTab
import com.rextechnologies.flint.castcore.discovery.DiscoveryRung
import com.rextechnologies.flint.castcore.session.SessionFailure
import com.rextechnologies.flint.castcore.setup.BundledReceiver
import com.rextechnologies.flint.castcore.setup.ReceiverInstallStage
import com.rextechnologies.flint.protocol.media.LinkHealth

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
    val elapsedSeconds: Long,
    val health: LinkHealth,
    val bitrateBitsPerSecond: Int,
    val degradedReason: String? = null,
)

/** Everything on screen, in one value. */
data class MobileUiState(
    val tab: MobileTab = MobileTab.CAST,
    val introductionSeen: Boolean = false,
    val network: LocalNetwork = LocalNetwork.NoLocalNetwork,
    val isProbing: Boolean = false,
    val report: CapabilityReport? = null,
    val receivers: List<ReceiverDevice> = emptyList(),
    val selected: ReceiverDevice? = null,
    val rungsAttempted: List<DiscoveryRung> = emptyList(),
    val path: NetworkPath? = null,
    val isPaired: Boolean = false,
    val pairingVisible: Boolean = false,
    val pairingError: String? = null,
    val sessionFailure: SessionFailure? = null,
    val output: LiveOutput? = null,
    val bundledReceiver: BundledReceiver? = null,
    val installStage: ReceiverInstallStage = ReceiverInstallStage.UNKNOWN,
    val installDetail: String = "",
    val notice: String? = null,
) {
    /**
     * Whether the phone has nothing to show yet.
     *
     * Deliberately not "the list is empty": a probe that ran and found nothing is a different screen
     * from one that has never run, and collapsing the two would lose the only thing worth saying.
     */
    val showEmptyState: Boolean
        get() = report == null && !isProbing && sessionFailure == null
}
