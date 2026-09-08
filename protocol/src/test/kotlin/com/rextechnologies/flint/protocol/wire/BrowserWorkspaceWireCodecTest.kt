package com.rextechnologies.flint.protocol.wire

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class BrowserWorkspaceWireCodecTest {
    @Test
    fun allWorkspaceCommandsLayoutsAndInputsRoundTrip() {
        val commands = listOf(
            BrowserWorkspaceCommandMessage(4, 130, 140, BrowserWorkspaceCommandAction.FOCUS, paneId = 42),
            BrowserWorkspaceCommandMessage(
                4,
                128,
                140,
                BrowserWorkspaceCommandAction.OPEN_PANE,
                url = "https://example.test/new",
            ),
            BrowserWorkspaceCommandMessage(4, 131, 140, BrowserWorkspaceCommandAction.CLOSE_PANE, paneId = 42),
            BrowserWorkspaceCommandMessage(
                4,
                132,
                140,
                BrowserWorkspaceCommandAction.SET_LAYOUT,
                value = BrowserWorkspaceWireLayout.TWO_COLUMNS.id,
            ),
            BrowserWorkspaceCommandMessage(
                4,
                133,
                140,
                BrowserWorkspaceCommandAction.NAVIGATE,
                paneId = 42,
                url = "https://example.test/next",
            ),
            BrowserWorkspaceCommandMessage(4, 134, 140, BrowserWorkspaceCommandAction.RELOAD, paneId = 42),
            BrowserWorkspaceCommandMessage(4, 135, 140, BrowserWorkspaceCommandAction.BACK, paneId = 42),
            BrowserWorkspaceCommandMessage(4, 136, 140, BrowserWorkspaceCommandAction.FORWARD, paneId = 42),
            BrowserWorkspaceCommandMessage(4, 137, 140, BrowserWorkspaceCommandAction.SET_MUTE, paneId = 42, value = 1),
            BrowserWorkspaceCommandMessage(4, 138, 140, BrowserWorkspaceCommandAction.PLAY_PAUSE, paneId = 42),
            BrowserWorkspaceCommandMessage(
                4,
                139,
                140,
                BrowserWorkspaceCommandAction.SET_INTERACTION,
                value = BrowserWorkspaceWireInteractionMode.PAGE.id,
            ),
            BrowserWorkspaceCommandMessage(4, 140, 140, BrowserWorkspaceCommandAction.ENTER_THEATER, paneId = 42),
            BrowserWorkspaceCommandMessage(4, 141, 140, BrowserWorkspaceCommandAction.EXIT_THEATER),
            BrowserWorkspaceCommandMessage(4, 142, 140, BrowserWorkspaceCommandAction.REQUEST_SNAPSHOT),
            BrowserWorkspaceCommandMessage(4, 143, 140, BrowserWorkspaceCommandAction.MOVE_PANE, paneId = 42, value = 1),
        )
        val states = listOf(
            singlePaneState(),
            BrowserWorkspaceStateMessage(
                4,
                141,
                BrowserWorkspaceWireLayout.SINGLE,
                0,
                BrowserWorkspaceWireInteractionMode.WORKSPACE_CHROME,
                0,
                0,
                1,
                1,
                emptyList(),
            ),
        ) + BrowserWorkspaceWireLayout.entries.map { layout ->
            singlePaneState().copy(layout = layout)
        }
        val inputs = listOf(
            BrowserWorkspaceInputMessage(4, 131, 140, 42, BrowserWorkspaceInputKind.TEXT, text = "hello"),
            BrowserWorkspaceInputMessage(
                4,
                132,
                140,
                42,
                BrowserWorkspaceInputKind.KEY,
                key = BrowserSemanticKey.SELECT,
            ),
        )

        (commands + states + inputs).forEach { message ->
            val frame = WireFrame(3, message)
            assertEquals(frame, WireCodec.decode(WireCodec.encode(frame)))
        }
    }

    @Test
    fun workspaceMessagesAreAdditiveTlsOnlyV3BrowserTypes() {
        assertEquals(32, WireMessageType.BROWSER_WORKSPACE_COMMAND.id)
        assertEquals(33, WireMessageType.BROWSER_WORKSPACE_STATE.id)
        assertEquals(34, WireMessageType.BROWSER_WORKSPACE_INPUT.id)

        val messages = listOf<WireMessage>(
            BrowserWorkspaceCommandMessage(4, 1, 140, BrowserWorkspaceCommandAction.REQUEST_SNAPSHOT),
            BrowserWorkspaceStateMessage(
                4,
                1,
                BrowserWorkspaceWireLayout.SINGLE,
                0,
                BrowserWorkspaceWireInteractionMode.WORKSPACE_CHROME,
                0,
                0,
                1,
                1,
                emptyList(),
            ),
            BrowserWorkspaceInputMessage(4, 1, 140, 42, BrowserWorkspaceInputKind.TEXT, text = "hello"),
        )
        messages.forEach { message ->
            assertTrue(BrowserWireRules.isBrowserMessage(message))
            assertTrue(BrowserWireRules.isWorkspaceMessage(message))
            assertTrue(BrowserWireRules.isBrowserType(message.typeId))
            assertTrue(BrowserWireRules.isForbiddenOnOrdinaryChannel(message))
            assertFailsWith<WireFormatException> { WireCodec.encode(WireFrame(2, message)) }
        }
        messages.forEach { message ->
            val frame = WireFrame(3, message)
            assertEquals(frame, WireCodec.decode(WireCodec.encode(frame)))
        }
        assertTrue(BrowserWireRules.isBrowserType(32))
        assertTrue(BrowserWireRules.isBrowserType(33))
        assertTrue(BrowserWireRules.isBrowserType(34))
        assertFalse(BrowserWireRules.isBrowserType(37))
    }

    @Test
    fun invalidWorkspaceCommandsStatesAndInputsAreRejected() {
        val validCommand = BrowserWorkspaceCommandMessage(4, 130, 140, BrowserWorkspaceCommandAction.FOCUS, paneId = 42)
        val invalidCommands = listOf(
            validCommand.copy(epoch = 0),
            validCommand.copy(commandId = 0),
            validCommand.copy(expectedRevision = 0),
            validCommand.copy(paneId = 0),
            validCommand.copy(action = BrowserWorkspaceCommandAction.REQUEST_SNAPSHOT),
            validCommand.copy(url = "https://example.test"),
            BrowserWorkspaceCommandMessage(
                4,
                128,
                140,
                BrowserWorkspaceCommandAction.OPEN_PANE,
                paneId = 42,
                url = "https://example.test/new",
            ),
        )
        invalidCommands.forEach { message ->
            assertFailsWith<WireFormatException>(message.toString()) { WireCodec.encode(WireFrame(3, message)) }
        }

        val validState = singlePaneState()
        val invalidStates = listOf(
            validState.copy(epoch = 0),
            validState.copy(revision = 0),
            validState.copy(maxLiveRenderers = 0),
            validState.copy(maxOpenPanes = 0),
            validState.copy(maxLiveRenderers = 3, maxOpenPanes = 2),
            validState.copy(
                panes = listOf(
                    BrowserWorkspacePaneStateEntry(
                        42,
                        0,
                        BrowserWorkspaceWirePaneResidency.LIVE,
                        "",
                        "",
                        false,
                        100,
                        false,
                        false,
                        false,
                        BrowserWorkspaceWireMuteApplication.NOT_REQUESTED,
                        BrowserWorkspaceWireObservedPlayback.UNKNOWN,
                    ),
                ),
            ),
            validState.copy(focusedPaneId = 99),
        )
        invalidStates.forEach { message ->
            assertFailsWith<WireFormatException>(message.toString()) { WireCodec.encode(WireFrame(3, message)) }
        }

        val validInput = BrowserWorkspaceInputMessage(4, 131, 140, 42, BrowserWorkspaceInputKind.TEXT, text = "hello")
        val invalidInputs = listOf(
            validInput.copy(epoch = 0),
            validInput.copy(commandId = 0),
            validInput.copy(expectedRevision = 0),
            validInput.copy(paneId = 0),
            validInput.copy(text = ""),
            BrowserWorkspaceInputMessage(4, 131, 140, 42, BrowserWorkspaceInputKind.KEY, text = "hello"),
        )
        invalidInputs.forEach { message ->
            assertFailsWith<WireFormatException>(message.toString()) { WireCodec.encode(WireFrame(3, message)) }
        }
    }

    @Test
    fun workspaceDecodersRejectUnknownEnumsAndTrailingBytes() {
        fun payload(message: WireMessage): ByteArray {
            val encoded = WireCodec.encode(WireFrame(3, message))
            return encoded.copyOfRange(12, encoded.size)
        }

        val command = payload(
            BrowserWorkspaceCommandMessage(4, 1, 140, BrowserWorkspaceCommandAction.REQUEST_SNAPSHOT),
        ).also { it[24] = 0 }
        assertFailsWith<WireFormatException> {
            WireMessageCodec.decode(3, WireMessageType.BROWSER_WORKSPACE_COMMAND.id, command)
        }

        val state = payload(singlePaneState()).also { it[16] = 0 }
        assertFailsWith<WireFormatException> {
            WireMessageCodec.decode(3, WireMessageType.BROWSER_WORKSPACE_STATE.id, state)
        }

        val valid = payload(singlePaneState())
        assertFailsWith<WireFormatException> {
            WireMessageCodec.decode(3, WireMessageType.BROWSER_WORKSPACE_STATE.id, valid + 0)
        }
    }

    private fun singlePaneState() = BrowserWorkspaceStateMessage(
        4,
        140,
        BrowserWorkspaceWireLayout.SINGLE,
        42,
        BrowserWorkspaceWireInteractionMode.WORKSPACE_CHROME,
        0,
        0,
        2,
        4,
        listOf(
            BrowserWorkspacePaneStateEntry(
                42,
                0,
                BrowserWorkspaceWirePaneResidency.LIVE,
                "https://example.test/new",
                "",
                false,
                100,
                false,
                false,
                false,
                BrowserWorkspaceWireMuteApplication.NOT_REQUESTED,
                BrowserWorkspaceWireObservedPlayback.UNKNOWN,
            ),
        ),
    )
}
