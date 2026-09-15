package com.rextechnologies.flint.castcore.copy

/**
 * Sound on a mirror, said honestly.
 *
 * Playback capture arrived in Android 10 and captures only from apps that allow it, so there are
 * four different reasons a mirror may be silent and one of them is that nothing is playing. Each
 * gets its own word on the row and its own sentence under it, because "no sound" with no reason
 * reads as a fault in Flint whether or not it is one.
 */
object AudioCopy {
    const val ROW: String = "Sound"

    const val OFF: String = "Off"
    const val PLATFORM_TOO_OLD: String = "Not on this Android"
    const val PERMISSION_DENIED: String = "Not allowed"
    const val CAPTURING: String = "Listening"
    const val SOUNDING: String = "On"
    const val FAILED: String = "Stopped"

    const val SECOND_SCREEN_SILENT: String =
        "A second screen carries no sound. It is Flint's own picture, and there is nothing playing " +
            "behind it to capture."

    const val PLATFORM_TOO_OLD_SENTENCE: String =
        "Capturing what other apps play needs Android 10 or later, so this mirror carries the " +
            "picture only."

    const val PERMISSION_DENIED_SENTENCE: String =
        "Flint was not allowed to record audio, so this mirror carries the picture only. Android " +
            "puts playback capture behind the microphone permission; Flint captures what apps play, " +
            "not the microphone."

    const val CAPTURING_SENTENCE: String =
        "Sound is being captured, but nothing audible has come through yet. An app may opt out of " +
            "capture, in which case its picture is sent without its sound."

    fun failedSentence(detail: String): String =
        if (detail.isBlank()) {
            "Sound stopped without saying why. The picture continues without it."
        } else {
            "Sound stopped: $detail. The picture continues without it."
        }
}
