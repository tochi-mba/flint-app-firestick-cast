package com.rextechnologies.flint.receiver.browser.workspace

import com.rextechnologies.flint.protocol.wire.BrowserPointerAction
import com.rextechnologies.flint.receiver.browser.*
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import kotlin.test.*
import kotlin.test.assertSame

@RunWith(RobolectricTestRunner::class)
@Config(manifest = Config.NONE, sdk = [28])
class BrowserWorkspaceSessionTest {
    private class Host(
        private val captureView: android.view.View? = null,
        private val hits: Map<Pair<Int, Int>, Pair<Long, Pair<Int, Int>>> = emptyMap(),
    ) : BrowserWorkspaceHostPort {
        val tokens = mutableMapOf<Long, Long>()
        val inputs = mutableListOf<Pair<Long, BrowserNativeInput>>()
        var destroyed = 0
        override fun create(id: Long, generation: Long, url: String?) {
            tokens[id] = generation
        }
        override fun restore(id: Long, generation: Long): Boolean {
            tokens[id] = generation
            return false
        }
        override fun driverFor(id: Long): BrowserWebViewDriver? = null
        override fun dispatchNativeInput(id: Long, input: BrowserNativeInput) {
            inputs.add(id to input)
        }
        override fun freeze(id: Long) {
            tokens.remove(id)
        }
        override fun destroy(id: Long) {
            tokens.remove(id)
        }
        override fun destroyAll() {
            destroyed++
            tokens.clear()
        }
        override fun exitFullscreen(id: Long) = true
        override fun setPageFocusEnabled(enabled: Boolean) = Unit
        override fun render(state: BrowserWorkspaceState) = Unit
        override fun previewCaptureView(): android.view.View? = captureView
        override fun hitTestMosaic(x: Int, y: Int): Pair<Long, Pair<Int, Int>>? = hits[x to y]
    }

    private fun open(session: BrowserWorkspaceSession, host: Host): Long {
        session.openLocalWorkspace("family", null)
        session.attachHost(host)
        return assertNotNull(host.tokens[session.state.focusedPaneId])
    }

    @Test fun `resize cancels held gesture and discards its later release`() {
        val host = Host()
        val session = BrowserWorkspaceSession()
        open(session, host)
        session.dispatch(BrowserWorkspaceAction.OpenPane(null))
        val pane = session.state.focusedPaneId
        session.dispatch(BrowserWorkspaceAction.SetInteractionMode(BrowserWorkspaceInteractionMode.PAGE))
        session.dispatchNativeInput(pane, BrowserNativeInput.Pointer(BrowserPointerAction.DOWN, 10, 20, 1))
        session.dispatch(BrowserWorkspaceAction.SetSplit(BrowserWorkspaceSplit.of(7000, 3000)))
        session.dispatchNativeInput(pane, BrowserNativeInput.Pointer(BrowserPointerAction.UP, 10, 20, 0))
        assertEquals(
            listOf(BrowserPointerAction.DOWN, BrowserPointerAction.CANCEL),
            host.inputs.map { (it.second as BrowserNativeInput.Pointer).action },
        )
        assertEquals(listOf(pane, pane), host.inputs.map { it.first })
    }

    @Test fun `attaching activity rebuilds pages opened without a host`() {
        val session = BrowserWorkspaceSession()
        val host = Host()
        open(session, host)
        assertEquals(1, host.tokens.size)
        assertEquals(session.state, session.stateFlow.value)
    }

    @Test fun `focus change cancels held gesture on old pane before accepting new input`() {
        val host = Host()
        val session = BrowserWorkspaceSession()
        open(session, host)
        val first = session.state.focusedPaneId
        session.dispatch(BrowserWorkspaceAction.OpenPane(null))
        val second = session.state.focusedPaneId
        session.dispatch(BrowserWorkspaceAction.FocusPane(first))
        session.dispatch(BrowserWorkspaceAction.SetInteractionMode(BrowserWorkspaceInteractionMode.PAGE))
        session.dispatchNativeInput(first, BrowserNativeInput.Pointer(BrowserPointerAction.DOWN, 10, 20, 1))
        session.dispatch(BrowserWorkspaceAction.FocusPane(second))
        session.dispatch(BrowserWorkspaceAction.SetInteractionMode(BrowserWorkspaceInteractionMode.PAGE))
        session.dispatchNativeInput(second, BrowserNativeInput.Pointer(BrowserPointerAction.UP, 10, 20, 0))
        assertEquals(listOf(first, first), host.inputs.map { it.first })
        assertEquals(
            listOf(BrowserPointerAction.DOWN, BrowserPointerAction.CANCEL),
            host.inputs.map { (it.second as BrowserNativeInput.Pointer).action },
        )
    }

    @Test fun `preview click on another pane selects it and delivers the local click`() {
        val session = BrowserWorkspaceSession()
        session.openLocalWorkspace("family", "https://example.com/a")
        session.dispatch(BrowserWorkspaceAction.OpenPane("https://example.com/b"))
        val first = session.state.paneIdsInSlotOrder[0]
        val second = session.state.paneIdsInSlotOrder[1]
        session.dispatch(BrowserWorkspaceAction.FocusPane(first))
        session.dispatch(BrowserWorkspaceAction.SetInteractionMode(BrowserWorkspaceInteractionMode.WORKSPACE_CHROME))
        val host = Host(
            hits = mapOf(
                150 to 40 to (second to (12 to 8)),
            ),
        )
        session.attachHost(host)

        val delivered = session.dispatchPreviewPointer(
            mosaicX = 150,
            mosaicY = 40,
            pointer = BrowserNativeInput.Pointer(BrowserPointerAction.DOWN, 150, 40, 1),
        )

        assertTrue(delivered)
        assertEquals(second, session.state.focusedPaneId)
        assertEquals(BrowserWorkspaceInteractionMode.PAGE, session.state.interactionMode)
        assertEquals(second, host.inputs.single().first)
        val pointer = host.inputs.single().second as BrowserNativeInput.Pointer
        assertEquals(BrowserPointerAction.DOWN, pointer.action)
        assertEquals(12, pointer.x)
        assertEquals(8, pointer.y)
    }

    @Test fun `detaching activity cancels touch before destroying its host`() {
        val host = Host()
        val session = BrowserWorkspaceSession()
        open(session, host)
        session.dispatch(BrowserWorkspaceAction.SetInteractionMode(BrowserWorkspaceInteractionMode.PAGE))
        session.dispatchNativeInput(
            session.state.focusedPaneId,
            BrowserNativeInput.Pointer(BrowserPointerAction.DOWN, 10, 20, 1),
        )
        session.detachHost(host)
        assertEquals(BrowserPointerAction.CANCEL, (host.inputs.last().second as BrowserNativeInput.Pointer).action)
        assertEquals(1, host.destroyed)
    }

    @Test fun `old activity disposal cannot detach its replacement`() {
        val session = BrowserWorkspaceSession()
        val old = Host()
        open(session, old)
        val replacement = Host()
        session.attachHost(replacement)
        session.detachHost(old)
        assertEquals(0, replacement.destroyed)
        assertEquals(1, replacement.tokens.size)
    }

    @Test fun `callback from destroyed activity cannot update replacement page`() {
        val session = BrowserWorkspaceSession()
        val oldToken = open(session, Host())
        val paneId = session.state.focusedPaneId
        val replacement = Host()
        session.attachHost(replacement)
        session.onPaneStateEvent(paneId, oldToken, BrowserStateEvent.Title(1, 0, "Stale"))
        assertEquals("", session.state.focusedPane?.page?.title)
        session.onPaneStateEvent(paneId, replacement.tokens.getValue(paneId), BrowserStateEvent.Title(1, 0, "Current"))
        assertEquals("Current", session.state.focusedPane?.page?.title)
    }

    @Test fun `old navigation callback does not inherit current navigation id`() {
        val session = BrowserWorkspaceSession()
        val token = open(session, Host())
        val id = session.state.focusedPaneId
        session.dispatch(BrowserWorkspaceAction.NavigatePane(id, "https://example.com"))
        session.onPaneStateEvent(id, token, BrowserStateEvent.Title(1, 0, "Old title"))
        assertEquals("", session.state.focusedPane?.page?.title)
    }

    @Test fun `missing renderer reports media requests honestly`() {
        val session = BrowserWorkspaceSession()
        open(session, Host())
        val paneId = session.state.focusedPaneId

        session.dispatch(BrowserWorkspaceAction.RequestMediaPlayPause(paneId))
        assertEquals(
            BrowserWorkspacePlaybackRequestStatus.REJECTED,
            session.state.focusedPane?.media?.playbackRequest?.status,
        )

        session.dispatch(BrowserWorkspaceAction.SetPaneMuted(paneId, true))
        assertEquals(
            BrowserWorkspaceMuteApplication.FAILED,
            session.state.focusedPane?.media?.muteApplication,
        )
        assertNull(session.state.focusedPane?.media?.pendingMuteRequestId)
    }

    @Test fun `closing pane resolves native dialog exactly once`() {
        val session = BrowserWorkspaceSession()
        val token = open(session, Host())
        val answers = mutableListOf<BrowserDialogAnswer>()
        session.onDialog(
            session.state.focusedPaneId,
            token,
            PendingJsDialog(BrowserDialogKind.CONFIRM, "https://example.com", "Continue?", null, answers::add),
        )
        val request = assertNotNull(session.pendingDialog.value)
        session.closeWorkspace()
        session.answerDialog(request.id, BrowserDialogAnswer.Confirm)
        assertEquals(listOf<BrowserDialogAnswer>(BrowserDialogAnswer.Cancel), answers)
    }

    @Test fun `stale renderer cannot show a native dialog`() {
        val session = BrowserWorkspaceSession()
        val oldToken = open(session, Host())
        session.attachHost(Host())
        val answers = mutableListOf<BrowserDialogAnswer>()
        session.onDialog(
            session.state.focusedPaneId,
            oldToken,
            PendingJsDialog(BrowserDialogKind.ALERT, "https://example.com", "Old", null, answers::add),
        )
        assertNull(session.pendingDialog.value)
        assertEquals(listOf<BrowserDialogAnswer>(BrowserDialogAnswer.Cancel), answers)
    }

    @Test fun `previewCaptureView exposes the mosaic root so Windows sees every live pane`() {
        val mosaic = android.view.View(org.robolectric.RuntimeEnvironment.getApplication())
        val session = BrowserWorkspaceSession()
        open(session, Host(captureView = mosaic))
        session.dispatch(BrowserWorkspaceAction.OpenPane(null))
        assertEquals(2, session.state.panes.size)
        assertSame(mosaic, session.previewCaptureView())
    }
}
