package com.rextechnologies.flint.castcore.copy

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

    const val PROBE_RUNNING: String = "Checking…"
    const val FORGET_PAIRINGS: String = "Forget every paired TV"
    const val FORGOTTEN: String =
        "Every stored pairing has been removed from this phone. The televisions themselves are " +
            "unchanged and will show a fresh code next time."

    /** What a finished check found, said as a result rather than as a status word. */
    fun encoderProbeResult(found: List<String>): String =
        if (found.isEmpty()) {
            "This phone reported no hardware video encoder."
        } else {
            "This phone can encode ${found.joinToString()} in hardware."
        }

    const val SECOND_SCREEN_PROBE_SUPPORTED: String =
        "This phone gave Flint a private display and the frame drawn into it came back intact."
    const val SECOND_SCREEN_PROBE_UNSUPPORTED: String =
        "This phone did not produce the frame Flint drew into a private display, so the second " +
            "screen is not available on it."
}
