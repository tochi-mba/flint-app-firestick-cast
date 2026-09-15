package com.rextechnologies.flint.receiver.browser

import android.view.KeyEvent
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

/**
 * The translation from Flint's portable key vocabulary to Android key codes.
 *
 * Pure, and tested separately from the WebView because this is where a browser stops responding to
 * a key in a way nobody can see from the host: the message arrives, the reducer accepts it, and the
 * page does nothing because the code sent was wrong or absent. Every key the protocol defines has to
 * land on something.
 */
class BrowserKeyCodesTest {
    @Test
    fun `every portable key maps to an android key code`() {
        // A key the host can send and the receiver cannot dispatch is a dead control on the remote,
        // and the protocol enumerates them precisely so this can be asserted exhaustively.
        for (key in BrowserNativeKey.entries) {
            val code = BrowserKeyCodes.androidKeyCode(key)
            assertNotNull(code, "no Android key code for $key")
            assertTrue(code > 0, "$key mapped to a non-positive code")
        }
    }

    @Test
    fun `directional keys map to the dpad rather than to arrows`() {
        // A Fire TV remote sends D-pad codes, and WebView's focus handling follows them. Sending
        // arrow-key codes instead works on a keyboard and does nothing useful on a television.
        assertEquals(KeyEvent.KEYCODE_DPAD_UP, BrowserKeyCodes.androidKeyCode(BrowserNativeKey.UP))
        assertEquals(KeyEvent.KEYCODE_DPAD_DOWN, BrowserKeyCodes.androidKeyCode(BrowserNativeKey.DOWN))
        assertEquals(KeyEvent.KEYCODE_DPAD_LEFT, BrowserKeyCodes.androidKeyCode(BrowserNativeKey.LEFT))
        assertEquals(KeyEvent.KEYCODE_DPAD_RIGHT, BrowserKeyCodes.androidKeyCode(BrowserNativeKey.RIGHT))
    }

    @Test
    fun `select maps to dpad center so a focused link activates`() {
        assertEquals(
            KeyEvent.KEYCODE_DPAD_CENTER,
            BrowserKeyCodes.androidKeyCode(BrowserNativeKey.SELECT),
        )
    }

    @Test
    fun `back maps to the back key so the page history is used`() {
        assertEquals(KeyEvent.KEYCODE_BACK, BrowserKeyCodes.androidKeyCode(BrowserNativeKey.BACK))
    }

    @Test
    fun `paging and document keys map to their own codes rather than to repeated arrows`() {
        // Synthesising a page down as many down-presses scrolls the wrong amount on every page and
        // moves focus as a side effect.
        assertEquals(KeyEvent.KEYCODE_PAGE_UP, BrowserKeyCodes.androidKeyCode(BrowserNativeKey.PAGE_UP))
        assertEquals(KeyEvent.KEYCODE_PAGE_DOWN, BrowserKeyCodes.androidKeyCode(BrowserNativeKey.PAGE_DOWN))
        assertEquals(KeyEvent.KEYCODE_MOVE_HOME, BrowserKeyCodes.androidKeyCode(BrowserNativeKey.HOME))
        assertEquals(KeyEvent.KEYCODE_MOVE_END, BrowserKeyCodes.androidKeyCode(BrowserNativeKey.END))
    }

    @Test
    fun `tab and escape map to their own codes`() {
        assertEquals(KeyEvent.KEYCODE_TAB, BrowserKeyCodes.androidKeyCode(BrowserNativeKey.TAB))
        assertEquals(KeyEvent.KEYCODE_ESCAPE, BrowserKeyCodes.androidKeyCode(BrowserNativeKey.ESCAPE))
    }

    @Test
    fun `refresh maps to a code the webview will not swallow as navigation`() {
        // F5 rather than KEYCODE_REFRESH: the latter is not handled by WebView, so a refresh key
        // sent that way is silently dropped.
        assertEquals(KeyEvent.KEYCODE_F5, BrowserKeyCodes.androidKeyCode(BrowserNativeKey.REFRESH))
    }

    @Test
    fun `shift is carried as a meta state rather than as a separate key`() {
        // Shift-Tab has to arrive as one event with a modifier. Sending shift down, tab, shift up
        // races the focus move and lands on the wrong element about as often as not.
        assertEquals(0, BrowserKeyCodes.metaState(shift = false))
        assertTrue(BrowserKeyCodes.metaState(shift = true) and KeyEvent.META_SHIFT_ON != 0)
    }

    @Test
    fun `two distinct keys never share a code`() {
        // Sharing one would make two remote buttons do the same thing, which is the kind of defect
        // that survives manual testing because both buttons appear to work.
        val codes = BrowserNativeKey.entries.map(BrowserKeyCodes::androidKeyCode)
        assertEquals(codes.size, codes.toSet().size, "duplicate key codes: $codes")
    }
}
