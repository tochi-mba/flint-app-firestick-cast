package com.rextechnologies.flint.castcore.copy

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

/**
 * What to say when something failed in a way nobody anticipated.
 *
 * The type of the failure is named rather than its message, on purpose: a message is often blank,
 * or is a sentence written for a log rather than for a person, while the type is short, always
 * present, and is the one part worth quoting when passing the fault on. The whole stack is recorded
 * on the phone either way, because this app runs on devices its authors cannot reach.
 */
object FailureCopy {
    fun unexpectedFailure(type: String): String =
        "Something in Flint failed in a way it did not expect ($type). That action stopped; the " +
            "rest of the app is still running. Settings has the details."

    const val SECTION: String = "LAST FAILURE"
    const val TITLE: String = "Flint recorded a failure"

    const val BODY: String =
        "The most recent failure on this phone, kept so it can be reported. It has not been sent " +
            "anywhere and will not be. Copy it if somebody is fixing it, or clear it once it is no " +
            "longer useful."

    const val COPY_ACTION: String = "Copy the details"
    const val CLEAR_ACTION: String = "Clear it"
    const val COPIED: String = "Copied. Paste it wherever the fault is being reported."
}
