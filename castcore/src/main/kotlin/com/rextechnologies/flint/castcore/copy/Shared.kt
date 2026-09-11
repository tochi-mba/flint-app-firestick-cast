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
