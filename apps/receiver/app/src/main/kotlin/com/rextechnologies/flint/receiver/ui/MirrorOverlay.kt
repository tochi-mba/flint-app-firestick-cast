package com.rextechnologies.flint.receiver.ui

import android.view.KeyEvent

/**
 * What a remote key press should do while a mirror fills the screen.
 *
 * A mirror is the one surface with no controls of its own: the whole point is an unobstructed
 * picture. So the controls stay hidden until the viewer reaches for them, which on a television
 * means pressing the D-pad — the same gesture every streaming app uses to summon a HUD.
 */
internal enum class MirrorKeyOutcome {
    /** Bring the controls up, swallowing the press that summoned them. */
    REVEAL_CONTROLS,

    /** Put the controls away, leaving the picture unobstructed again. */
    HIDE_CONTROLS,

    /** Leave the mirror entirely. */
    STOP_MIRRORING,

    /** Not ours: let focus navigation, a button, or the service decide. */
    IGNORE,
}

/**
 * Decides what a key press means on the mirror surface.
 *
 * Split out from the composable because this is where the behaviour someone would actually
 * complain about lives, and a pure function can be tested for every key without a television.
 *
 * Two rules carry the whole design:
 *
 * * The press that reveals the controls is consumed rather than also acted on. Otherwise the
 *   first D-pad press would both summon the controls and immediately move focus off the button
 *   it landed on, so the button a viewer aimed at is never the one they get.
 * * BACK means "the smallest step back". With the controls up that is putting them away; with
 *   them already hidden there is nothing left to close but the mirror itself. Treating BACK as
 *   "stop mirroring" in both cases makes glancing at the controls a one-press route to killing
 *   the session.
 *
 * @param keyCode An `android.view.KeyEvent` key code.
 * @param controlsVisible Whether the controls are currently on screen.
 */
internal fun mirrorKeyOutcome(keyCode: Int, controlsVisible: Boolean): MirrorKeyOutcome =
    if (controlsVisible) {
        when (keyCode) {
            KeyEvent.KEYCODE_BACK,
            KeyEvent.KEYCODE_MENU,
            KeyEvent.KEYCODE_INFO,
            -> MirrorKeyOutcome.HIDE_CONTROLS
            // Everything else, D-pad included, belongs to the focused button now.
            else -> MirrorKeyOutcome.IGNORE
        }
    } else {
        when (keyCode) {
            KeyEvent.KEYCODE_DPAD_UP,
            KeyEvent.KEYCODE_DPAD_DOWN,
            KeyEvent.KEYCODE_DPAD_LEFT,
            KeyEvent.KEYCODE_DPAD_RIGHT,
            KeyEvent.KEYCODE_DPAD_CENTER,
            KeyEvent.KEYCODE_ENTER,
            KeyEvent.KEYCODE_MENU,
            KeyEvent.KEYCODE_INFO,
            -> MirrorKeyOutcome.REVEAL_CONTROLS
            KeyEvent.KEYCODE_BACK -> MirrorKeyOutcome.STOP_MIRRORING
            else -> MirrorKeyOutcome.IGNORE
        }
    }

/**
 * How a mirrored desktop is fitted to a television that is a different shape.
 *
 * A 16:10 laptop panel on a 16:9 television leaves bars on two sides. Which trade is right is a
 * matter of taste rather than correctness — see the whole picture, or fill the whole screen — so
 * it is offered as a control instead of decided here.
 */
internal enum class MirrorFitMode {
    /** The whole desktop, letterboxed. Nothing is cropped. */
    FIT,

    /** The whole screen, cropping whichever edge does not fit. */
    FILL,
    ;

    /** The mode this one toggles to, so the control needs no ordering logic of its own. */
    fun toggled(): MirrorFitMode = if (this == FIT) FILL else FIT

    /** What the button offers to do next, rather than the state it is already in. */
    val actionLabel: String get() = if (this == FIT) "FILL SCREEN" else "FIT SCREEN"
}

/**
 * The aspect ratio to render a mirrored frame at.
 *
 * Returns null when the sender has not reported a frame size yet, which is the state between a
 * mirror session opening and its first frame arriving. Guessing 16:9 during that window made the
 * picture visibly jump the moment the real size arrived, so callers are told to wait instead.
 */
internal fun mirrorAspectRatio(width: Int, height: Int): Float? =
    if (width > 0 && height > 0) width.toFloat() / height else null

/**
 * A one-line summary of the live stream, for the controls' stats row.
 *
 * Reports what is actually known rather than a plausible-looking placeholder: a mirror that
 * silently showed "1920x1080" while receiving nothing is the exact failure this project spent an
 * evening chasing.
 */
internal fun mirrorStatsLabel(width: Int, height: Int, frameReceived: Boolean): String = when {
    !frameReceived -> "Waiting for the first frame"
    width > 0 && height > 0 -> "${width} x ${height}"
    else -> "Streaming, size not reported"
}
