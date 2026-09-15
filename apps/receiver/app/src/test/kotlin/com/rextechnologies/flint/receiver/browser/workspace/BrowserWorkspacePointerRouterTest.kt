package com.rextechnologies.flint.receiver.browser.workspace

import com.rextechnologies.flint.protocol.wire.BrowserPointerAction
import com.rextechnologies.flint.receiver.browser.BrowserNativeInput
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

class BrowserWorkspacePointerRouterTest {
    private val sent = mutableListOf<Pair<Long, BrowserNativeInput>>()
    private val router = BrowserWorkspacePointerRouter { id, input -> sent.add(id to input) }
    private fun pointer(action: BrowserPointerAction, buttons: Int = 0, x: Int = 10) =
        BrowserNativeInput.Pointer(action, x, 20, buttons)

    @Test fun `held select is cancelled on original pane and cannot release on another`() {
        router.dispatch(1, pointer(BrowserPointerAction.DOWN, 1))
        router.cancel()
        router.dispatch(2, pointer(BrowserPointerAction.UP))
        assertEquals(listOf(1L, 1L), sent.map { it.first })
        assertEquals(BrowserPointerAction.CANCEL, (sent.last().second as BrowserNativeInput.Pointer).action)
    }

    @Test fun `new down cancels old gesture before starting a different pane`() {
        router.dispatch(1, pointer(BrowserPointerAction.DOWN, 1))
        router.dispatch(2, pointer(BrowserPointerAction.DOWN, 1))
        router.dispatch(1, pointer(BrowserPointerAction.UP))
        router.dispatch(2, pointer(BrowserPointerAction.UP))
        assertEquals(listOf(1L, 1L, 2L, 2L), sent.map { it.first })
        assertEquals(
            listOf(
                BrowserPointerAction.DOWN,
                BrowserPointerAction.CANCEL,
                BrowserPointerAction.DOWN,
                BrowserPointerAction.UP,
            ),
            sent.map {
                (it.second as BrowserNativeInput.Pointer).action
            },
        )
    }

    @Test fun `cancel is idempotent and uses last gesture coordinates`() {
        router.dispatch(1, pointer(BrowserPointerAction.DOWN, 1))
        router.dispatch(1, pointer(BrowserPointerAction.MOVE, 1, x = 44))
        router.cancel()
        router.cancel()
        assertEquals(3, sent.size)
        assertEquals(pointer(BrowserPointerAction.CANCEL, x = 44), sent.last().second)
    }

    @Test fun `orphan releases and drags are discarded but hover remains available`() {
        router.dispatch(1, pointer(BrowserPointerAction.UP))
        router.dispatch(1, pointer(BrowserPointerAction.CANCEL))
        router.dispatch(1, pointer(BrowserPointerAction.MOVE, 1))
        assertTrue(sent.isEmpty())
        router.dispatch(1, pointer(BrowserPointerAction.MOVE))
        assertEquals(1, sent.size)
    }

    @Test fun `release terminates gesture so teardown cannot cancel it twice`() {
        router.dispatch(1, pointer(BrowserPointerAction.DOWN, 1))
        router.dispatch(1, pointer(BrowserPointerAction.UP))
        router.cancel()
        assertEquals(2, sent.size)
    }
}
