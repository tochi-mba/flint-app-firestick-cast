package com.rextechnologies.flint.castcore.copy

import com.rextechnologies.flint.castcore.capability.CapabilityReport
import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.ToneIntent
import com.rextechnologies.flint.protocol.media.LinkHealth

/**
 * An empty state.
 *
 * The glyph is a letterform rather than an icon, which is what makes an empty screen read as part of
 * the same instrument instead of a gap where a picture failed to load.
 */
data class EmptyStateCopy(val glyph: String, val title: String, val body: String) {
    init {
        require(glyph.isNotBlank() && title.isNotBlank() && body.isNotBlank())
    }
}

/** The four tabs, and the letterform each carries. Taken from the desktop's left rail. */
enum class MobileTab(val glyph: String, val eyebrow: String, val title: String) {
    CAST("C", "LOCAL LINK", "Cast"),
    SCREEN("S", "PHONE OUTPUT", "Screen"),
    MEDIA("M", "LOCAL PLAYBACK", "Media"),
    SETTINGS("R", "LOCAL PREFERENCES", "Settings"),
}

/** What is on screen while nothing has been measured. Never a number, and never a dash pretending. */
object Placeholders {
    const val NOT_MEASURED: String = "Not measured"
    const val NOT_PROBED: String = "Not probed"
    const val NOT_REPORTED: String = "Not reported"
    const val NONE: String = "—"
    const val NO_RECEIVER: String = "No receiver found"
}

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

    fun networkTone(network: LocalNetwork): ToneIntent = when (network) {
        is LocalNetwork.PhoneIsHost -> ToneIntent.SIGNAL
        is LocalNetwork.PhoneIsClient -> ToneIntent.NEUTRAL
        LocalNetwork.NoLocalNetwork -> ToneIntent.LIVE
    }
}

/** The pairing sheet. */
object PairingCopy {
    const val EYEBROW: String = "PAIRING"
    const val TITLE: String = "Enter the code on the TV"
    const val BODY: String =
        "The television is showing six digits. Type them here. The code pairs this phone once, and " +
            "after that Flint reconnects on its own."
    const val INVALID_CODE: String = "Enter the six-digit code shown on the TV."
    const val TIMED_OUT: String =
        "The television stopped waiting. Open Flint on the TV to show a new code, then try again."
    const val DIGITS: Int = 6

    /** Whether what has been typed is a code at all, without saying whether it is the right one. */
    fun isWellFormed(entered: String): Boolean =
        entered.length == DIGITS && entered.all { it in '0'..'9' }
}

/** The Screen tab: mirror and second screen, their live HUD, and their stopped states. */
object ScreenCopy {
    val empty: EmptyStateCopy = EmptyStateCopy(
        glyph = "S",
        title = "Nothing is being sent",
        body = "Pair with a television on the Cast tab, then start a mirror or a second screen here.",
    )

    const val MIRROR_TITLE: String = "Mirror this screen"
    const val SECOND_SCREEN_TITLE: String = "Second screen"
    const val START_MIRROR: String = "Start mirroring"
    const val START_SECOND_SCREEN: String = "Start second screen"
    const val STOP_MIRROR: String = "Stop mirroring"
    const val STOP_SECOND_SCREEN: String = "Stop the second screen"
    const val LIVE_PILL: String = "Live"

    /**
     * The consent sentence, shown before the system dialog rather than after it.
     *
     * Android asks for capture permission every session and will not remember the answer, so the app
     * that explains why is the one a person is less annoyed by the fourth time.
     */
    const val MIRROR_CONSENT: String =
        "Android will ask whether Flint may record this screen. It asks every time and cannot " +
            "remember the answer. Flint encodes the picture and sends it to the television; it is " +
            "not recorded to a file and it does not leave this network."

    const val SECOND_SCREEN_NO_CONSENT: String =
        "A second screen needs no permission, because Flint draws it on a display only Flint can see."

    /** What the television is showing while the phone is the cockpit. */
    fun cockpitLine(surfaceName: String, deviceName: String): String =
        "$deviceName is showing $surfaceName. This phone stays the controller."

    fun linkHealthWord(health: LinkHealth): String = when (health) {
        LinkHealth.Headroom -> "Headroom"
        LinkHealth.Stable -> "Stable"
        LinkHealth.Congested -> "Congested"
    }

    fun linkHealthTone(health: LinkHealth): ToneIntent = when (health) {
        LinkHealth.Headroom, LinkHealth.Stable -> ToneIntent.SIGNAL
        LinkHealth.Congested -> ToneIntent.LIVE
    }

    /** The one sentence that explains a picture getting softer, so it does not read as a fault. */
    const val CONGESTED_EXPLANATION: String =
        "The link is not keeping up, so Flint has lowered the bitrate. The picture will be softer " +
            "until it recovers."
}

/** The Media tab: pick a file, push it, drive the transport. */
object MediaCopy {
    val empty: EmptyStateCopy = EmptyStateCopy(
        glyph = "M",
        title = "Nothing picked yet",
        body = "Choose a video and Flint hands it to the television, which plays it at its original " +
            "quality. Nothing is re-encoded on the way.",
    )

    const val PICK_ACTION: String = "Choose a video"
    const val STOP_ACTION: String = "Stop playback"

    fun pushing(chunksSent: Long, chunksTotal: Long): String {
        require(chunksTotal > 0)
        val percent = ((chunksSent.coerceIn(0, chunksTotal) * 100) / chunksTotal)
        return "Sending to the TV — $percent%."
    }

    /**
     * Why the file is pushed rather than fetched.
     *
     * Shown once, in the advisory block, because the alternative is somebody wondering why a local
     * file takes any time at all to start.
     */
    const val WHY_PUSHED: String =
        "Flint sends the file to the television over the connection this phone opened. Some Fire TV " +
            "builds quietly drop connections the television itself starts to a phone, so sending it " +
            "this way is the route that works rather than the fastest one."
}

/** The Settings tab. */
object SettingsCopy {
    const val SECTION_RECEIVER: String = "Receiver on the TV"
    const val SECTION_CHECKS: String = "Checks"
    const val SECTION_ABOUT: String = "About"
    const val REPLAY_INTRODUCTION: String = "Show the introduction again"
    const val RUN_ENCODER_PROBE: String = "Check this phone's encoders"
    const val RUN_SECOND_SCREEN_PROBE: String = "Check the second screen"

    /**
     * What the second-screen check does, in full, before it runs.
     *
     * It is the load-bearing platform claim in the whole design, so it is a check rather than an
     * assertion — and a check somebody is asked to run has to say what it will do to their phone.
     */
    const val SECOND_SCREEN_PROBE_EXPLANATION: String =
        "Flint creates a display only this app can see, draws one frame into it and encodes that " +
            "frame. Nothing appears on the television, nothing is sent anywhere, and the display is " +
            "released as soon as the check finishes."

    const val PERMISSIONS_LINE: String =
        "Flint never scans for Wi-Fi networks, so it never asks for location permission."
}

/** The Diagnostics sheet. Rows, never prose, and never a number nobody measured. */
object DiagnosticsCopy {
    val empty: EmptyStateCopy = EmptyStateCopy(
        glyph = "—",
        title = "Nothing measured yet",
        body = "Run a probe from the Cast tab and the results appear here.",
    )

    const val SECTION_PHONE: String = "This phone"
    const val SECTION_RECEIVER: String = "Receiver"
    const val SECTION_PATH: String = "Network path"

    /**
     * Why the round trip is the phone's own measurement.
     *
     * The receiver reports a round-trip time of zero in every STATS frame because it never measures
     * one. Passing that through would put a zero on a diagnostics row, which reads as an
     * extraordinarily good link rather than as no measurement at all.
     */
    const val ROUND_TRIP_SOURCE: String =
        "Measured by this phone when it connects. The television does not measure a round trip and " +
            "reports zero, which is not a reading."
}

/** The ongoing notification, which is the only control that exists while the phone is locked. */
object NotificationCopy {
    const val CHANNEL_ID: String = "flint-mobile-session"
    const val CHANNEL_NAME: String = "Casting"
    const val CHANNEL_DESCRIPTION: String =
        "Shows what Flint is sending to the television, and lets you stop it."
    const val STOP_ACTION: String = "Stop"

    fun title(mode: String): String = mode

    fun text(deviceName: String): String = "Sending to $deviceName"
}
