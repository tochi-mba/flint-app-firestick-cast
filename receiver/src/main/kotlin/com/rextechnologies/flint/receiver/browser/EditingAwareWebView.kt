package com.rextechnologies.flint.receiver.browser

import android.annotation.SuppressLint
import android.content.Context
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputConnection
import android.webkit.WebView

/**
 * A page that says when one of its text fields has focus.
 *
 * Android asks a view for an [InputConnection] exactly when the input method needs to send it text,
 * and a `WebView` answers with one only while an editable element is focused. That makes this the
 * one signal for "the viewer is typing" that costs nothing and needs no injected JavaScript —
 * ADR-0006 forbids reaching into the page, and reaching into it would not work on every site anyway.
 *
 * Knowing it matters twice over: the remote's Select becomes Enter while a field is focused, and the
 * desktop can start forwarding keystrokes without anyone having to arm anything.
 */
@SuppressLint("ViewConstructor")
internal class EditingAwareWebView(
    context: Context,
    private val onEditingChanged: (Boolean) -> Unit,
) : WebView(context) {
    private var editing = false

    override fun onCreateInputConnection(outAttrs: EditorInfo): InputConnection? {
        val connection = super.onCreateInputConnection(outAttrs)
        // A null connection is the WebView saying nothing editable is focused.
        report(connection != null)
        return connection
    }

    override fun onWindowFocusChanged(hasWindowFocus: Boolean) {
        super.onWindowFocusChanged(hasWindowFocus)
        // Losing the window ends any typing session. Leaving the flag set would keep the desktop
        // forwarding keystrokes at a page that is no longer listening.
        if (!hasWindowFocus) {
            report(false)
        }
    }

    override fun onFocusChanged(focused: Boolean, direction: Int, previouslyFocusedRect: android.graphics.Rect?) {
        super.onFocusChanged(focused, direction, previouslyFocusedRect)
        if (!focused) {
            report(false)
        }
    }

    /** Reports only on a change, so a page that asks repeatedly does not publish repeatedly. */
    private fun report(value: Boolean) {
        if (editing == value) {
            return
        }
        editing = value
        onEditingChanged(value)
    }
}
