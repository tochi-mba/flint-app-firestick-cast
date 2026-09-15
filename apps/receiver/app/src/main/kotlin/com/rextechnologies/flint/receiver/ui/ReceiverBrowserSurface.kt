package com.rextechnologies.flint.receiver.ui

import android.view.KeyEvent
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.focusable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.runtime.withFrameMillis
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.input.key.type
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.testTagsAsResourceId
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.BrowserCursorUi
import com.rextechnologies.flint.receiver.ReceiverService
import com.rextechnologies.flint.receiver.ReceiverUiState
import com.rextechnologies.flint.receiver.browser.BrowserInteractionMode
import com.rextechnologies.flint.receiver.browser.BrowserNativeInput
import com.rextechnologies.flint.receiver.browser.BrowserOverlay
import com.rextechnologies.flint.receiver.clearBrowserFind
import com.rextechnologies.flint.receiver.closeBrowserFromTv
import com.rextechnologies.flint.receiver.createBrowserTvProfile
import com.rextechnologies.flint.receiver.deleteBrowserTvProfile
import com.rextechnologies.flint.receiver.dismissBrowserNotice
import com.rextechnologies.flint.receiver.navigateBrowserFromTv
import com.rextechnologies.flint.receiver.nextBrowserFind
import com.rextechnologies.flint.receiver.previousBrowserFind
import com.rextechnologies.flint.receiver.renameBrowserTvProfile
import com.rextechnologies.flint.receiver.replyBrowserDialogLocal
import com.rextechnologies.flint.receiver.selectBrowserConnectedDeviceProfile
import com.rextechnologies.flint.receiver.selectBrowserTvProfile
import com.rextechnologies.flint.receiver.setBrowserEditing
import com.rextechnologies.flint.receiver.showBrowserNotice
import com.rextechnologies.flint.receiver.startBrowserFind

/**
 * The television's browsing surface: a page, chrome, and any active native dialog.
 *
 * The Activity owns the WebView's whole lifetime and the Service only ever holds the driver
 * adapter, which is what keeps a destroyed view from being written to by a socket callback.
 */
@Composable
internal fun ReceiverBrowserSurface(
    service: ReceiverService?,
    state: ReceiverUiState,
) {
    if (service == null) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(Color.Black)
                .testTag(ReceiverTags.BROWSER_SURFACE)
                .semantics { contentDescription = "Secure browser" },
            contentAlignment = Alignment.Center,
        ) {
            Text(
                text = "Secure browser unavailable",
                color = ReceiverColors.Muted,
                fontSize = 16.sp,
            )
        }
        return
    }

    val controller = rememberBrowserSurfaceController(service)
    LaunchedEffect(controller.overlay) {
        if (controller.overlay == BrowserOverlay.WORKSPACE) {
            controller.dismissOverlays()
            service.openBrowserWorkspace()
        }
    }
    if (state.browserWorkspaceVisible) {
        ReceiverBrowserWorkspaceSurface(
            service = service,
            controller = controller,
            onClose = service::closeBrowserWorkspace,
        )
        return
    }
    controller.dialogOpen = state.browser.dialog != null
    controller.noticeVisible = state.browserNotice != null
    controller.fullscreen = state.browserFullscreen
    controller.canGoBack = state.browser.canGoBack
    controller.tabCount = state.browser.tabs.size.coerceAtLeast(1)
    controller.pageLoading = state.browser.isLoading
    controller.searchEngine = state.browserView.searchEngine

    val surfaceFocus = remember { FocusRequester() }
    val chromeFocus = remember { FocusRequester() }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.Black)
            .testTag(ReceiverTags.BROWSER_SURFACE)
            .semantics { contentDescription = "Secure browser" }
            // Root preview, same shape as the mirror surface: while a tab/menu button holds focus,
            // Back must still hit the ladder here. Handling keys only on the WebView wrapper left
            // overlays stranded — remote Back fell through to the Activity and did nothing.
            .onPreviewKeyEvent { event ->
                when (event.type) {
                    KeyEventType.KeyDown -> {
                        val keyCode = event.nativeKeyEvent.keyCode
                        val repeats = event.nativeKeyEvent.repeatCount
                        // Direction holds are driven by the frame loop. Treating the first Android
                        // key-repeat as a long-press (as Select/Back need) would leak the event and
                        // leave edge scrolling unreliable on Fire TV remotes.
                        if (isBrowserDirectionKey(keyCode) && repeats >= 1) {
                            true
                        } else if (repeats == 1) {
                            controller.onKeyLongPress(keyCode)
                        } else if (repeats > 1) {
                            true
                        } else {
                            controller.onKeyDown(keyCode)
                        }
                    }
                    KeyEventType.KeyUp -> controller.onKeyUp(event.nativeKeyEvent.keyCode)
                    else -> false
                }
            },
    ) {
        // Focus ping-pongs between the page and the chrome's first control, the same shape the
        // mirror surface uses. Without it a D-pad press has no owner and the surface reads as frozen.
        LaunchedEffect(controller.chromeVisible, controller.overlay, controller.leaveConfirmVisible) {
            when {
                controller.leaveConfirmVisible -> {
                    // The prompt owns focus (Keep browsing). Pulling it back to the page surface
                    // left Select clicking through the scrim and the D-pad steering the cursor behind.
                    controller.chromeFocused = false
                    service.setBrowserPageFocusEnabled(false)
                }

                controller.overlay == BrowserOverlay.OMNIBOX ||
                    controller.overlay == BrowserOverlay.FIND ||
                    controller.overlay == BrowserOverlay.PROFILE_NAME -> {
                    controller.chromeFocused = false
                    service.setBrowserPageFocusEnabled(false)
                    withFrameNanos { }
                    runCatching { surfaceFocus.requestFocus() }
                }

                // An overlay owns its own focus. Pulling it back to the page here left the tab
                // switcher on screen with focus behind it, so the D-pad moved nothing and the sheet
                // could not be used at all — found on a Fire TV Stick 4K.
                controller.overlay != BrowserOverlay.NONE -> {
                    controller.chromeFocused = false
                    service.setBrowserPageFocusEnabled(false)
                }

                controller.chromeVisible -> {
                    controller.chromeFocused = true
                    // Drop WebView focus so a normal Select reaches Compose chrome instead of the page.
                    service.setBrowserPageFocusEnabled(false)
                    // Same reason as the leave prompt: the omnibar has only just been composed, so
                    // the control is not placed yet on the frame the request would otherwise use.
                    withFrameNanos { }
                    runCatching { chromeFocus.requestFocus() }
                }

                else -> {
                    controller.chromeFocused = false
                    service.setBrowserPageFocusEnabled(true)
                    runCatching { surfaceFocus.requestFocus() }
                }
            }
        }

        // One repeat loop for a held direction. Frame-paced rather than fixed-delay so the cursor
        // moves with the display instead of against it.
        LaunchedEffect(Unit) {
            var previous = 0L
            while (true) {
                withFrameMillis { frame ->
                    val elapsed = if (previous == 0L) FRAME_MILLIS else (frame - previous).coerceIn(1L, 64L)
                    previous = frame
                    controller.onRepeatFrame(elapsed)
                }
            }
        }

        AndroidView(
            factory = { context ->
                ReceiverBrowserTabBinding(
                    context = context,
                    service = service,
                    onViewportChanged = controller::onViewportChanged,
                    // Both ends need this: the remote turns Select into Enter, and the desktop can
                    // start forwarding keystrokes without anyone arming anything.
                    onEditingChanged = { editing ->
                        controller.editingFocused = editing
                        service.setBrowserEditing(editing)
                    },
                ).also { binding ->
                    binding.host.tag = binding
                    service.attachBrowserTabSurface(binding)
                }.host
            },
            update = { /* the driver stays attached for the surface's lifetime */ },
            onRelease = { host ->
                (host.tag as? ReceiverBrowserTabBinding)?.let { binding ->
                    service.detachBrowserTabSurface(binding)
                    binding.destroy()
                }
                host.tag = null
            },
            modifier = Modifier
                .fillMaxSize()
                .focusRequester(surfaceFocus)
                .focusable(),
        )

        // Chrome and both pointers step aside for a page that has taken the whole screen.
        if (!state.browserFullscreen) {
            if (state.browser.failure != null) {
                ReceiverBrowserErrorPage(
                    page = state.browser,
                    onRetry = service.browserCoordinator()::reload,
                    onEditAddress = { controller.openOverlay(BrowserOverlay.OMNIBOX) },
                )
            } else if (state.browser.tabs.firstOrNull { it.id == state.browser.activeTabId }?.url.isNullOrBlank() &&
                state.browser.url.isBlank()
            ) {
                ReceiverBrowserNewTabPage(
                    onSearch = { controller.openOverlay(BrowserOverlay.OMNIBOX) },
                    claimFocus = !controller.chromeVisible && controller.overlay == BrowserOverlay.NONE,
                )
            }

            BrowserHostCursor(cursor = state.browserCursor)

            ReceiverBrowserCursor(
                cursor = controller.cursor,
                pressed = controller.cursorPressed,
                visible = controller.mode == BrowserInteractionMode.CURSOR &&
                    controller.overlay == BrowserOverlay.NONE,
            )

            if (controller.chromeVisible && controller.overlay == BrowserOverlay.NONE) {
                Box(
                    modifier = Modifier
                        .fillMaxSize(ReceiverOverscan.CONTENT_FRACTION)
                        .align(Alignment.Center),
                    contentAlignment = Alignment.TopCenter,
                ) {
                    Column(verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Small)) {
                        ReceiverBrowserTabStrip(
                            tabs = state.browser.tabs,
                            activeId = state.browser.activeTabId,
                            onSelect = controller::selectTab,
                            onClose = controller::closeTab,
                            onNew = controller::newTab,
                        )
                        ReceiverBrowserOmnibar(
                            page = state.browser,
                            tabCount = state.browser.tabs.size.coerceAtLeast(1),
                            activeTab = (
                                state.browser.tabs.indexOfFirst {
                                    it.id == state.browser.activeTabId
                                } + 1
                                ).coerceAtLeast(1),
                            firstControl = chromeFocus,
                            onAction = controller::onOmnibarAction,
                        )
                    }
                }
            } else {
                // The page's own status, kept for the case where the chrome is away. It is the only
                // thing on screen that says a page is still loading once the omnibar withdraws.
                ReceiverBrowserChrome(page = state.browser)
            }

            if (state.browserView.find.active && controller.overlay == BrowserOverlay.NONE) {
                ReceiverBrowserFindBar(
                    find = state.browserView.find,
                    onPrevious = service::previousBrowserFind,
                    onNext = service::nextBrowserFind,
                    onClose = service::clearBrowserFind,
                    modifier = Modifier
                        .align(Alignment.TopEnd)
                        .padding(ReceiverSpace.Huge),
                )
            }

            ReceiverBrowserOverlayHost(service, state, controller)

            if (controller.leaveConfirmVisible) {
                ReceiverBrowserLeavePrompt(
                    onLeave = controller::confirmLeave,
                    onStay = controller::cancelLeaveConfirm,
                )
            }
        }

        state.browserNotice?.let { notice ->
            ReceiverBrowserNotice(
                notice = notice,
                onExpired = { service.dismissBrowserNotice() },
            )
        }

        state.browser.dialog?.let { dialog ->
            ReceiverBrowserDialog(
                dialog = dialog,
                onConfirm = { service.replyBrowserDialogLocal(accepted = true) },
                onCancel = { service.replyBrowserDialogLocal(accepted = false) },
            )
        }
    }
}

/**
 * A ring + crosshair over the page so someone looking at the television can see the Windows
 * touchpad aim before a click lands.
 */
@Composable
private fun BrowserHostCursor(cursor: BrowserCursorUi) {
    if (!cursor.visible) {
        return
    }

    BoxWithConstraints(modifier = Modifier.fillMaxSize()) {
        val diameter = if (cursor.pressed) 36.dp else 28.dp
        val x = maxWidth * cursor.xFraction - diameter / 2
        val y = maxHeight * cursor.yFraction - diameter / 2
        Canvas(
            modifier = Modifier
                .offset(x = x, y = y)
                .size(diameter)
                .semantics { contentDescription = "Host cursor" }
                .testTag(ReceiverTags.BROWSER_HOST_CURSOR),
        ) {
            val stroke = Stroke(width = 3.dp.toPx())
            val center = Offset(size.width / 2f, size.height / 2f)
            val radius = size.minDimension / 2f - stroke.width
            drawCircle(
                color = ReceiverColors.Signal.copy(alpha = if (cursor.pressed) 0.35f else 0.2f),
                radius = radius,
                center = center,
            )
            drawCircle(
                color = ReceiverColors.Signal,
                radius = radius,
                center = center,
                style = stroke,
            )
            val arm = radius * 0.55f
            drawLine(
                ReceiverColors.Signal,
                Offset(center.x - arm, center.y),
                Offset(center.x + arm, center.y),
                strokeWidth = stroke.width,
            )
            drawLine(
                ReceiverColors.Signal,
                Offset(center.x, center.y - arm),
                Offset(center.x, center.y + arm),
                strokeWidth = stroke.width,
            )
        }
    }
}

/** One frame at 60Hz, used before the loop has two timestamps to subtract. */
private const val FRAME_MILLIS = 16L

private fun isBrowserDirectionKey(keyCode: Int): Boolean =
    keyCode == KeyEvent.KEYCODE_DPAD_UP ||
        keyCode == KeyEvent.KEYCODE_DPAD_DOWN ||
        keyCode == KeyEvent.KEYCODE_DPAD_LEFT ||
        keyCode == KeyEvent.KEYCODE_DPAD_RIGHT

/**
 * Builds the surface's state machine and connects it to the receiver.
 *
 * Remembered against the service so a recomposition does not reset the interaction mode, the cursor
 * position, or a half-typed address.
 */
@Composable
private fun rememberBrowserSurfaceController(service: ReceiverService): BrowserSurfaceController =
    remember(service) {
        BrowserSurfaceController(
            actions = object : BrowserSurfaceActions {
                override fun dispatch(input: BrowserNativeInput) =
                    service.browserCoordinator().dispatchInput(input)

                override fun viewport(): Pair<Int, Int>? = service.browserCoordinator().viewport()

                override fun goBack() = service.browserCoordinator().goBack()

                override fun goForward() = service.browserCoordinator().goForward()

                override fun reload() = service.browserCoordinator().reload()

                override fun stopLoading() = service.browserCoordinator().stopLoading()

                override fun navigate(url: String) = service.navigateBrowserFromTv(url)

                override fun openTab() = service.openBrowserTab()

                override fun selectTab(tabId: Long) = service.selectBrowserTab(tabId)

                override fun closeTab(tabId: Long) = service.closeBrowserTab(tabId)

                override fun closeActiveTab() = service.closeBrowserTab(
                    service.uiState.value.browser.activeTabId,
                )

                override fun startFind(query: String) = service.startBrowserFind(query)

                override fun findNext() = service.nextBrowserFind()

                override fun findPrevious() = service.previousBrowserFind()

                override fun clearFind() = service.clearBrowserFind()

                override fun selectTvProfile(profileId: String) =
                    service.selectBrowserTvProfile(profileId)

                override fun selectConnectedDeviceProfile() =
                    service.selectBrowserConnectedDeviceProfile()

                override fun createTvProfile(name: String) = service.createBrowserTvProfile(name)

                override fun renameTvProfile(profileId: String, name: String) =
                    service.renameBrowserTvProfile(profileId, name)

                override fun deleteTvProfile(profileId: String) =
                    service.deleteBrowserTvProfile(profileId)

                override fun closeBrowser() = service.closeBrowserFromTv()

                override fun dismissNotice() = service.dismissBrowserNotice()

                override fun cancelDialog() = service.replyBrowserDialogLocal(accepted = false)

                override fun exitFullscreen() = service.exitBrowserFullscreen()

                override fun notice(message: String) = service.showBrowserNotice(message)
            },
        )
    }

/**
 * What the omnibox offers under the field.
 *
 * Bookmarks and history arrive with the library store; until then the search row alone is honest
 * and still useful, because looking something up is the commonest thing anyone types here.
 */
private fun omniboxSuggestions(controller: BrowserSurfaceController): List<OmniboxSuggestion> {
    val typed = controller.keyboardState.text.trim()
    if (typed.isEmpty()) {
        return emptyList()
    }
    return listOf(
        OmniboxSuggestion(
            glyph = "⌕",
            primary = "Search ${controller.searchEngine.name} for \u201C$typed\u201D",
        ),
    )
}

/**
 * The prompt shown when Back would otherwise end the session.
 *
 * Deliberately a stop rather than a confirmation dialog with a default: losing a browsing session
 * to one stray press on a remote that has Back next to the D-pad is the failure this exists for.
 */
@Composable
internal fun ReceiverBrowserLeavePrompt(onLeave: () -> Unit, onStay: () -> Unit) {
    val stayFocus = remember { FocusRequester() }
    // A frame is waited for before the request.
    //
    // Requesting focus during first composition happens before the node is placed, so it fails
    // silently and focus falls to whatever the default traversal picks — which here was *Leave*.
    // A destructive default on a remote whose Back and Select sit next to each other is the exact
    // accident this prompt exists to prevent, and it was reproduced on a Fire TV Stick 4K.
    LaunchedEffect(Unit) {
        withFrameNanos { }
        runCatching { stayFocus.requestFocus() }
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(ReceiverColors.Scrim)
            .semantics { testTagsAsResourceId = true }
            .testTag(ReceiverTags.BROWSER_LEAVE_PROMPT),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier
                .background(ReceiverColors.Raised, ReceiverShapes.Large)
                .padding(ReceiverSpace.XLarge),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Medium),
        ) {
            Text(text = "Leave the browser?", style = ReceiverType.Title, color = ReceiverColors.Text)
            Text(
                text = "The page you are on will be closed.",
                style = ReceiverType.Body,
                color = ReceiverColors.Muted,
            )
            Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Compact)) {
                TvActionButton(
                    label = "Keep browsing",
                    enabled = true,
                    onClick = onStay,
                    modifier = Modifier.focusRequester(stayFocus),
                )
                TvActionButton(label = "Leave", enabled = true, onClick = onLeave)
            }
        }
    }
}
