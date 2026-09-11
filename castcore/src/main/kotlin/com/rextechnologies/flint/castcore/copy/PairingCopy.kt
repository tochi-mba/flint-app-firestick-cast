package com.rextechnologies.flint.castcore.copy

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

    const val CONNECTING: String = "Connecting to the TV…"
    const val NO_DEVICE: String =
        "No television is selected, so there is nothing to send a code to. Find one on the Cast tab " +
            "first."
    const val NO_NETWORK: String =
        "This phone is not on a local network, so it cannot reach a television."

    /** After the television has granted a token, which is what makes the next start automatic. */
    fun paired(deviceName: String): String =
        "Paired with $deviceName. Flint will reconnect on its own from now on."

    /** Whether what has been typed is a code at all, without saying whether it is the right one. */
    fun isWellFormed(entered: String): Boolean =
        entered.length == DIGITS && entered.all { it in '0'..'9' }

    /** The digits, grouped in threes, for display only. The field's own value stays the digits. */
    fun grouped(digits: String): String =
        if (digits.length <= DIGITS / 2) {
            digits
        } else {
            digits.substring(0, DIGITS / 2) + " " + digits.substring(DIGITS / 2)
        }
}
