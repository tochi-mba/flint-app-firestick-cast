package com.rextechnologies.flint.receiver.browser.workspace

import com.rextechnologies.flint.receiver.browser.BrowserDialogAnswer
import com.rextechnologies.flint.receiver.browser.BrowserDialogText
import com.rextechnologies.flint.receiver.browser.PendingJsDialog
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow

/** One native modal at a time, tied to the exact renderer that requested it. */
internal class BrowserWorkspaceDialogs {
    data class Request(val id: Long, val paneId: Long, val rendererToken: Long, val dialog: PendingJsDialog)
    private val mutablePending = MutableStateFlow<Request?>(null)
    val pending = mutablePending.asStateFlow()
    private var nextId = 0L

    fun show(paneId: Long, rendererToken: Long, dialog: PendingJsDialog): Long {
        val id = ++nextId
        val previous = mutablePending.value
        mutablePending.value = Request(id, paneId, rendererToken, dialog.copy(
            message = BrowserDialogText.fromPage(dialog.message).value,
            defaultValue = dialog.defaultValue?.let { BrowserDialogText.fromPage(it).value },
        ))
        // Publish the replacement first. Cancelling page code may synchronously open another
        // dialog; that newer request must cancel this one rather than being overwritten/leaked.
        previous?.dialog?.resolve(BrowserDialogAnswer.Cancel)
        return id
    }

    fun answer(id: Long, answer: BrowserDialogAnswer) {
        val request = mutablePending.value?.takeIf { it.id == id } ?: return
        // Clear before calling page code: callbacks may synchronously ask for another dialog.
        mutablePending.value = null
        request.dialog.resolve(answer)
    }

    fun cancel() {
        mutablePending.value?.let { answer(it.id, BrowserDialogAnswer.Cancel) }
    }
}
