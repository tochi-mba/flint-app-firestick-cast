package com.rextechnologies.flint.castcore.screen

/**
 * What the television shows while the phone is the cockpit.
 *
 * A scene is a value with no Android in it, so what the second screen can show is a closed list that
 * can be tested, and the phone's cockpit and the television's renderer are two views of one thing
 * rather than two things kept in step by hand. Every scene is Flint's own content: a private display
 * shows only what the app that owns it draws, and nothing here pretends otherwise.
 */
sealed interface SecondScreenScene {
    /** What the cockpit calls the scene. */
    val title: String

    /** The connection, the link and who is driving, for a television with nothing else to show. */
    data class Dashboard(
        val televisionName: String,
        val phoneName: String,
        val linkWord: String,
    ) : SecondScreenScene {
        init {
            require(linkWord.isNotBlank()) { "The link has a word for how it is coping" }
        }

        override val title: String get() = DASHBOARD_TITLE

        /** What to call the television on its own screen. */
        val televisionLabel: String get() = televisionName.ifBlank { SceneCopy.THIS_TELEVISION }

        val phoneLabel: String get() = phoneName.ifBlank { SceneCopy.THIS_PHONE }
    }

    /** What is playing on the television, driven by the receiver's own playback reports. */
    data class NowPlaying(
        val mediaTitle: String,
        val stateWord: String,
        val positionMs: Long,
        val durationMs: Long,
    ) : SecondScreenScene {
        init {
            require(stateWord.isNotBlank())
            require(positionMs >= 0)
            require(durationMs >= UNKNOWN_DURATION)
        }

        override val title: String get() = NOW_PLAYING_TITLE

        /** How far through, as a fraction, or `null` while the duration is unknown. */
        val progress: Double?
            get() = if (durationMs > 0) (positionMs.toDouble() / durationMs).coerceIn(0.0, 1.0) else null

        /** "3:07 / 1:24:00", or just the position while the duration is unknown. */
        val clock: String
            get() = if (durationMs > 0) {
                "${PlaybackClock.format(positionMs)} / ${PlaybackClock.format(durationMs)}"
            } else {
                PlaybackClock.format(positionMs)
            }

        val mediaLabel: String get() = mediaTitle.ifBlank { SceneCopy.UNTITLED }
    }

    companion object {
        const val DASHBOARD_TITLE: String = "Dashboard"
        const val NOW_PLAYING_TITLE: String = "Now playing"
        const val UNKNOWN_DURATION: Long = -1
    }
}

/** The television's own words, kept beside the scenes they belong to and swept by the copy tests. */
object SceneCopy {
    const val EYEBROW: String = "SECOND SCREEN"
    const val THIS_TELEVISION: String = "This television"
    const val THIS_PHONE: String = "This phone"
    const val UNTITLED: String = "Untitled"
    const val DRIVEN_BY: String = "Driven from"
    const val LINK: String = "Link"

    /** What a person glancing at the television needs to know about the mode, and no more. */
    const val BOUNDARY: String =
        "This screen is Flint's own. The phone stays the controller, and the television shows " +
            "what Flint draws rather than the phone's home screen or other apps."
}

/** Elapsed and total time, formatted the one way both the cockpit and the television use. */
object PlaybackClock {
    fun format(millis: Long): String {
        require(millis >= 0)
        val totalSeconds = millis / MILLIS_PER_SECOND
        val hours = totalSeconds / SECONDS_PER_HOUR
        val minutes = (totalSeconds % SECONDS_PER_HOUR) / SECONDS_PER_MINUTE
        val seconds = totalSeconds % SECONDS_PER_MINUTE
        return if (hours > 0) {
            "$hours:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}"
        } else {
            "$minutes:${seconds.toString().padStart(2, '0')}"
        }
    }

    private const val MILLIS_PER_SECOND = 1_000L
    private const val SECONDS_PER_MINUTE = 60L
    private const val SECONDS_PER_HOUR = 3_600L
}
