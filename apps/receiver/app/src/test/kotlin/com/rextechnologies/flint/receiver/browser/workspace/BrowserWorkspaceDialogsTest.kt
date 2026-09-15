package com.rextechnologies.flint.receiver.browser.workspace

import com.rextechnologies.flint.receiver.browser.*
import kotlin.test.*

class BrowserWorkspaceDialogsTest {
    private val dialogs = BrowserWorkspaceDialogs()
    private val answers = mutableListOf<BrowserDialogAnswer>()
    private fun show(kind: BrowserDialogKind = BrowserDialogKind.CONFIRM): Long = dialogs.show(
        1,
        7,
        PendingJsDialog(kind, "https://example.com", "Continue?", null, answers::add),
    )

    @Test fun `confirm resolves once and clears modal`() {
        val id = show()
        dialogs.answer(id, BrowserDialogAnswer.Confirm)
        dialogs.answer(id, BrowserDialogAnswer.Cancel)
        assertEquals(listOf<BrowserDialogAnswer>(BrowserDialogAnswer.Confirm), answers)
        assertNull(dialogs.pending.value)
    }

    @Test fun `replacing dialog cancels previous result`() {
        show()
        show()
        assertEquals(listOf<BrowserDialogAnswer>(BrowserDialogAnswer.Cancel), answers)
        assertNotNull(dialogs.pending.value)
    }

    @Test fun `stale timeout cannot dismiss new dialog`() {
        val old = show()
        val current = show()
        dialogs.answer(old, BrowserDialogAnswer.Cancel)
        assertEquals(current, dialogs.pending.value?.id)
        assertEquals(1, answers.size)
    }

    @Test fun `cancel on teardown is idempotent`() {
        show()
        dialogs.cancel()
        dialogs.cancel()
        assertEquals(listOf<BrowserDialogAnswer>(BrowserDialogAnswer.Cancel), answers)
    }

    @Test fun `prompt preserves empty response rather than substituting default`() {
        val id = show(BrowserDialogKind.PROMPT)
        dialogs.answer(id, BrowserDialogAnswer.Prompt(""))
        assertEquals(listOf<BrowserDialogAnswer>(BrowserDialogAnswer.Prompt("")), answers)
    }

    @Test fun `page text is bounded and sanitized`() {
        dialogs.show(
            2,
            8,
            PendingJsDialog(
                BrowserDialogKind.ALERT,
                "https://example.com",
                "a\u0000".repeat(5000),
                "\u0000test",
                answers::add,
            ),
        )
        val request = assertNotNull(dialogs.pending.value)
        assertTrue(request.dialog.message.toByteArray().size <= 4096)
        assertFalse(request.dialog.message.contains('\u0000'))
        assertEquals("test", request.dialog.defaultValue)
    }

    @Test fun `resolve callback can open next dialog without it being cleared`() {
        val id = dialogs.show(
            1,
            7,
            PendingJsDialog(
                BrowserDialogKind.ALERT,
                "https://example.com",
                "First",
                null,
            ) { show() },
        )
        dialogs.answer(id, BrowserDialogAnswer.Confirm)
        assertNotNull(dialogs.pending.value)
        assertNotEquals(id, dialogs.pending.value?.id)
    }

    @Test fun `cancellation opening another dialog does not leak the intervening replacement`() {
        val interveningAnswers = mutableListOf<BrowserDialogAnswer>()
        var newestId = 0L
        dialogs.show(
            1,
            7,
            PendingJsDialog(
                BrowserDialogKind.ALERT,
                "https://example.com",
                "First",
                null,
            ) { newestId = show() },
        )
        val interveningId = dialogs.show(
            1,
            7,
            PendingJsDialog(
                BrowserDialogKind.CONFIRM,
                "https://example.com",
                "Intervening",
                null,
                interveningAnswers::add,
            ),
        )
        assertEquals(listOf<BrowserDialogAnswer>(BrowserDialogAnswer.Cancel), interveningAnswers)
        assertEquals(newestId, dialogs.pending.value?.id)
        dialogs.answer(interveningId, BrowserDialogAnswer.Confirm)
        assertEquals(newestId, dialogs.pending.value?.id)
        dialogs.cancel()
        assertNull(dialogs.pending.value)
        assertEquals(listOf<BrowserDialogAnswer>(BrowserDialogAnswer.Cancel), answers)
    }
}
