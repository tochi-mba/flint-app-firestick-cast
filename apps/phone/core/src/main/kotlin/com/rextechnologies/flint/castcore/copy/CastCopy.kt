package com.rextechnologies.flint.castcore.copy

import com.rextechnologies.flint.castcore.capability.CapabilityReport
import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.ToneIntent
import com.rextechnologies.flint.castcore.discovery.DiscoveryRung

/** The Cast tab: the network, the television, and the verdict for each mode. */
object CastCopy {
    val empty: EmptyStateCopy = EmptyStateCopy(
        glyph = "TV",
        title = "Nothing probed yet",
        body = "Flint has not looked for a television on this network. It will say what it finds, " +
            "and what it tried when it finds nothing.",
    )

    const val SECTION_MODES: String = "What this pair can do"
    const val SECTION_NETWORK: String = "This network"
    const val SECTION_RECEIVERS: String = "Televisions found"
    const val PROBE_ACTION: String = "Find my TV"
    const val PAIR_ACTION: String = "Enter the code"
    const val MANUAL_ACTION: String = "Type an address"

    /** Mirrors the desktop's heading states exactly, so the two products read the same. */
    fun headingStatus(isProbing: Boolean, report: CapabilityReport?): String = when {
        isProbing -> "Probing"
        report == null -> Placeholders.NOT_PROBED
        report.device == null -> "Connect TV"
        report.anythingOfferable -> "Ready"
        else -> "Limited"
    }

    fun headingTone(report: CapabilityReport?): ToneIntent =
        if (report?.anythingOfferable == true) ToneIntent.SIGNAL else ToneIntent.NEUTRAL

    /**
     * One sentence about which end of the network this phone is on.
     *
     * Worth saying out loud on the good path as well as the bad one. Most people have been told that
     * casting needs everything on the same Wi-Fi, and being the access point is better than that
     * rather than a compromise.
     */
    fun networkLine(network: LocalNetwork): String = when (network) {
        is LocalNetwork.PhoneIsHost ->
            "This phone is the access point, on ${network.selected.interfaceName}. An access point " +
                "can always reach its own clients, so the setting that breaks casting on a shared " +
                "network does not apply here."

        is LocalNetwork.PhoneIsClient ->
            "This phone is a client of a network somebody else runs, on ${network.interfaceName}. " +
                "That works, but if the network keeps its clients from reaching each other the " +
                "television will never answer."

        LocalNetwork.NoLocalNetwork ->
            "This phone is not on a local network. Mobile data cannot carry a cast, because the " +
                "television has to be on the same link."
    }

    const val PROBE_FAILED_TITLE: String = "Nothing answered"
    const val HOTSPOT_ACTION: String = "Open hotspot settings"
    const val MANUAL_CHECKING: String = "Checking…"

    /**
     * What a probe that found nothing actually tried.
     *
     * A probe that says only "nothing found" is indistinguishable from one that never ran, and the
     * difference is the whole reason the ladder has named rungs.
     */
    fun probeFailedBody(rungsAttempted: List<DiscoveryRung>): String {
        if (rungsAttempted.isEmpty()) {
            return "Flint had no way to look on this network, so it did not try. Nothing was sent."
        }
        val names = rungsAttempted.joinToString { rungName(it) }
        return "Flint tried $names and nothing answered. The television may be asleep, on another " +
            "network, or not running the receiver yet."
    }

    fun rungName(rung: DiscoveryRung): String = when (rung) {
        DiscoveryRung.LINE_PROBE -> "a sweep of this subnet"
        DiscoveryRung.MULTICAST_DNS -> "multicast DNS"
        DiscoveryRung.UDP_BROADCAST -> "a broadcast"
        DiscoveryRung.SSDP -> "SSDP"
        DiscoveryRung.MANUAL -> "the address you typed"
    }

    /** The one thing to do when there is no local network at all, offered rather than described. */
    const val NO_NETWORK_TITLE: String = "No local network"
    const val NO_NETWORK_BODY: String =
        "A cast needs the television on the same link as this phone. Turning on this phone's " +
            "hotspot and joining the Fire TV to it is the arrangement Flint is built around."

    fun networkTone(network: LocalNetwork): ToneIntent = when (network) {
        is LocalNetwork.PhoneIsHost -> ToneIntent.SIGNAL
        is LocalNetwork.PhoneIsClient -> ToneIntent.NEUTRAL
        LocalNetwork.NoLocalNetwork -> ToneIntent.LIVE
    }
}
