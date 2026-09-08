package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertNull
import kotlin.test.assertTrue

class BrowserDialogReducerTest {
    private val reducer = BrowserDialogReducer()
    private val origin = (BrowserUrlPolicy().evaluate("https://example.com/checkout?token=private") as BrowserUrlResult.Accepted).url

    @Test
    fun `only one current dialog is active and first valid reply wins`() {
        val first = BrowserDialogRequest(9, 4, BrowserDialogKind.CONFIRM, origin, BrowserDialogText.fromPage("Proceed?"))
        val shown = reducer.reduce(BrowserDialogState(), BrowserDialogEvent.Show(first, nowMs = 100))
        assertIs<BrowserDialogEffect.ShowLocal>(shown.effect)
        assertEquals("https://example.com/checkout", shown.state.active?.origin?.displayUrl)

        val later = BrowserDialogRequest(10, 4, BrowserDialogKind.ALERT, origin, BrowserDialogText.fromPage("Later"))
        val rejected = reducer.reduce(shown.state, BrowserDialogEvent.Show(later, nowMs = 101))
        assertIs<BrowserDialogEffect.RejectPageDialog>(rejected.effect)
        assertEquals(shown.state, rejected.state)

        val accepted = reducer.reduce(
            shown.state,
            BrowserDialogEvent.Reply(4, 9, BrowserDialogReplySource.TV_LOCAL, BrowserDialogAnswer.Confirm),
        )
        assertTrue(assertIs<BrowserDialogEffect.ResolvePageDialog>(accepted.effect).accepted)
        assertNull(accepted.state.active)

        val duplicate = reducer.reduce(
            accepted.state,
            BrowserDialogEvent.Reply(4, 9, BrowserDialogReplySource.PINNED_SESSION, BrowserDialogAnswer.Cancel),
        )
        assertNull(duplicate.effect)
        assertEquals(accepted.state, duplicate.state)
    }

    @Test
    fun `stale epoch wrong ID bad prompt and expiry resolve safely without exposing input`() {
        val prompt = BrowserDialogRequest(9, 4, BrowserDialogKind.PROMPT, origin, BrowserDialogText.fromPage("Name"))
        val shown = reducer.reduce(BrowserDialogState(), BrowserDialogEvent.Show(prompt, nowMs = 100)).state
        val stale = reducer.reduce(shown, BrowserDialogEvent.Reply(3, 9, BrowserDialogReplySource.PINNED_SESSION, BrowserDialogAnswer.Cancel))
        val wrongId = reducer.reduce(shown, BrowserDialogEvent.Reply(4, 8, BrowserDialogReplySource.PINNED_SESSION, BrowserDialogAnswer.Cancel))
        assertNull(stale.effect)
        assertNull(wrongId.effect)

        val invalidPrompt = reducer.reduce(
            shown,
            BrowserDialogEvent.Reply(4, 9, BrowserDialogReplySource.PINNED_SESSION, BrowserDialogAnswer.Prompt("not\u0000safe")),
        )
        assertIs<BrowserDialogEffect.RejectReply>(invalidPrompt.effect)
        assertEquals(shown, invalidPrompt.state)

        val expired = reducer.reduce(shown, BrowserDialogEvent.Expire(nowMs = 100 + BrowserDialogReducer.DEFAULT_TIMEOUT_MS))
        val resolution = assertIs<BrowserDialogEffect.ResolvePageDialog>(expired.effect)
        assertTrue(!resolution.accepted)
        assertNull(resolution.text)
    }

    @Test
    fun `surface close cancels prompt and all dialog values have redacted representations`() {
        val prompt = BrowserDialogRequest(9, 4, BrowserDialogKind.PROMPT, origin, BrowserDialogText.fromPage("Super secret prompt"))
        val shown = reducer.reduce(BrowserDialogState(), BrowserDialogEvent.Show(prompt, nowMs = 100)).state

        val closed = reducer.reduce(shown, BrowserDialogEvent.Close)
        val resolution = assertIs<BrowserDialogEffect.ResolvePageDialog>(closed.effect)
        assertTrue(!resolution.accepted)
        assertTrue(!prompt.message.toString().contains("secret"))
        assertTrue(!resolution.toString().contains("secret"))
    }
}
