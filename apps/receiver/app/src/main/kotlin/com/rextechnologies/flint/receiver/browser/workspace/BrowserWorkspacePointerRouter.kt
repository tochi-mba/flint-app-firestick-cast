package com.rextechnologies.flint.receiver.browser.workspace

import com.rextechnologies.flint.protocol.wire.BrowserPointerAction
import com.rextechnologies.flint.receiver.browser.BrowserNativeInput

/** A touch gesture belongs to its original pane; focus changes must never retarget its release. */
internal class BrowserWorkspacePointerRouter(private val send: (Long, BrowserNativeInput) -> Unit) {
    private var active: Pair<Long, BrowserNativeInput.Pointer>? = null

    fun dispatch(paneId: Long, input: BrowserNativeInput) {
        if (input !is BrowserNativeInput.Pointer) {
            send(paneId, input)
            return
        }
        when (input.action) {
            BrowserPointerAction.DOWN -> {
                cancel()
                active = paneId to input
            }
            BrowserPointerAction.UP, BrowserPointerAction.CANCEL -> {
                if (active?.first != paneId) return
                active = null
            }
            BrowserPointerAction.MOVE -> {
                if (input.buttons != 0 && active?.first != paneId) return
                if (active?.first == paneId) active = paneId to input
            }
        }
        send(paneId, input)
    }

    fun cancel() {
        val (paneId, pointer) = active ?: return
        active = null
        send(paneId, pointer.copy(action = BrowserPointerAction.CANCEL, buttons = 0))
    }
}
