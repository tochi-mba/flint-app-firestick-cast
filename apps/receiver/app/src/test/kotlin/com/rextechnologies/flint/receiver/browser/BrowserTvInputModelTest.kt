package com.rextechnologies.flint.receiver.browser

import android.view.KeyEvent
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertTrue

/**
 * What every button on the remote does, in every state the browser can be in.
 *
 * Exhaustive on purpose. A television browser has no second input device to fall back on, so a key
 * that does the wrong thing in one state is not a rough edge — it is the viewer stuck. The Back
 * ladder in particular has eight rungs and every one of them is asserted here.
 */
class BrowserTvInputModelTest {
    private val model = BrowserTvInputModel()

    // ---- Cursor mode -------------------------------------------------------

    @Test
    fun `in cursor mode the d-pad drives the pointer`() {
        val browsing = browsing()

        assertEquals(
            TvKeyOutcome.MoveCursor(CursorDirection.UP),
            model.onKeyDown(browsing, KeyEvent.KEYCODE_DPAD_UP),
        )
        assertEquals(
            TvKeyOutcome.MoveCursor(CursorDirection.RIGHT),
            model.onKeyDown(browsing, KeyEvent.KEYCODE_DPAD_RIGHT),
        )
    }

    @Test
    fun `in cursor mode select clicks where the pointer is`() {
        assertEquals(TvKeyOutcome.ClickCursor, model.onKeyDown(browsing(), KeyEvent.KEYCODE_DPAD_CENTER))
    }

    @Test
    fun `holding select swaps the interaction mode`() {
        // The escape hatch for a page the cursor cannot work: real element focus, on the same
        // button, without going near a menu.
        assertEquals(TvKeyOutcome.ToggleMode, model.onKeyLongPress(browsing(), KeyEvent.KEYCODE_DPAD_CENTER))
    }

    // ---- Focus mode --------------------------------------------------------

    @Test
    fun `in focus mode left and right walk the page's own focus order`() {
        val focus = browsing().copy(mode = BrowserInteractionMode.FOCUS)

        assertEquals(
            TvKeyOutcome.SendKey(BrowserSemanticKey.TAB),
            model.onKeyDown(focus, KeyEvent.KEYCODE_DPAD_RIGHT),
        )
        assertEquals(
            TvKeyOutcome.SendKey(BrowserSemanticKey.SHIFT_TAB),
            model.onKeyDown(focus, KeyEvent.KEYCODE_DPAD_LEFT),
        )
    }

    @Test
    fun `in focus mode up and down scroll the page`() {
        val focus = browsing().copy(mode = BrowserInteractionMode.FOCUS)

        val down = model.onKeyDown(focus, KeyEvent.KEYCODE_DPAD_DOWN)

        val scroll = assertIs<TvKeyOutcome.ScrollPage>(down)
        assertTrue(scroll.deltaY < 0)
        assertEquals(0, scroll.deltaX)
    }

    @Test
    fun `in focus mode select activates whatever the page has focused`() {
        val focus = browsing().copy(mode = BrowserInteractionMode.FOCUS)

        assertEquals(
            TvKeyOutcome.SendKey(BrowserSemanticKey.SELECT),
            model.onKeyDown(focus, KeyEvent.KEYCODE_DPAD_CENTER),
        )
    }

    // ---- Chrome ------------------------------------------------------------

    @Test
    fun `menu brings the chrome up and puts it away again`() {
        val browsing = browsing()

        assertEquals(TvKeyOutcome.ShowChrome, model.onKeyDown(browsing, KeyEvent.KEYCODE_MENU))
        assertEquals(
            TvKeyOutcome.HideChrome,
            model.onKeyDown(browsing.copy(chromeVisible = true), KeyEvent.KEYCODE_MENU),
        )
    }

    @Test
    fun `pushing up against the top of the page reveals the chrome`() {
        // The gesture every phone browser taught people, translated to a d-pad: reach the top and
        // the address bar comes to you.
        val atTop = browsing().copy(cursorAtTopEdge = true)

        assertEquals(TvKeyOutcome.ShowChrome, model.onKeyDown(atTop, KeyEvent.KEYCODE_DPAD_UP))
    }

    @Test
    fun `search opens the address entry directly`() {
        assertEquals(
            TvKeyOutcome.OpenOverlay(BrowserOverlay.OMNIBOX),
            model.onKeyDown(browsing(), KeyEvent.KEYCODE_SEARCH),
        )
    }

    @Test
    fun `while the chrome has focus the d-pad belongs to the chrome`() {
        val chrome = browsing().copy(chromeVisible = true, chromeFocused = true)

        assertEquals(TvKeyOutcome.PassThrough, model.onKeyDown(chrome, KeyEvent.KEYCODE_DPAD_RIGHT))
    }

    @Test
    fun `holding select on focused chrome does not flip pointer mode`() {
        val chrome = browsing().copy(chromeVisible = true, chromeFocused = true)

        assertEquals(
            TvKeyOutcome.PassThrough,
            model.onKeyLongPress(chrome, KeyEvent.KEYCODE_DPAD_CENTER),
        )
    }

    @Test
    fun `holding select on an overlay does not flip pointer mode`() {
        val menu = browsing().copy(overlay = BrowserOverlay.MENU, chromeVisible = true)

        assertEquals(
            TvKeyOutcome.PassThrough,
            model.onKeyLongPress(menu, KeyEvent.KEYCODE_DPAD_CENTER),
        )
    }

    @Test
    fun `while the leave prompt is up the d-pad belongs to Keep browsing and Leave`() {
        // Without this the cursor moved behind the scrim, Select clicked the page, and Keep
        // browsing never dismissed the prompt — reproduced on a Fire TV Stick 4K.
        val leaving = browsing().copy(leaveConfirmVisible = true)

        assertEquals(TvKeyOutcome.PassThrough, model.onKeyDown(leaving, KeyEvent.KEYCODE_DPAD_RIGHT))
        assertEquals(TvKeyOutcome.PassThrough, model.onKeyDown(leaving, KeyEvent.KEYCODE_DPAD_CENTER))
        assertEquals(
            TvKeyOutcome.PassThrough,
            model.onKeyLongPress(leaving, KeyEvent.KEYCODE_DPAD_CENTER),
        )
    }

    // ---- The Back ladder ---------------------------------------------------

    @Test
    fun `back dismisses a notice before anything else`() {
        val state = browsing().copy(
            noticeVisible = true,
            dialogOpen = true,
            fullscreen = true,
            chromeVisible = true,
            canGoBack = true,
        )

        assertEquals(TvKeyOutcome.DismissNotice, model.onKeyDown(state, KeyEvent.KEYCODE_BACK))
    }

    @Test
    fun `back answers a dialog before leaving fullscreen`() {
        val state = browsing().copy(dialogOpen = true, fullscreen = true, canGoBack = true)

        assertEquals(TvKeyOutcome.CancelDialog, model.onKeyDown(state, KeyEvent.KEYCODE_BACK))
    }

    @Test
    fun `back leaves fullscreen before closing an overlay`() {
        val state = browsing().copy(
            fullscreen = true,
            overlay = BrowserOverlay.TABS,
            chromeVisible = true,
            canGoBack = true,
        )

        assertEquals(TvKeyOutcome.ExitFullscreen, model.onKeyDown(state, KeyEvent.KEYCODE_BACK))
    }

    @Test
    fun `back closes an overlay before hiding the chrome`() {
        val state = browsing().copy(overlay = BrowserOverlay.OMNIBOX, chromeVisible = true, canGoBack = true)

        assertEquals(TvKeyOutcome.CloseOverlay, model.onKeyDown(state, KeyEvent.KEYCODE_BACK))
    }

    @Test
    fun `back hides the chrome before walking history`() {
        val state = browsing().copy(chromeVisible = true, canGoBack = true)

        assertEquals(TvKeyOutcome.HideChrome, model.onKeyDown(state, KeyEvent.KEYCODE_BACK))
    }

    @Test
    fun `back walks history when there is history to walk`() {
        val state = browsing().copy(canGoBack = true)

        assertEquals(TvKeyOutcome.GoBack, model.onKeyDown(state, KeyEvent.KEYCODE_BACK))
    }

    @Test
    fun `back closes a spare tab rather than the whole browser`() {
        val state = browsing().copy(canGoBack = false, isLastTab = false)

        assertEquals(TvKeyOutcome.CloseTab, model.onKeyDown(state, KeyEvent.KEYCODE_BACK))
    }

    @Test
    fun `back on the last page of the last tab asks before leaving`() {
        // Never a silent exit. Losing a browsing session to one stray press is the thing this rung
        // exists to prevent.
        val state = browsing().copy(canGoBack = false, isLastTab = true)

        assertEquals(TvKeyOutcome.ConfirmLeave, model.onKeyDown(state, KeyEvent.KEYCODE_BACK))
    }

    @Test
    fun `back confirms the leave once it has been asked`() {
        val state = browsing().copy(canGoBack = false, isLastTab = true, leaveConfirmVisible = true)

        assertEquals(TvKeyOutcome.CloseBrowser, model.onKeyDown(state, KeyEvent.KEYCODE_BACK))
    }

    // ---- Media and transport ----------------------------------------------

    @Test
    fun `media keys reach the page so a video answers the remote`() {
        val browsing = browsing()

        assertEquals(
            TvKeyOutcome.SendKey(BrowserSemanticKey.SELECT),
            model.onKeyDown(browsing, KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE),
        )
    }

    @Test
    fun `fast forward and rewind page through a document`() {
        val browsing = browsing()

        assertEquals(
            TvKeyOutcome.SendKey(BrowserSemanticKey.PAGE_DOWN),
            model.onKeyDown(browsing, KeyEvent.KEYCODE_MEDIA_FAST_FORWARD),
        )
        assertEquals(
            TvKeyOutcome.SendKey(BrowserSemanticKey.PAGE_UP),
            model.onKeyDown(browsing, KeyEvent.KEYCODE_MEDIA_REWIND),
        )
    }

    @Test
    fun `while fullscreen the d-pad belongs to the video player, not the cursor`() {
        val state = browsing().copy(fullscreen = true)

        assertEquals(TvKeyOutcome.PassThrough, model.onKeyDown(state, KeyEvent.KEYCODE_DPAD_RIGHT))
    }

    @Test
    fun `a dialog owns the d-pad so its buttons can be reached`() {
        val state = browsing().copy(dialogOpen = true)

        assertEquals(TvKeyOutcome.PassThrough, model.onKeyDown(state, KeyEvent.KEYCODE_DPAD_DOWN))
    }

    // ---- Keys that are not ours -------------------------------------------

    @Test
    fun `volume and home stay with the system`() {
        val browsing = browsing()
        val systemKeys = listOf(
            KeyEvent.KEYCODE_HOME,
            KeyEvent.KEYCODE_VOLUME_UP,
            KeyEvent.KEYCODE_VOLUME_DOWN,
            KeyEvent.KEYCODE_VOLUME_MUTE,
        )

        systemKeys.forEach { key ->
            assertEquals(TvKeyOutcome.PassThrough, model.onKeyDown(browsing, key), "key $key")
        }
    }

    @Test
    fun `an unknown key is never swallowed`() {
        assertEquals(TvKeyOutcome.PassThrough, model.onKeyDown(browsing(), KeyEvent.KEYCODE_F12))
    }

    @Test
    fun `a long press on anything but select is not a mode change`() {
        val browsing = browsing()

        assertEquals(TvKeyOutcome.PassThrough, model.onKeyLongPress(browsing, KeyEvent.KEYCODE_DPAD_UP))
    }

    @Test
    fun `long pressing back opens the tab switcher`() {
        // A shortcut worth having: the fastest route between two sites, without walking the chrome.
        assertEquals(
            TvKeyOutcome.OpenOverlay(BrowserOverlay.TABS),
            model.onKeyLongPress(browsing(), KeyEvent.KEYCODE_BACK),
        )
    }

    // ---- Typing ------------------------------------------------------------

    @Test
    fun `while a text field has focus select commits the text instead of clicking`() {
        // Fire OS opens its own keyboard the moment a page field takes focus. In that state the
        // remote's Select is the "go" key, and a d-pad centre does not submit a form in Chromium —
        // Enter does. The same button therefore has to mean Enter while typing.
        val typing = browsing().copy(editingFocused = true)

        assertEquals(TvKeyOutcome.CommitText, model.onKeyDown(typing, KeyEvent.KEYCODE_DPAD_CENTER))
        assertEquals(TvKeyOutcome.CommitText, model.onKeyDown(typing, KeyEvent.KEYCODE_ENTER))
    }

    @Test
    fun `with no field focused select still clicks the pointer`() {
        assertEquals(TvKeyOutcome.ClickCursor, model.onKeyDown(browsing(), KeyEvent.KEYCODE_DPAD_CENTER))
    }

    @Test
    fun `typing does not steal the d-pad from the cursor`() {
        // Only Select changes meaning. Moving the pointer while a field is focused is still moving
        // the pointer — someone may be aiming at a different field.
        val typing = browsing().copy(editingFocused = true)

        assertEquals(
            TvKeyOutcome.MoveCursor(CursorDirection.DOWN),
            model.onKeyDown(typing, KeyEvent.KEYCODE_DPAD_DOWN),
        )
    }

    @Test
    fun `back still leaves a focused field rather than committing it`() {
        // Back must never submit. It walks the ladder exactly as it does otherwise.
        val typing = browsing().copy(editingFocused = true, canGoBack = true)

        assertEquals(TvKeyOutcome.GoBack, model.onKeyDown(typing, KeyEvent.KEYCODE_BACK))
    }

    private fun browsing() = BrowserTvState()
}
