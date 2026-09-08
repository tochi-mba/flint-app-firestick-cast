package com.rextechnologies.flint.receiver.browser

import android.view.KeyEvent

/**
 * Translates Flint's portable key vocabulary into Android key codes.
 *
 * The protocol carries a small, closed set of *semantic* keys — up, select, page down — rather than
 * raw key codes, so the host never has to know what a Fire TV expects and a future receiver on
 * another platform can map the same set differently. This is the one place that mapping lives.
 *
 * Two choices here are not the obvious ones, and both were wrong in the obvious form:
 *
 * * Directions map to the **D-pad** codes, not the arrow keys. WebView's focus handling on a
 *   television follows D-pad events; arrow codes work on a keyboard and do nothing useful here.
 * * Refresh maps to **F5**, not `KEYCODE_REFRESH`. WebView does not handle the latter, so a refresh
 *   sent that way is accepted by every layer and silently dropped at the last one.
 */
object BrowserKeyCodes {
    /**
     * The Android key code for a portable key.
     *
     * Total by construction: the enum is closed, and a key with no code would be a control on the
     * host that does nothing on the television with no error anywhere between them.
     */
    fun androidKeyCode(key: BrowserNativeKey): Int = when (key) {
        BrowserNativeKey.UP -> KeyEvent.KEYCODE_DPAD_UP
        BrowserNativeKey.DOWN -> KeyEvent.KEYCODE_DPAD_DOWN
        BrowserNativeKey.LEFT -> KeyEvent.KEYCODE_DPAD_LEFT
        BrowserNativeKey.RIGHT -> KeyEvent.KEYCODE_DPAD_RIGHT
        BrowserNativeKey.SELECT -> KeyEvent.KEYCODE_DPAD_CENTER
        BrowserNativeKey.ENTER -> KeyEvent.KEYCODE_ENTER
        BrowserNativeKey.BACK -> KeyEvent.KEYCODE_BACK
        BrowserNativeKey.TAB -> KeyEvent.KEYCODE_TAB
        BrowserNativeKey.ESCAPE -> KeyEvent.KEYCODE_ESCAPE
        BrowserNativeKey.PAGE_UP -> KeyEvent.KEYCODE_PAGE_UP
        BrowserNativeKey.PAGE_DOWN -> KeyEvent.KEYCODE_PAGE_DOWN
        BrowserNativeKey.HOME -> KeyEvent.KEYCODE_MOVE_HOME
        BrowserNativeKey.END -> KeyEvent.KEYCODE_MOVE_END
        BrowserNativeKey.REFRESH -> KeyEvent.KEYCODE_F5
    }

    /**
     * The meta state for a key stroke.
     *
     * Shift travels as a modifier on the one event rather than as separate down and up presses
     * around it. Synthesising it as three events races the focus move that Tab performs, and
     * Shift-Tab then lands on the wrong element often enough to look intermittent.
     */
    fun metaState(shift: Boolean): Int = if (shift) KeyEvent.META_SHIFT_ON else 0
}
