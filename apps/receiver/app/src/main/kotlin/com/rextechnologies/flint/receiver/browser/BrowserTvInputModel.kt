package com.rextechnologies.flint.receiver.browser

import android.view.KeyEvent

/** How the D-pad talks to the page. */
enum class BrowserInteractionMode {
    /** A pointer the D-pad steers. Works on any site, including ones with no focus order. */
    CURSOR,

    /** The page's own focus order, walked with Tab. Faster on sites that are built for it. */
    FOCUS,
}

/** A full-screen layer that owns input while it is up. */
enum class BrowserOverlay {
    NONE,
    OMNIBOX,
    TABS,
    MENU,
    FIND,
    BOOKMARKS,
    HISTORY,
    CLEAR_DATA,
    PROFILES,
    PROFILE_NAME,
    PROFILE_DELETE,

    /** Multi-page browser workspace with independent WebViews (ADR-0023). */
    WORKSPACE,

    /** Per-profile network / VPN settings (ADR-0022). */
    NETWORK,
}

/**
 * Everything a key press depends on.
 *
 * Flat and immutable so the whole decision table is a pure function of it, which is what makes the
 * eight-rung Back ladder testable rather than something discovered on a device.
 */
data class BrowserTvState(
    val mode: BrowserInteractionMode = BrowserInteractionMode.CURSOR,
    val chromeVisible: Boolean = false,
    val chromeFocused: Boolean = false,
    val overlay: BrowserOverlay = BrowserOverlay.NONE,
    val dialogOpen: Boolean = false,
    val fullscreen: Boolean = false,
    val noticeVisible: Boolean = false,
    val leaveConfirmVisible: Boolean = false,
    val canGoBack: Boolean = false,
    val isLastTab: Boolean = true,
    val cursorAtTopEdge: Boolean = false,
    /**
     * Whether a text field on the page currently has focus.
     *
     * Reported by the WebView itself when it asks for an input connection, so it needs no injected
     * JavaScript and works on any site.
     */
    val editingFocused: Boolean = false,
)

/** What the surface should do about a key. */
sealed interface TvKeyOutcome {
    data class MoveCursor(val direction: CursorDirection) : TvKeyOutcome
    data object ClickCursor : TvKeyOutcome

    /**
     * Submit whatever is being typed.
     *
     * Distinct from [ClickCursor] because a d-pad centre does not submit a form in Chromium — Enter
     * does. While Fire OS has its keyboard open over a focused field, Select is the "go" key, and
     * this is what makes it behave like one.
     */
    data object CommitText : TvKeyOutcome
    data class SendKey(val key: BrowserSemanticKey) : TvKeyOutcome
    data class ScrollPage(val deltaX: Int, val deltaY: Int) : TvKeyOutcome
    data object ToggleMode : TvKeyOutcome
    data object ShowChrome : TvKeyOutcome
    data object HideChrome : TvKeyOutcome
    data class OpenOverlay(val overlay: BrowserOverlay) : TvKeyOutcome
    data object CloseOverlay : TvKeyOutcome
    data object ExitFullscreen : TvKeyOutcome
    data object DismissNotice : TvKeyOutcome
    data object CancelDialog : TvKeyOutcome
    data object GoBack : TvKeyOutcome
    data object CloseTab : TvKeyOutcome
    data object ConfirmLeave : TvKeyOutcome
    data object CloseBrowser : TvKeyOutcome

    /** Not ours. Compose focus, the WebView, or the system should have it. */
    data object PassThrough : TvKeyOutcome
}

/**
 * The remote control, decided in one place.
 *
 * Two things make a television browser usable or not: what the D-pad means inside a page, and what
 * Back does. Both are here, as data, because both were previously either absent or wrong — Back in
 * particular reached the media key handler and tore the whole session down.
 *
 * Modelled on the existing `mirrorKeyOutcome`, which is this codebase's proven shape for remote
 * handling: a pure function to a small outcome vocabulary, exhaustively tested, with the Android
 * plumbing kept outside.
 */
class BrowserTvInputModel {
    companion object {
        /** How far Up/Down scroll in focus mode, as a fraction of the viewport. */
        const val SCROLL_FRACTION = 0.33f

        /** Nominal scroll step, scaled by the caller against the real viewport height. */
        const val SCROLL_STEP_PIXELS = 320
    }

    fun onKeyDown(state: BrowserTvState, keyCode: Int): TvKeyOutcome {
        if (keyCode == KeyEvent.KEYCODE_BACK) {
            return back(state)
        }
        if (isSystemOwned(keyCode)) {
            return TvKeyOutcome.PassThrough
        }

        // A dialog, leave prompt, overlay, focused chrome, or fullscreen video each own the D-pad
        // while they are up. Stealing it for the cursor is how a television browser traps someone in
        // a modal they cannot reach the buttons of — found on the leave prompt, where Keep browsing
        // / Leave could not be focused and Select clicked the page behind the scrim.
        if (state.dialogOpen ||
            state.leaveConfirmVisible ||
            state.overlay != BrowserOverlay.NONE ||
            state.fullscreen ||
            (state.chromeVisible && state.chromeFocused)
        ) {
            return when (keyCode) {
                KeyEvent.KEYCODE_MENU -> if (state.chromeVisible) TvKeyOutcome.HideChrome else TvKeyOutcome.ShowChrome
                else -> TvKeyOutcome.PassThrough
            }
        }

        return when (keyCode) {
            KeyEvent.KEYCODE_MENU ->
                if (state.chromeVisible) TvKeyOutcome.HideChrome else TvKeyOutcome.ShowChrome

            KeyEvent.KEYCODE_SEARCH -> TvKeyOutcome.OpenOverlay(BrowserOverlay.OMNIBOX)

            KeyEvent.KEYCODE_DPAD_UP -> directionUp(state)
            KeyEvent.KEYCODE_DPAD_DOWN -> direction(state, CursorDirection.DOWN)
            KeyEvent.KEYCODE_DPAD_LEFT -> direction(state, CursorDirection.LEFT)
            KeyEvent.KEYCODE_DPAD_RIGHT -> direction(state, CursorDirection.RIGHT)

            KeyEvent.KEYCODE_DPAD_CENTER, KeyEvent.KEYCODE_ENTER -> when {
                // Typing wins over both interaction modes. A field has focus, the keyboard is up,
                // and the only thing Select can sensibly mean is "done".
                state.editingFocused -> TvKeyOutcome.CommitText
                state.mode == BrowserInteractionMode.CURSOR -> TvKeyOutcome.ClickCursor
                else -> TvKeyOutcome.SendKey(BrowserSemanticKey.SELECT)
            }

            // Media keys go to the page rather than to a player the browser does not own. Chromium
            // maps Select onto the focused media element, which is what makes a remote's
            // play/pause work on an ordinary web video.
            KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE,
            KeyEvent.KEYCODE_MEDIA_PLAY,
            KeyEvent.KEYCODE_MEDIA_PAUSE,
            -> TvKeyOutcome.SendKey(BrowserSemanticKey.SELECT)

            KeyEvent.KEYCODE_MEDIA_FAST_FORWARD -> TvKeyOutcome.SendKey(BrowserSemanticKey.PAGE_DOWN)
            KeyEvent.KEYCODE_MEDIA_REWIND -> TvKeyOutcome.SendKey(BrowserSemanticKey.PAGE_UP)
            KeyEvent.KEYCODE_PAGE_DOWN -> TvKeyOutcome.SendKey(BrowserSemanticKey.PAGE_DOWN)
            KeyEvent.KEYCODE_PAGE_UP -> TvKeyOutcome.SendKey(BrowserSemanticKey.PAGE_UP)

            else -> TvKeyOutcome.PassThrough
        }
    }

    fun onKeyLongPress(state: BrowserTvState, keyCode: Int): TvKeyOutcome {
        // Chrome, overlays, the leave prompt and dialogs own Select. Flipping pointer/link mode
        // under them is how a held Select on the omnibar hid the cursor and made Up/Down jump the
        // page instead.
        if (state.dialogOpen ||
            state.leaveConfirmVisible ||
            state.overlay != BrowserOverlay.NONE ||
            state.fullscreen ||
            (state.chromeVisible && state.chromeFocused)
        ) {
            return TvKeyOutcome.PassThrough
        }
        return when (keyCode) {
            // The mode switch lives on the button people already hold while browsing the page.
            KeyEvent.KEYCODE_DPAD_CENTER, KeyEvent.KEYCODE_ENTER -> TvKeyOutcome.ToggleMode
            KeyEvent.KEYCODE_BACK -> TvKeyOutcome.OpenOverlay(BrowserOverlay.TABS)
            else -> TvKeyOutcome.PassThrough
        }
    }

    /**
     * Back, in the order a viewer expects to undo things.
     *
     * Each rung answers "what is the smallest thing I just did that this should undo". Getting the
     * order wrong is how a browser loses someone's session to a stray press.
     */
    private fun back(state: BrowserTvState): TvKeyOutcome = when {
        state.noticeVisible -> TvKeyOutcome.DismissNotice
        state.dialogOpen -> TvKeyOutcome.CancelDialog
        state.fullscreen -> TvKeyOutcome.ExitFullscreen
        state.overlay != BrowserOverlay.NONE -> TvKeyOutcome.CloseOverlay
        state.leaveConfirmVisible -> TvKeyOutcome.CloseBrowser
        state.chromeVisible -> TvKeyOutcome.HideChrome
        state.canGoBack -> TvKeyOutcome.GoBack
        !state.isLastTab -> TvKeyOutcome.CloseTab
        else -> TvKeyOutcome.ConfirmLeave
    }

    private fun directionUp(state: BrowserTvState): TvKeyOutcome =
        if (state.mode == BrowserInteractionMode.CURSOR && state.cursorAtTopEdge && !state.chromeVisible) {
            // Reaching the top of the page is how phone browsers summon their address bar. The same
            // gesture is the most discoverable route to chrome a d-pad has.
            TvKeyOutcome.ShowChrome
        } else {
            direction(state, CursorDirection.UP)
        }

    private fun direction(state: BrowserTvState, direction: CursorDirection): TvKeyOutcome =
        when (state.mode) {
            BrowserInteractionMode.CURSOR -> TvKeyOutcome.MoveCursor(direction)
            BrowserInteractionMode.FOCUS -> when (direction) {
                CursorDirection.LEFT -> TvKeyOutcome.SendKey(BrowserSemanticKey.SHIFT_TAB)
                CursorDirection.RIGHT -> TvKeyOutcome.SendKey(BrowserSemanticKey.TAB)
                // Same Avalonia/host convention as edge scroll: positive Y scrolls up.
                CursorDirection.UP -> TvKeyOutcome.ScrollPage(0, SCROLL_STEP_PIXELS)
                CursorDirection.DOWN -> TvKeyOutcome.ScrollPage(0, -SCROLL_STEP_PIXELS)
            }
        }

    /**
     * Keys that belong to Fire OS.
     *
     * Home and volume are the system's, and the microphone is Alexa's. Taking any of them would be
     * both broken and, for Home, grounds for rejection.
     */
    private fun isSystemOwned(keyCode: Int): Boolean = when (keyCode) {
        KeyEvent.KEYCODE_HOME,
        KeyEvent.KEYCODE_VOLUME_UP,
        KeyEvent.KEYCODE_VOLUME_DOWN,
        KeyEvent.KEYCODE_VOLUME_MUTE,
        KeyEvent.KEYCODE_POWER,
        -> true
        else -> false
    }
}
