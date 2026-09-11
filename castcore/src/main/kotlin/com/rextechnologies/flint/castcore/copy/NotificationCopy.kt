package com.rextechnologies.flint.castcore.copy

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
