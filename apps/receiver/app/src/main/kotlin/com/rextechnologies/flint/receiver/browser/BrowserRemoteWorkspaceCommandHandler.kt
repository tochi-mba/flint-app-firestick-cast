package com.rextechnologies.flint.receiver.browser

import android.util.Log
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceCommandAction
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceInputKind
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceResizeMessage
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceInputMessage
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceWireInteractionMode
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceWireLayout
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceAction
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceInteractionMode
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceLayout
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceSession

/** Executes authenticated workspace commands after accepting them on the shared command stream. */
class BrowserRemoteWorkspaceCommandHandler(
    private val workspaceSession: BrowserWorkspaceSession,
    private val publishWorkspace: () -> Unit,
    private val currentRevision: () -> Long,
    private val accept: (Long, Long, String) -> Boolean,
    private val setWorkspaceMode: (Boolean) -> Unit = {},
) {
    fun handle(command: BrowserWorkspaceCommandMessage) {
        // Accept commands based on the host's last seen revision *or any older one*. Progress and
        // media publishes bump revision constantly; requiring an exact match left mosaic chips,
        // ADD PANE, focus and close looking dead under an active page (2026-09-08).
        if (command.expectedRevision <= 0L || command.expectedRevision > currentRevision()) {
            Log.w(
                TAG,
                "Workspace command rejected: action=${command.action} expectedRev=${command.expectedRevision} currentRev=${currentRevision()}",
            )
            return
        }
        if (!accept(command.epoch, command.commandId, "workspace")) return
        when (command.action) {
            BrowserWorkspaceCommandAction.FOCUS ->
                workspaceSession.dispatch(BrowserWorkspaceAction.FocusPane(command.paneId))
            BrowserWorkspaceCommandAction.OPEN_PANE ->
                workspaceSession.dispatch(
                    BrowserWorkspaceAction.OpenPane(command.url.takeIf { it.isNotBlank() }),
                )
            BrowserWorkspaceCommandAction.CLOSE_PANE ->
                workspaceSession.dispatch(BrowserWorkspaceAction.ClosePane(command.paneId))
            BrowserWorkspaceCommandAction.MOVE_PANE ->
                workspaceSession.dispatch(BrowserWorkspaceAction.MovePane(command.paneId, command.value.toInt()))
            BrowserWorkspaceCommandAction.SET_LAYOUT -> {
                val layout = wireLayout(command.value) ?: return
                workspaceSession.dispatch(BrowserWorkspaceAction.SetLayout(layout))
            }
            BrowserWorkspaceCommandAction.NAVIGATE ->
                workspaceSession.dispatch(
                    BrowserWorkspaceAction.NavigatePane(command.paneId, command.url),
                )
            BrowserWorkspaceCommandAction.RELOAD ->
                workspaceSession.dispatch(BrowserWorkspaceAction.ReloadPane(command.paneId))
            BrowserWorkspaceCommandAction.BACK ->
                workspaceSession.dispatch(BrowserWorkspaceAction.GoBack(command.paneId))
            BrowserWorkspaceCommandAction.FORWARD ->
                workspaceSession.dispatch(BrowserWorkspaceAction.GoForward(command.paneId))
            BrowserWorkspaceCommandAction.SET_MUTE ->
                workspaceSession.dispatch(
                    BrowserWorkspaceAction.SetPaneMuted(command.paneId, command.value == 1),
                )
            BrowserWorkspaceCommandAction.PLAY_PAUSE ->
                workspaceSession.dispatch(BrowserWorkspaceAction.RequestMediaPlayPause(command.paneId))
            BrowserWorkspaceCommandAction.SET_INTERACTION -> {
                val mode = wireInteractionMode(command.value) ?: return
                workspaceSession.dispatch(BrowserWorkspaceAction.SetInteractionMode(mode))
            }
            BrowserWorkspaceCommandAction.ENTER_THEATER ->
                workspaceSession.dispatch(BrowserWorkspaceAction.EnterTheaterMode(command.paneId))
            BrowserWorkspaceCommandAction.EXIT_THEATER ->
                workspaceSession.dispatch(BrowserWorkspaceAction.ExitTheaterMode)
            BrowserWorkspaceCommandAction.REQUEST_SNAPSHOT -> publishWorkspace()
        }
    }

    fun handle(input: BrowserWorkspaceResizeMessage) {
        if (input.expectedRevision <= 0 || input.expectedRevision > currentRevision()) {
            publishWorkspace()
            return
        }
        if (!accept(input.epoch, input.commandId, "workspace-resize")) return
        if (input.mode != 0) setWorkspaceMode(input.mode == 2)
        workspaceSession.dispatch(BrowserWorkspaceAction.SetSplit(
            com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceSplit.of(input.column, input.row),
        ))
        // Even a refused/no-op resize must acknowledge authoritative geometry.
        publishWorkspace()
    }

    fun handle(input: BrowserWorkspaceInputMessage) {
        if (input.expectedRevision <= 0L || input.expectedRevision > currentRevision()) {
            Log.w(
                TAG,
                "Workspace input rejected: kind=${input.kind} expectedRev=${input.expectedRevision} currentRev=${currentRevision()}",
            )
            return
        }
        if (!accept(input.epoch, input.commandId, "workspace-input")) return
        when (input.kind) {
            BrowserWorkspaceInputKind.TEXT ->
                workspaceSession.dispatchRemoteText(input.paneId, input.text)
            BrowserWorkspaceInputKind.KEY -> {
                val key = input.key ?: return
                workspaceSession.dispatchRemoteKey(input.paneId, key)
            }
        }
    }

    private fun wireLayout(value: Int): BrowserWorkspaceLayout? = when (
        BrowserWorkspaceWireLayout.fromId(value)
    ) {
        BrowserWorkspaceWireLayout.SINGLE -> BrowserWorkspaceLayout.SINGLE
        BrowserWorkspaceWireLayout.TWO_COLUMNS -> BrowserWorkspaceLayout.SPLIT_HORIZONTAL
        BrowserWorkspaceWireLayout.TWO_ROWS -> BrowserWorkspaceLayout.SPLIT_VERTICAL
        BrowserWorkspaceWireLayout.FOUR_GRID -> BrowserWorkspaceLayout.GRID_2X2
        null -> {
            Log.w(TAG, "Browser workspace layout rejected at wire value $value")
            null
        }
    }

    private fun wireInteractionMode(value: Int): BrowserWorkspaceInteractionMode? = when (
        BrowserWorkspaceWireInteractionMode.fromId(value)
    ) {
        BrowserWorkspaceWireInteractionMode.WORKSPACE_CHROME ->
            BrowserWorkspaceInteractionMode.WORKSPACE_CHROME
        BrowserWorkspaceWireInteractionMode.PAGE -> BrowserWorkspaceInteractionMode.PAGE
        null -> {
            Log.w(TAG, "Browser workspace interaction mode rejected at wire value $value")
            null
        }
    }

    private companion object {
        const val TAG = "FlintBrowser"
    }
}
