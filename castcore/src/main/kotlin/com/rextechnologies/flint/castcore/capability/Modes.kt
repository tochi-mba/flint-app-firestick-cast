package com.rextechnologies.flint.castcore.capability

/**
 * The things Flint can be asked to do from a phone.
 *
 * The same three modes the Windows host offers, named the same way on purpose: a person who has
 * used Flint on a PC should not have to learn a second vocabulary to use it from a phone.
 */
enum class CastMode {
    /** Mirror the phone's screen to the television with the lowest achievable latency. */
    MIRROR,

    /** Drive the television as a second screen the phone renders into, with the phone as cockpit. */
    SECOND_SCREEN,

    /** Play a local media file on the television at original quality, without transcoding. */
    MEDIA_HANDOFF,
}

/** Whether a [CastMode] is available on a specific phone, network and television. */
enum class ModeStatus {
    /** Everything this mode needs is present and proven. */
    AVAILABLE,

    /**
     * Something this mode needs is missing, and the person holding the phone can probably fix it —
     * the hotspot is off, the TV has not authorised this phone, a probe has not been run yet.
     */
    BLOCKED,

    /** This phone or this television can never do this. Not a diagnostic to act on; a fact to report. */
    IMPOSSIBLE,

    /** The capability is unavailable in this build. */
    NOT_IMPLEMENTED,
}

/**
 * Whether one [CastMode] is possible on one phone-and-television pair, and why.
 *
 * @property mode the mode being judged.
 * @property status the verdict.
 * @property reason a complete, user-facing sentence shown verbatim, so it has to read as an
 *   explanation rather than a diagnostic code and must never overstate what Flint knows.
 * @property remedy what the person can do about it, when there is something. Always `null` when the
 *   status is [ModeStatus.IMPOSSIBLE] or [ModeStatus.NOT_IMPLEMENTED], because offering a remedy for
 *   something no action can change is a lie told in a helpful tone.
 */
data class ModeVerdict(
    val mode: CastMode,
    val status: ModeStatus,
    val reason: String,
    val remedy: String? = null,
) {
    init {
        require(reason.isNotBlank()) { "A verdict without a reason is not a verdict" }
        require(remedy == null || remedy.isNotBlank()) { "A blank remedy is worse than none" }
        require(status != ModeStatus.IMPOSSIBLE || remedy == null) {
            "An impossibility cannot carry a remedy"
        }
        require(status != ModeStatus.NOT_IMPLEMENTED || remedy == null) {
            "Waiting for an update is not something a person can do, so it is not a remedy"
        }
    }

    /** Whether this mode may be offered as a working control rather than a disabled one. */
    val isOfferable: Boolean
        get() = status == ModeStatus.AVAILABLE
}

/**
 * The single word the UI puts in a mode's pill.
 *
 * Four distinct words for four distinct outcomes. "Blocked" and "Not possible" have to stay
 * different words, because the first asks the person to go and do something and the second asks
 * them to stop trying.
 */
object StatusWord {
    const val AVAILABLE: String = "Ready"
    const val BLOCKED: String = "Blocked"
    const val IMPOSSIBLE: String = "Not possible"
    const val NOT_IMPLEMENTED: String = "Coming soon"

    fun of(status: ModeStatus): String = when (status) {
        ModeStatus.AVAILABLE -> AVAILABLE
        ModeStatus.BLOCKED -> BLOCKED
        ModeStatus.IMPOSSIBLE -> IMPOSSIBLE
        ModeStatus.NOT_IMPLEMENTED -> NOT_IMPLEMENTED
    }
}

/**
 * The semantic colour a status earns.
 *
 * A semantic name rather than a colour, so a status can never be painted an off-palette one, and
 * declared here rather than in `:design` so the rule "an impossible mode is never painted like an
 * available one" is unit-testable without an Android runtime. `:design` maps these onto the palette.
 */
enum class ToneIntent {
    /** No claim. Used for what has not been probed and for what has not shipped. */
    NEUTRAL,

    /** The accent. Only a genuinely available thing earns it. */
    SIGNAL,

    /** The failure tone. Blocked and impossible share it; a planned feature never does. */
    LIVE,

    /** Hairline only, for a card carrying no status at all. */
    LINE,
}

/**
 * Everything a mode card needs, derived from a verdict.
 *
 * Four mechanisms keep this honest, and they are separate on purpose:
 *
 * 1. Only [ModeStatus.AVAILABLE] is painted [ToneIntent.SIGNAL].
 * 2. [isOfferable] is the single gate on whether a control is enabled.
 * 3. Four distinct words, so "blocked" and "not possible" cannot read as the same thing.
 * 4. [isComingSoon] and [hasRemedy] are two independent booleans rather than one branch. Nothing here
 *    makes them mutually exclusive; they are exclusive only because the assessor never attaches a
 *    remedy to an unfinished or impossible verdict. Collapsing them into one branch would throw away
 *    the ability to catch it the day one of them does.
 */
data class ModePresentation(
    val mode: CastMode,
    val title: String,
    val statusWord: String,
    val tone: ToneIntent,
    val reason: String,
    val isOfferable: Boolean,
    val isComingSoon: Boolean,
    val hasRemedy: Boolean,
    val remedy: String,
) {
    /** The heading of the advisory block, or `null` when the card has nothing to advise. */
    val advisoryHeading: String?
        get() = when {
            isComingSoon -> NO_ACTION_NEEDED
            hasRemedy -> WHAT_TO_DO
            else -> null
        }

    /** The body of the advisory block, or an empty string when there is no block. */
    val advisoryBody: String
        get() = when {
            isComingSoon -> COMING_SOON_BODY
            hasRemedy -> remedy
            else -> ""
        }

    companion object {
        const val WHAT_TO_DO: String = "WHAT TO DO"

        /**
         * Not "COMING SOON" again — the pill above already says that. The block's job is to answer the
         * question the pill provokes, which is whether the reader should go and do something.
         */
        const val NO_ACTION_NEEDED: String = "NO ACTION NEEDED"

        const val COMING_SOON_BODY: String =
            "Nothing to change on your phone or your TV — this arrives in a Flint update."

        fun of(verdict: ModeVerdict): ModePresentation = ModePresentation(
            mode = verdict.mode,
            title = titleOf(verdict.mode),
            statusWord = StatusWord.of(verdict.status),
            tone = toneOf(verdict.status),
            reason = verdict.reason,
            isOfferable = verdict.isOfferable,
            isComingSoon = verdict.status == ModeStatus.NOT_IMPLEMENTED,
            hasRemedy = !verdict.remedy.isNullOrBlank(),
            remedy = verdict.remedy.orEmpty(),
        )

        /** Plain English, never an enum name. */
        fun titleOf(mode: CastMode): String = when (mode) {
            CastMode.MIRROR -> "Mirror this screen"
            CastMode.SECOND_SCREEN -> "Second screen"
            CastMode.MEDIA_HANDOFF -> "Play a file on the TV"
        }

        /**
         * Not one-for-one with the status.
         *
         * Blocked and impossible share the failure tone, because both mean the mode is not happening.
         * What separates them on screen is the absence of a WHAT TO DO block, not the colour. Only the
         * unfinished case falls through to neutral: live is the failure tone, and a planned feature is
         * not a failure.
         */
        fun toneOf(status: ModeStatus): ToneIntent = when (status) {
            ModeStatus.AVAILABLE -> ToneIntent.SIGNAL
            ModeStatus.BLOCKED, ModeStatus.IMPOSSIBLE -> ToneIntent.LIVE
            ModeStatus.NOT_IMPLEMENTED -> ToneIntent.NEUTRAL
        }
    }
}
