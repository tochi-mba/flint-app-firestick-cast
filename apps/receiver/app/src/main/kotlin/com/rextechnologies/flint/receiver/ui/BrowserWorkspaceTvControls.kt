package com.rextechnologies.flint.receiver.ui

import android.view.KeyEvent
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.rextechnologies.flint.protocol.wire.BrowserPointerAction
import com.rextechnologies.flint.receiver.browser.*

/** Transient remote/keyboard state. Page ownership and persistence remain in the workspace session. */
internal class BrowserWorkspaceTvControls(
    private val viewport: () -> Pair<Int, Int>?,
    private val send: (BrowserNativeInput) -> Unit,
    private val navigate: (String) -> Unit,
    private val searchEngine: () -> BrowserSearchEngine = { BrowserSearchEngine.DEFAULT },
) {
    val keyboard = BrowserKeyboard()
    var addressOpen by mutableStateOf(false)
        private set
    var keyboardState by mutableStateOf(BrowserKeyboardState())
        private set
    var cursor by mutableStateOf(CursorState())
        private set
    var pressed by mutableStateOf(false)
        private set
    private val engine = BrowserCursorEngine()

    fun openAddress(url: String) {
        cancelGesture()
        keyboardState = BrowserKeyboardState(text = url)
        addressOpen = true
    }

    fun closeAddress() {
        addressOpen = false
    }

    fun resetPointer() {
        cancelGesture()
        cursor = engine.centre(measured())
    }

    fun cancelGesture() {
        if (pressed) send(BrowserNativeInput.Pointer(BrowserPointerAction.CANCEL, cursor.x, cursor.y, 0))
        pressed = false
        cursor = engine.release(cursor)
    }

    fun onKey(key: Int, down: Boolean, repeat: Int = 0): Boolean {
        if (addressOpen) {
            if (!down) return isDirection(key) || isSelect(key) || key == KeyEvent.KEYCODE_BACK
            if (key == KeyEvent.KEYCODE_BACK) {
                closeAddress()
                return true
            }
            direction(key)?.let {
                keyboardState = keyboardState.copy(cursor = keyboard.move(keyboardState.cursor, it, keyboardState.page))
                return true
            }
            if (isSelect(key)) {
                if (repeat > 0) return true
                val selected = keyboard.keyAt(keyboardState.cursor, keyboardState.page)
                if (selected == BrowserKey.Submit) {
                    val result = BrowserQueryResolver().resolve(keyboard.submit(keyboardState), searchEngine())
                    when (result) {
                        is ResolvedQuery.Navigate -> navigate(result.url)
                        is ResolvedQuery.Search -> navigate(result.url)
                        ResolvedQuery.Empty -> Unit
                    }
                    addressOpen = false
                } else {
                    keyboardState = keyboard.press(keyboardState, selected)
                }
                return true
            }
            return false
        }
        direction(key)?.let { dir ->
            if (down && repeat == 0) {
                if (cursor.x == 0 && cursor.y == 0) cursor = engine.centre(measured())
                applyStep(engine.tap(cursor, dir, measured()))
            } else if (!down) {
                cursor = engine.release(cursor)
            }
            return true
        }
        if (isSelect(key)) {
            if (down && repeat == 0 && !pressed) {
                if (cursor.x == 0 && cursor.y == 0) cursor = engine.centre(measured())
                pressed = true
                send(BrowserNativeInput.Pointer(BrowserPointerAction.DOWN, cursor.x, cursor.y, 1))
            } else if (!down && pressed) {
                pressed = false
                send(BrowserNativeInput.Pointer(BrowserPointerAction.UP, cursor.x, cursor.y, 0))
            }
            return true
        }
        return false
    }

    fun frame(elapsedMs: Long) {
        if (!addressOpen &&
            cursor.direction != null
        ) {
            applyStep(engine.hold(cursor, elapsedMs.coerceIn(1, 64), measured()))
        }
    }

    private fun applyStep(step: CursorStep) {
        cursor = step.state
        send(BrowserNativeInput.Pointer(BrowserPointerAction.MOVE, cursor.x, cursor.y, if (pressed) 1 else 0))
        if (step.scrollX != 0 ||
            step.scrollY != 0
        ) {
            send(BrowserNativeInput.Scroll(cursor.x, cursor.y, step.scrollX, step.scrollY))
        }
    }

    private fun measured(): CursorViewport =
        viewport()?.let { CursorViewport(it.first, it.second) } ?: CursorViewport(0, 0)
    private fun direction(key: Int): CursorDirection? = when (key) {
        KeyEvent.KEYCODE_DPAD_UP -> CursorDirection.UP
        KeyEvent.KEYCODE_DPAD_DOWN -> CursorDirection.DOWN
        KeyEvent.KEYCODE_DPAD_LEFT -> CursorDirection.LEFT
        KeyEvent.KEYCODE_DPAD_RIGHT -> CursorDirection.RIGHT
        else -> null
    }
    private fun isDirection(key: Int) = direction(key) != null
    private fun isSelect(key: Int) = key == KeyEvent.KEYCODE_DPAD_CENTER || key == KeyEvent.KEYCODE_ENTER
}
