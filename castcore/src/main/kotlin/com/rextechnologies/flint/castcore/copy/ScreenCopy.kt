package com.rextechnologies.flint.castcore.copy

import com.rextechnologies.flint.castcore.capability.ToneIntent
import com.rextechnologies.flint.protocol.media.LinkHealth

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

    const val STARTING: String = "Starting…"

    const val NOT_PAIRED: String =
        "This phone is not connected to a television yet. Pair with one on the Cast tab, then come " +
            "back here."

    /**
     * What to say when somebody declined the system capture dialog.
     *
     * Not an error, and not phrased as one: declining is a legitimate answer to a question about
     * recording your own screen, and the only thing Flint has to say is that nothing started.
     */
    const val MIRROR_CONSENT_DECLINED: String =
        "Android was not given permission to record this screen, so nothing was started. Starting " +
            "the mirror again asks once more."

    const val NOTIFICATIONS_DECLINED: String =
        "Notifications are switched off for Flint, so the ongoing notification will not appear and " +
            "the Stop control will not be reachable from a locked phone. The cast itself is " +
            "unaffected."

    /** What the banner says when the socket stopped taking frames and the output was stopped for it. */
    const val LINK_STOPPED: String =
        "The television stopped accepting frames, so Flint has stopped sending them. Check that it " +
            "is still awake and on this phone's hotspot, then start again."

    /** Said once per session, on the strip, when the encoder has been restarted with a bounded GOP. */
    fun keyFrameFallback(seconds: Int): String =
        "This phone's encoder ignored a request for a key frame, so Flint has restarted it to send " +
            "one every $seconds seconds for the rest of this session."

    /** What the live strip says when the encoder itself stopped, rather than the link. */
    fun encoderFailure(detail: String): String =
        if (detail.isBlank()) {
            "This phone's encoder stopped without saying why, so the session has ended."
        } else {
            "This phone's encoder stopped: $detail"
        }
}
