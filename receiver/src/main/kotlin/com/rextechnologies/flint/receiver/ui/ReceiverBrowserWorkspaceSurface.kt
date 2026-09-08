package com.rextechnologies.flint.receiver.ui

import com.rextechnologies.flint.receiver.showBrowserRefusal
import com.rextechnologies.flint.receiver.allowBrowserUnderVpnPolicy
import com.rextechnologies.flint.receiver.ensureBrowserVpn
import com.rextechnologies.flint.receiver.showBrowserNotice
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.withFrameMillis
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.input.key.type
import androidx.compose.ui.input.key.KeyEventType
import android.view.KeyEvent
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceChromePolicy
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.ReceiverService
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceAction
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceInteractionMode
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceLayout
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceMuteApplication
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspacePlaybackObservation
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceState

/**
 * D-pad chrome around a real multi-WebView workspace.
 *
 * Pane rectangles are native ([ReceiverBrowserWorkspaceHost]); this Compose layer only owns the
 * overscan-safe controls and never fabricates page content.
 */
@Composable
internal fun ReceiverBrowserWorkspaceSurface(
    service: ReceiverService,
    controller: BrowserSurfaceController,
    onClose: () -> Unit,
) {
    val session = remember(service) { service.browserWorkspaceSession() }
    val snapshot by session.stateFlow.collectAsState()
    val dialog by session.pendingDialog.collectAsState()
    var resizeOpen by remember { mutableStateOf(false) }
    val addFocus = remember { FocusRequester() }
    val bindingRef = remember { arrayOfNulls<ReceiverBrowserWorkspaceBinding>(1) }
    val controls = remember(session) {
        BrowserWorkspaceTvControls(
            viewport = { session.viewportFor(session.state.focusedPaneId) },
            send = { session.dispatchNativeInput(session.state.focusedPaneId, it) },
            navigate = { session.dispatch(BrowserWorkspaceAction.NavigatePane(session.state.focusedPaneId, it)) },
        )
    }

    LaunchedEffect(snapshot.focusedPaneId, snapshot.interactionMode) {
        controls.resetPointer()
        if (snapshot.interactionMode == BrowserWorkspaceInteractionMode.WORKSPACE_CHROME) {
            runCatching { addFocus.requestFocus() }
        }
    }
    LaunchedEffect(controls, snapshot.interactionMode) {
        if (snapshot.interactionMode == BrowserWorkspaceInteractionMode.PAGE) {
            var previous = withFrameMillis { it }
            while (true) {
                val now = withFrameMillis { it }
                controls.frame(now - previous)
                previous = now
            }
        }
    }

    LaunchedEffect(Unit) {
        runCatching { addFocus.requestFocus() }
        if (!service.allowBrowserUnderVpnPolicy()) {
            service.ensureBrowserVpn()
            service.showBrowserNotice("VPN required — connect before opening workspace pages.")
            onClose()
            return@LaunchedEffect
        }
        service.ensureBrowserVpn()
    }

    DisposableEffect(controller, session) {
        controller.workspaceBackHandler = { if (resizeOpen) { resizeOpen = false; true } else session.handleBack() }
        onDispose {
            if (controller.workspaceBackHandler != null) {
                controller.workspaceBackHandler = null
            }
        }
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(ReceiverColors.Scrim)
            .onPreviewKeyEvent { event ->
                val down = event.type == KeyEventType.KeyDown
                val key = event.nativeKeyEvent.keyCode
                when {
                    resizeOpen && key == KeyEvent.KEYCODE_BACK -> {
                        if (down) { resizeOpen = false; session.saveLocalState() }
                        true
                    }
                    resizeOpen -> false
                    controls.addressOpen -> controls.onKey(key, down, event.nativeKeyEvent.repeatCount)
                    key == KeyEvent.KEYCODE_BACK -> {
                        if (down && event.nativeKeyEvent.repeatCount == 0) {
                            controls.cancelGesture()
                            if (!session.handleBack()) onClose()
                        }
                        true
                    }
                    snapshot.interactionMode == BrowserWorkspaceInteractionMode.PAGE ->
                        controls.onKey(key, down, event.nativeKeyEvent.repeatCount)
                    else -> false
                }
            }
            .testTag(ReceiverTags.BROWSER_WORKSPACE)
            .semantics { contentDescription = "Browser workspace" },
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize(ReceiverOverscan.CONTENT_FRACTION)
                .align(Alignment.Center),
            verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Small),
        ) {
            // Chrome is not composed at all while someone is driving a page: it was costing about
            // forty per cent of the screen, leaving a single page in a strip three times wider than
            // it was tall. Leaving workspace controls brings it straight back.
            val chromeVisible = BrowserWorkspaceChromePolicy.showsChrome(snapshot.interactionMode)
            if (chromeVisible) {
                WorkspaceChrome(
                state = snapshot,
                addFocus = addFocus,
                onAddPane = {
                    session.dispatch(BrowserWorkspaceAction.OpenPane(null))
                },
                allowedLayouts = session.capacity.allowedLayouts,
                onResize = { resizeOpen = true },
                onLayout = { layout ->
                    session.dispatch(BrowserWorkspaceAction.SetLayout(layout))
                },
                onFocusPane = { id ->
                    session.dispatch(BrowserWorkspaceAction.FocusPane(id))
                },
                onEnterPage = {
                    session.dispatch(
                        BrowserWorkspaceAction.SetInteractionMode(BrowserWorkspaceInteractionMode.PAGE),
                    )
                },
                onToggleMute = { id ->
                    val muted = snapshot.pane(id)?.media?.desiredMuted == true
                    session.dispatch(BrowserWorkspaceAction.SetPaneMuted(id, !muted))
                },
                onPlayPause = { id ->
                    session.dispatch(BrowserWorkspaceAction.RequestMediaPlayPause(id))
                },
                onToggleTheater = { id ->
                    session.dispatch(
                        if (snapshot.theaterPaneId == id) {
                            BrowserWorkspaceAction.ExitTheaterMode
                        } else {
                            BrowserWorkspaceAction.EnterTheaterMode(id)
                        },
                    )
                },
                onClosePane = { id ->
                    session.dispatch(BrowserWorkspaceAction.ClosePane(id))
                    if (session.state.panes.isEmpty()) onClose()
                },
                onMovePane = { id, slot ->
                    session.dispatch(BrowserWorkspaceAction.MovePane(id, slot))
                },
                onCloseWorkspace = onClose,
                )

                Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small)) {
                    WorkspaceChip("Address / search", { controls.openAddress(snapshot.focusedPane?.page?.url.orEmpty()) })
                    WorkspaceChip("Back", { session.dispatch(BrowserWorkspaceAction.GoBack(snapshot.focusedPaneId)) })
                    WorkspaceChip("Forward", { session.dispatch(BrowserWorkspaceAction.GoForward(snapshot.focusedPaneId)) })
                    WorkspaceChip("Reload", { session.dispatch(BrowserWorkspaceAction.ReloadPane(snapshot.focusedPaneId)) })
                }
            }

            AndroidView(
                factory = { context ->
                    val binding = ReceiverBrowserWorkspaceBinding(
                        context = context,
                        session = session,
                        onNotice = service::showBrowserNotice,
                        onRefusal = service::showBrowserRefusal,
                    )
                    bindingRef[0] = binding
                    binding.host
                },
                modifier = Modifier
                    .weight(1f)
                    .fillMaxWidth()
                    .border(ReceiverSpace.Hairline, ReceiverColors.Line, ReceiverShapes.Medium),
                update = { host ->
                    host.setPointer(controls.cursor.x, controls.cursor.y,
                        snapshot.interactionMode == BrowserWorkspaceInteractionMode.PAGE && !controls.addressOpen)
                },
                onRelease = {
                    bindingRef[0]?.destroy()
                    bindingRef[0] = null
                },
            )
        }
        if (resizeOpen) {
            val resizeFocus = remember { FocusRequester() }
            LaunchedEffect(Unit) { withFrameMillis { }; resizeFocus.requestFocus() }
            Column(Modifier.align(Alignment.Center).background(ReceiverColors.Panel).padding(24.dp)) {
                Text("Pane sizes", color = ReceiverColors.Text)
                Text("Columns ${snapshot.split.column.tenThousandths / 100}% / Rows ${snapshot.split.row.tenThousandths / 100}%",
                    color = ReceiverColors.Muted)
                Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small)) {
                    WorkspaceChip("Less left", {
                        session.dispatch(BrowserWorkspaceAction.SetSplit(snapshot.split.copy(column = snapshot.split.column.nudged(-500))))
                    }, Modifier.focusRequester(resizeFocus))
                    WorkspaceChip("More left", {
                        session.dispatch(BrowserWorkspaceAction.SetSplit(snapshot.split.copy(column = snapshot.split.column.nudged(500))))
                    })
                    WorkspaceChip("Less top", {
                        session.dispatch(BrowserWorkspaceAction.SetSplit(snapshot.split.copy(row = snapshot.split.row.nudged(-500))))
                    })
                    WorkspaceChip("More top", {
                        session.dispatch(BrowserWorkspaceAction.SetSplit(snapshot.split.copy(row = snapshot.split.row.nudged(500))))
                    })
                }
                Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small)) {
                    WorkspaceChip("Equal sizes", {
                        session.dispatch(BrowserWorkspaceAction.SetSplit(com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceSplit.Even))
                    })
                    WorkspaceChip("Done", { resizeOpen = false; session.saveLocalState() })
                }
            }
        }
        if (controls.addressOpen) {
            Column(Modifier.align(Alignment.Center).background(ReceiverColors.Panel).padding(24.dp)) {
                Text(controls.keyboardState.text.ifBlank { "Enter an address or search" }, color = ReceiverColors.Text)
                ReceiverBrowserKeyboard(controls.keyboardState, controls.keyboard)
            }
        }
        dialog?.let { request ->
            ReceiverWorkspaceDialog(request) { session.answerDialog(request.id, it) }
        }
    }
}

@Composable
private fun WorkspaceChrome(
    state: BrowserWorkspaceState,
    addFocus: FocusRequester,
    onAddPane: () -> Unit,
    allowedLayouts: Set<BrowserWorkspaceLayout>,
    onLayout: (BrowserWorkspaceLayout) -> Unit,
    onResize: () -> Unit,
    onFocusPane: (Long) -> Unit,
    onEnterPage: () -> Unit,
    onToggleMute: (Long) -> Unit,
    onPlayPause: (Long) -> Unit,
    onToggleTheater: (Long) -> Unit,
    onClosePane: (Long) -> Unit,
    onMovePane: (Long, Int) -> Unit,
    onCloseWorkspace: () -> Unit,
) {
    Column(verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Compact)) {
        // One row, not two. Every row of chrome is height taken from the pages, and on a 540dp
        // panel there is width to spare and none to waste vertically.
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = "LAYOUT",
                style = ReceiverType.Caption,
                color = ReceiverColors.Muted,
            )
            WorkspaceChip(
                "One page",
                { onLayout(BrowserWorkspaceLayout.SINGLE) },
                enabled = BrowserWorkspaceLayout.SINGLE in allowedLayouts && state.panes.size == 1,
            )
            WorkspaceChip(
                "Side by side",
                { onLayout(BrowserWorkspaceLayout.SPLIT_HORIZONTAL) },
                enabled = BrowserWorkspaceLayout.SPLIT_HORIZONTAL in allowedLayouts && state.panes.size == 2,
            )
            WorkspaceChip(
                "Stacked",
                { onLayout(BrowserWorkspaceLayout.SPLIT_VERTICAL) },
                enabled = BrowserWorkspaceLayout.SPLIT_VERTICAL in allowedLayouts && state.panes.size == 2,
            )
            WorkspaceChip(
                if (BrowserWorkspaceLayout.GRID_2X2 in allowedLayouts) "Grid" else "Grid unavailable",
                { onLayout(BrowserWorkspaceLayout.GRID_2X2) },
                enabled = BrowserWorkspaceLayout.GRID_2X2 in allowedLayouts && state.panes.size in 3..4,
            )
            Spacer(Modifier.weight(1f))
            // Explicit help only — never auto-open over panes or steal D-pad focus on entry.
            ReceiverHelp()
            WorkspaceChip("Add page", onAddPane, Modifier.focusRequester(addFocus))
            WorkspaceChip("Resize", onResize, enabled = state.panes.size > 1)
            WorkspaceChip("Tabs", onCloseWorkspace)
        }
        Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small)) {
            for (pane in state.panes.sortedBy { it.slot }) {
                val focused = pane.id == state.focusedPaneId
                Box(
                    modifier = Modifier
                        .weight(1f)
                        .border(
                            width = if (focused) 2.dp else 1.dp,
                            color = if (focused) ReceiverColors.Success else ReceiverColors.Line,
                            shape = ReceiverShapes.Small,
                        )
                        .background(ReceiverColors.Panel, ReceiverShapes.Small)
                        .tvClickable(shape = ReceiverShapes.Small, onClick = {
                            if (focused && state.interactionMode == BrowserWorkspaceInteractionMode.WORKSPACE_CHROME) {
                                onEnterPage()
                            } else {
                                onFocusPane(pane.id)
                            }
                        })
                        .padding(horizontal = ReceiverSpace.Compact, vertical = ReceiverSpace.Small)
                        .semantics {
                            contentDescription = buildString {
                                append("Page ${pane.slot + 1}")
                                if (pane.page.title.isNotBlank()) append(", ${pane.page.title}")
                                if (pane.isSuspended) append(", suspended")
                                if (focused) append(", focused")
                            }
                        },
                ) {
                    Row(
                        horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Compact),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                    Text(
                        text = pane.page.title.ifBlank {
                            pane.page.url.ifBlank { if (pane.isSuspended) "Suspended" else "New page" }
                        },
                        style = ReceiverType.Caption,
                        color = ReceiverColors.Text,
                        maxLines = 1,
                    )
                    // Only the focused pane carries controls. Four panes' worth of media, theater
                    // and close chips is what made this chrome tall enough to squeeze the pages.
                    if (BrowserWorkspaceChromePolicy.showsPaneControls(pane.id, state.focusedPaneId)) {
                    Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Compact)) {
                        WorkspaceChip("Move left", {
                            onMovePane(pane.id, pane.slot - 1)
                        }, enabled = pane.slot > 0)
                        WorkspaceChip("Move right", {
                            onMovePane(pane.id, pane.slot + 1)
                        }, enabled = pane.slot < state.panes.size - 1)
                        val muteUnavailable =
                            pane.media.muteApplication == BrowserWorkspaceMuteApplication.UNSUPPORTED ||
                                pane.media.muteApplication == BrowserWorkspaceMuteApplication.FAILED
                        val mutePending = pane.media.pendingMuteRequestId != null
                        WorkspaceChip(
                            when {
                                muteUnavailable -> "Mute unavailable"
                                mutePending -> "Mute requested"
                                pane.media.desiredMuted -> "Request unmute"
                                else -> "Request mute"
                            },
                            { onToggleMute(pane.id) },
                            enabled = pane.rendererResidency.hasRenderer && !muteUnavailable && !mutePending,
                        )
                        val playbackUnavailable =
                            pane.media.observedPlayback == BrowserWorkspacePlaybackObservation.UNAVAILABLE
                        WorkspaceChip(
                            if (pane.media.playbackRequest == null) "Toggle playback" else "Playback requested",
                            { onPlayPause(pane.id) },
                            enabled = pane.rendererResidency.hasRenderer &&
                                !playbackUnavailable &&
                                pane.media.playbackRequest == null,
                        )
                        WorkspaceChip(
                            if (state.theaterPaneId == pane.id) "Exit theater" else "Theater",
                            { onToggleTheater(pane.id) },
                            enabled = state.pageFullscreenPaneId == null &&
                                (state.theaterPaneId == null || state.theaterPaneId == pane.id),
                        )
                        WorkspaceChip("Close", { onClosePane(pane.id) })
                    }
                    }
                    }
                }
            }
        }
    }
}

@Composable
private fun WorkspaceChip(
    label: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    Text(
        text = label.uppercase(),
        style = ReceiverType.Label,
        color = if (enabled) ReceiverColors.Text else ReceiverColors.Muted,
        modifier = modifier
            .tvClickable(enabled = enabled, shape = ReceiverShapes.Pill, onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 8.dp)
            .semantics { contentDescription = label },
    )
}
