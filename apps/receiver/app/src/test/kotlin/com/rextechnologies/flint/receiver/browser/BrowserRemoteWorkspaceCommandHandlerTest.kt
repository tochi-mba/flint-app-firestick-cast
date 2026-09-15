package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceCommandAction
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceInputKind
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceInputMessage
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceSession
import kotlin.test.Test
import kotlin.test.assertEquals

class BrowserRemoteWorkspaceCommandHandlerTest {
    @Test
    fun `mode switch requires command acceptance and retains panes`() {
        val session = BrowserWorkspaceSession()
        session.openLocalWorkspace("family", "https://example.test/")
        val panes = session.state.panes
        var accepted = false
        val modes = mutableListOf<Boolean>()
        val handler = BrowserRemoteWorkspaceCommandHandler(
            session,
            {},
            { 7 },
            { _, _, _ -> accepted },
            { modes += it },
        )
        val command = com.rextechnologies.flint.protocol.wire.BrowserWorkspaceResizeMessage(4, 130, 7, 7000, 3000, 1)
        handler.handle(command)
        assertEquals(emptyList(), modes)
        accepted = true
        handler.handle(command)
        assertEquals(listOf(false), modes)
        assertEquals(panes, session.state.panes)
        assertEquals(7000, session.state.split.column.tenThousandths)
    }

    @Test
    fun `future workspace revision is rejected before acceptance or state mutation`() {
        val session = BrowserWorkspaceSession()
        session.openLocalWorkspace("family", null)
        val before = session.state
        var acceptanceCalls = 0
        var publications = 0
        val handler = BrowserRemoteWorkspaceCommandHandler(
            workspaceSession = session,
            publishWorkspace = { publications += 1 },
            currentRevision = { 7 },
            accept = { _, _, _ ->
                acceptanceCalls += 1
                true
            },
        )

        handler.handle(
            BrowserWorkspaceCommandMessage(
                epoch = 4,
                commandId = 130,
                expectedRevision = 8,
                action = BrowserWorkspaceCommandAction.OPEN_PANE,
            ),
        )

        assertEquals(before, session.state)
        assertEquals(0, acceptanceCalls)
        assertEquals(0, publications)
    }

    @Test
    fun `older workspace revision is accepted so progress races do not kill mosaic clicks`() {
        val session = BrowserWorkspaceSession()
        session.openLocalWorkspace("family", null)
        val beforeCount = session.state.panes.size
        var acceptanceCalls = 0
        val handler = BrowserRemoteWorkspaceCommandHandler(
            workspaceSession = session,
            publishWorkspace = {},
            currentRevision = { 7 },
            accept = { _, _, _ ->
                acceptanceCalls += 1
                true
            },
        )

        handler.handle(
            BrowserWorkspaceCommandMessage(
                epoch = 4,
                commandId = 130,
                expectedRevision = 6,
                action = BrowserWorkspaceCommandAction.OPEN_PANE,
            ),
        )

        assertEquals(1, acceptanceCalls)
        assertEquals(beforeCount + 1, session.state.panes.size)
    }

    @Test
    fun `move pane reorders slots without recreating panes`() {
        val session = BrowserWorkspaceSession()
        session.openLocalWorkspace("family", "https://example.test/a")
        session.dispatch(
            com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceAction.OpenPane(
                "https://example.test/b",
            ),
        )
        val first = session.state.panes.sortedBy { it.slot }.first()
        val second = session.state.panes.sortedBy { it.slot }.last()
        val handler = BrowserRemoteWorkspaceCommandHandler(session, {}, { 7 }, { _, _, _ -> true })
        handler.handle(
            BrowserWorkspaceCommandMessage(
                epoch = 4,
                commandId = 144,
                expectedRevision = 1,
                action = BrowserWorkspaceCommandAction.MOVE_PANE,
                paneId = first.id,
                value = 1,
            ),
        )
        assertEquals(listOf(second.id, first.id), session.state.panes.sortedBy { it.slot }.map { it.id })
    }

    @Test
    fun `future workspace input is rejected before acceptance or dispatch`() {
        val session = BrowserWorkspaceSession()
        session.openLocalWorkspace("family", null)
        val before = session.state
        var acceptanceCalls = 0
        val handler = BrowserRemoteWorkspaceCommandHandler(
            workspaceSession = session,
            publishWorkspace = {},
            currentRevision = { 7 },
            accept = { _, _, _ ->
                acceptanceCalls += 1
                true
            },
        )

        handler.handle(
            BrowserWorkspaceInputMessage(
                epoch = 4,
                commandId = 131,
                expectedRevision = 8,
                paneId = session.state.focusedPaneId,
                kind = BrowserWorkspaceInputKind.TEXT,
                text = "ignored",
            ),
        )

        assertEquals(before, session.state)
        assertEquals(0, acceptanceCalls)
    }
}
