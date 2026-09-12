package com.rextechnologies.flint.castcore.copy

import com.rextechnologies.flint.castcore.capability.ProbeOutcome

/** The Settings tab. */
object SettingsCopy {
    const val SECTION_RECEIVER: String = "Receiver on the TV"
    const val SECTION_CHECKS: String = "Checks"
    const val SECTION_ABOUT: String = "About"
    const val REPLAY_INTRODUCTION: String = "Show the introduction again"
    const val RUN_ENCODER_PROBE: String = "Check this phone's encoder"

    /**
     * What the encoder check does, in full, before it runs.
     *
     * It is two checks in one press. Listing the hardware encoders is quick and proves little; a
     * list of names was exactly what an encoder that produced nothing but green passed. So the same
     * press draws a test pattern through the encoder a session would use and decodes it on this
     * phone, which is the only check that looks at pixels.
     */
    const val ENCODER_PROBE_EXPLANATION: String =
        "Flint lists this phone's hardware video encoders, then draws a test pattern into a display " +
            "only this app can see, encodes it the way a session would, decodes the result on this " +
            "phone and checks the pixels. Nothing appears on the television and nothing is sent " +
            "anywhere."
    const val RUN_SECOND_SCREEN_PROBE: String = "Check the second screen"

    /**
     * What the second-screen check does, in full, before it runs.
     *
     * It is the load-bearing platform claim in the whole design, so it is a check rather than an
     * assertion — and a check somebody is asked to run has to say what it will do to their phone.
     */
    const val SECOND_SCREEN_PROBE_EXPLANATION: String =
        "Flint creates a display only this app can see, draws one frame into it and reads that " +
            "frame back. Nothing appears on the television, nothing is sent anywhere, and the " +
            "display is released as soon as the check finishes."

    const val PERMISSIONS_LINE: String =
        "Flint never scans for Wi-Fi networks, so it never asks for location permission."

    const val PROBE_RUNNING: String = "Checking…"
    const val FORGET_PAIRINGS: String = "Forget every paired TV"
    const val FORGOTTEN: String =
        "Every stored pairing has been removed from this phone, and so has the key televisions " +
            "knew it by over ADB. The televisions themselves are unchanged: they will show a fresh " +
            "code next time, and their own prompt again before this phone can install anything."

    /**
     * What a finished encoder check found, said as a result rather than as a status word.
     *
     * @param found the hardware encoders, by name.
     * @param through the codec the test frame went through, or `null` when none could.
     * @param roundTrip whether the frame survived; [ProbeOutcome.NOT_PROBED] when it could not run.
     * @param detail what the round trip said, or blank.
     */
    fun encoderCheckResult(
        found: List<String>,
        through: String?,
        roundTrip: ProbeOutcome,
        detail: String,
    ): String {
        if (found.isEmpty()) return "This phone reported no hardware video encoder."
        val listed = "This phone can encode ${found.joinToString()} in hardware"
        val codec = through ?: found.first()
        return when (roundTrip) {
            ProbeOutcome.SUPPORTED ->
                "$listed, and a test frame drawn through $codec came back from a decoder intact."

            ProbeOutcome.UNSUPPORTED ->
                "$listed, but a test frame drawn through $codec did not come back from a decoder " +
                    "as what was drawn.${sentence(detail)}"

            ProbeOutcome.NOT_PROBED ->
                "$listed. The test frame could not be drawn, so the encoder's output is " +
                    "unchecked.${sentence(detail)}"
        }
    }

    /** The word the diagnostics row shows for the round trip. */
    fun roundTripWord(outcome: ProbeOutcome): String = when (outcome) {
        ProbeOutcome.NOT_PROBED -> "Not run"
        ProbeOutcome.SUPPORTED -> "Passed"
        ProbeOutcome.UNSUPPORTED -> "Failed"
    }

    /** A detail appended as its own sentence, or nothing at all when there is none. */
    private fun sentence(detail: String): String {
        val trimmed = detail.trim()
        if (trimmed.isEmpty()) return ""
        return if (trimmed.last() in SENTENCE_ENDINGS) " $trimmed" else " $trimmed."
    }

    private val SENTENCE_ENDINGS = charArrayOf('.', '?', '…')

    const val SECOND_SCREEN_PROBE_SUPPORTED: String =
        "This phone gave Flint a display only this app can see, and the frame drawn into it came " +
            "back intact."
    const val SECOND_SCREEN_PROBE_UNSUPPORTED: String =
        "This phone did not produce the frame Flint drew into a display only this app can see, so " +
            "the second screen is not available on it."
}
