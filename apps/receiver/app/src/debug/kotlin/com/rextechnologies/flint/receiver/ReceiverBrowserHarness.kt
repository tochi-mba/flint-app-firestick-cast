package com.rextechnologies.flint.receiver

import android.view.KeyEvent
import androidx.compose.foundation.background
import androidx.compose.foundation.focusable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.remember
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.input.key.type
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.testTagsAsResourceId
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.browser.BrowserInteractionMode
import com.rextechnologies.flint.receiver.browser.BrowserKeyboard
import com.rextechnologies.flint.receiver.browser.BrowserLibraryEntry
import com.rextechnologies.flint.receiver.browser.BrowserLibraryProfile
import com.rextechnologies.flint.receiver.browser.BrowserNativeInput
import com.rextechnologies.flint.receiver.browser.BrowserOverlay
import com.rextechnologies.flint.receiver.browser.BrowserProfilesUiState
import com.rextechnologies.flint.receiver.browser.BrowserTab
import com.rextechnologies.flint.receiver.browser.BrowserViewState
import com.rextechnologies.flint.receiver.ui.BrowserSurfaceActions
import com.rextechnologies.flint.receiver.ui.BrowserSurfaceController
import com.rextechnologies.flint.receiver.ui.ReceiverBrowserClearDataPrompt
import com.rextechnologies.flint.receiver.ui.ReceiverBrowserCursor
import com.rextechnologies.flint.receiver.ui.ReceiverBrowserLeavePrompt
import com.rextechnologies.flint.receiver.ui.ReceiverBrowserLibrarySheet
import com.rextechnologies.flint.receiver.ui.ReceiverBrowserMenuSheet
import com.rextechnologies.flint.receiver.ui.ReceiverBrowserNetworkSheet
import com.rextechnologies.flint.receiver.ui.ReceiverBrowserOmnibar
import com.rextechnologies.flint.receiver.ui.ReceiverBrowserOmnibox
import com.rextechnologies.flint.receiver.ui.ReceiverBrowserProfilesSheet
import com.rextechnologies.flint.receiver.ui.ReceiverBrowserTabSwitcher
import com.rextechnologies.flint.receiver.ui.ReceiverColors
import com.rextechnologies.flint.receiver.ui.ReceiverSpace
import com.rextechnologies.flint.receiver.ui.ReceiverTags
import com.rextechnologies.flint.receiver.ui.ReceiverType
import com.rextechnologies.flint.receiver.browser.BrowserVpnState
import com.rextechnologies.flint.receiver.browser.ProfileNetworkSettings
import com.rextechnologies.flint.receiver.browser.VpnCapability

/** Where the deterministic browser harness begins, so UiAutomator need not invent key chords. */
enum class BrowserHarnessStart {
    PAGE,
    CHROME,
    MENU,
    TABS,
    LEAVE,
    OMNIBOX,
    FIND,
    BOOKMARKS,
    HISTORY,
    CLEAR_DATA,
    PROFILES,
}

/**
 * Deterministic browser chrome for Fire TV UiAutomator.
 *
 * Uses the real [BrowserSurfaceController] key ladder and Compose chrome, without a WebView or
 * [ReceiverService]. That lets connectedAndroidTest prove Back/focus ownership for every overlay
 * without pairing a host.
 */
@Composable
internal fun ReceiverBrowserHarness(
    marker: String,
    start: BrowserHarnessStart = BrowserHarnessStart.PAGE,
    onControllerReady: (BrowserSurfaceController) -> Unit = {},
) {
    val actions = remember { BrowserHarnessActions() }
    val controller = remember {
        BrowserSurfaceController(actions).also { surface ->
            applyHarnessStart(surface, start)
        }
    }
    // Hand-off after the composition commits so headless tests can drive the ladder immediately.
    SideEffect {
        onControllerReady(controller)
    }
    val surfaceFocus = remember { FocusRequester() }
    val chromeFocus = remember { FocusRequester() }
    val page = remember {
        BrowserSurfaceUi(
            url = "https://example.test/harness",
            title = "Harness tab",
            canGoBack = false,
            canGoForward = false,
        )
    }
    val tabs = remember {
        listOf(
            BrowserTab(id = 1L, title = "Harness tab", url = "https://example.test/harness", live = true),
            BrowserTab(id = 2L, title = "Second tab", url = "https://example.test/two", live = false),
        )
    }
    val view = remember { BrowserViewState() }
    val profiles = remember {
        BrowserProfilesUiState(
            tvProfiles = listOf(
                BrowserLibraryProfile("default", "Default"),
                BrowserLibraryProfile("kids", "Kids"),
            ),
            activeTvProfileId = "default",
        )
    }
    val bookmarks = remember {
        listOf(
            BrowserLibraryEntry("https://example.test/a", "Example A", lastVisitedMs = 1),
            BrowserLibraryEntry("https://example.test/b", "Example B", lastVisitedMs = 2),
        )
    }
    val history = remember {
        listOf(
            BrowserLibraryEntry("https://example.test/visited", "Visited page", lastVisitedMs = 3),
        )
    }

    LaunchedEffect(controller.chromeVisible, controller.overlay, controller.leaveConfirmVisible) {
        when {
            controller.leaveConfirmVisible -> Unit
            controller.overlay != BrowserOverlay.NONE -> Unit
            controller.chromeVisible -> {
                controller.chromeFocused = true
                withFrameNanos { }
                runCatching { chromeFocus.requestFocus() }
            }
            else -> {
                controller.chromeFocused = false
                runCatching { surfaceFocus.requestFocus() }
            }
        }
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.Black)
            .semantics { testTagsAsResourceId = true }
            .testTag(ReceiverTags.BROWSER_SURFACE)
            .onPreviewKeyEvent { event ->
                when (event.type) {
                    KeyEventType.KeyDown -> {
                        val keyCode = event.nativeKeyEvent.keyCode
                        val repeats = event.nativeKeyEvent.repeatCount
                        if (isHarnessDirectionKey(keyCode) && repeats >= 1) {
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
        // Launch marker kept off the full-screen focus target so Fire OS a11y still exposes the
        // page title and chrome nodes underneath (a parent contentDescription was swallowing them).
        Box(
            modifier = Modifier
                .align(Alignment.TopStart)
                .semantics { contentDescription = marker },
        )

        Box(
            modifier = Modifier
                .fillMaxSize()
                .focusRequester(surfaceFocus)
                .focusable()
                .testTag("receiver-browser-harness-page")
                .semantics { contentDescription = "Harness page title" },
            contentAlignment = Alignment.Center,
        ) {
            Text(
                text = "HARNESS PAGE",
                style = ReceiverType.Title,
                color = ReceiverColors.Muted,
                modifier = Modifier
                    .testTag("receiver-browser-harness-title")
                    .semantics { contentDescription = "Harness page title" },
            )
        }

        ReceiverBrowserCursor(
            cursor = controller.cursor,
            pressed = controller.cursorPressed,
            visible = controller.mode == BrowserInteractionMode.CURSOR &&
                controller.overlay == BrowserOverlay.NONE &&
                !controller.leaveConfirmVisible,
        )

        if (controller.chromeVisible && controller.overlay == BrowserOverlay.NONE) {
            Column(
                modifier = Modifier
                    .align(Alignment.TopCenter)
                    .fillMaxWidth(0.9f)
                    .padding(top = ReceiverSpace.Huge),
                verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Small),
            ) {
                ReceiverBrowserOmnibar(
                    page = page,
                    tabCount = tabs.size,
                    activeTab = 1,
                    firstControl = chromeFocus,
                    onAction = controller::onOmnibarAction,
                )
            }
        }

        when (controller.overlay) {
            BrowserOverlay.NONE -> Unit
            BrowserOverlay.OMNIBOX -> ReceiverBrowserOmnibox(
                state = controller.keyboardState,
                keyboard = remember { BrowserKeyboard() },
                suggestions = emptyList(),
                selectedSuggestion = null,
                desktopTypingAvailable = false,
            )
            BrowserOverlay.FIND -> ReceiverBrowserOmnibox(
                state = controller.keyboardState,
                keyboard = remember { BrowserKeyboard() },
                suggestions = emptyList(),
                selectedSuggestion = null,
                desktopTypingAvailable = false,
                heading = "FIND IN THIS PAGE",
                fieldDescription = "Find in page",
                placeholder = "Type a word or phrase",
                submitLabel = "find",
            )
            BrowserOverlay.MENU -> ReceiverBrowserMenuSheet(
                view = view,
                bookmarkCount = bookmarks.size,
                historyCount = history.size,
                bookmarked = false,
                profiles = profiles,
                onZoomOut = {},
                onZoomReset = {},
                onZoomIn = {},
                onUserAgent = {},
                onDarkMode = {},
                onFind = { controller.openOverlay(BrowserOverlay.FIND) },
                onBookmarks = { controller.openOverlay(BrowserOverlay.BOOKMARKS) },
                onHistory = { controller.openOverlay(BrowserOverlay.HISTORY) },
                onBookmark = {},
                onSearchEngine = {},
                onProfiles = { controller.openOverlay(BrowserOverlay.PROFILES) },
                onClearData = { controller.openOverlay(BrowserOverlay.CLEAR_DATA) },
                onWorkspace = { controller.openOverlay(BrowserOverlay.WORKSPACE) },
                onNetwork = { controller.openOverlay(BrowserOverlay.NETWORK) },
            )
            BrowserOverlay.TABS -> ReceiverBrowserTabSwitcher(
                tabs = tabs,
                activeId = 1L,
                onSelect = { controller.dismissOverlays() },
                onClose = { controller.dismissOverlays() },
                onNew = controller::newTab,
            )
            BrowserOverlay.BOOKMARKS -> ReceiverBrowserLibrarySheet(
                title = "Bookmarks · ${profiles.activeName}",
                entries = bookmarks,
                onOpen = { controller.dismissOverlays() },
                onRemove = {},
            )
            BrowserOverlay.HISTORY -> ReceiverBrowserLibrarySheet(
                title = "History · ${profiles.activeName}",
                entries = history,
                onOpen = { controller.dismissOverlays() },
            )
            BrowserOverlay.CLEAR_DATA -> ReceiverBrowserClearDataPrompt(
                profiles = profiles,
                onCancel = { controller.openOverlay(BrowserOverlay.MENU) },
                onClear = { controller.dismissOverlays() },
            )
            BrowserOverlay.PROFILES -> ReceiverBrowserProfilesSheet(
                profiles = profiles,
                onSelectTv = { controller.dismissOverlays() },
                onSelectDevice = { controller.dismissOverlays() },
                onCreate = controller::beginCreateProfile,
                onRename = {},
                onDelete = {},
            )
            BrowserOverlay.WORKSPACE -> Text(
                text = "BROWSER WORKSPACE",
                style = ReceiverType.Body,
                color = ReceiverColors.Text,
                modifier = Modifier.testTag(ReceiverTags.BROWSER_WORKSPACE),
            )
            BrowserOverlay.NETWORK -> ReceiverBrowserNetworkSheet(
                settings = ProfileNetworkSettings(),
                capability = VpnCapability(preparable = false, reason = "Harness"),
                vpnState = BrowserVpnState.Idle,
                onToggleEnabled = {},
                onToggleAutoConnect = {},
                onToggleRequireVpn = {},
                onConnect = {},
                onClear = {},
                onClose = { controller.openOverlay(BrowserOverlay.MENU) },
            )
            BrowserOverlay.PROFILE_NAME,
            BrowserOverlay.PROFILE_DELETE,
            -> Unit
        }

        if (controller.leaveConfirmVisible) {
            ReceiverBrowserLeavePrompt(
                onLeave = controller::confirmLeave,
                onStay = controller::cancelLeaveConfirm,
            )
        }
    }
}

private fun applyHarnessStart(surface: BrowserSurfaceController, start: BrowserHarnessStart) {
    when (start) {
        BrowserHarnessStart.PAGE -> Unit
        BrowserHarnessStart.CHROME -> {
            surface.showChrome()
            surface.chromeFocused = true
        }
        BrowserHarnessStart.MENU -> {
            surface.showChrome()
            surface.openOverlay(BrowserOverlay.MENU)
        }
        BrowserHarnessStart.TABS -> {
            surface.showChrome()
            surface.openOverlay(BrowserOverlay.TABS)
        }
        BrowserHarnessStart.LEAVE -> surface.showLeaveConfirm()
        BrowserHarnessStart.OMNIBOX -> {
            surface.showChrome()
            surface.openOverlay(BrowserOverlay.OMNIBOX)
        }
        BrowserHarnessStart.FIND -> {
            surface.showChrome()
            surface.openOverlay(BrowserOverlay.FIND)
        }
        BrowserHarnessStart.BOOKMARKS -> {
            surface.showChrome()
            surface.openOverlay(BrowserOverlay.BOOKMARKS)
        }
        BrowserHarnessStart.HISTORY -> {
            surface.showChrome()
            surface.openOverlay(BrowserOverlay.HISTORY)
        }
        BrowserHarnessStart.CLEAR_DATA -> {
            surface.showChrome()
            surface.openOverlay(BrowserOverlay.CLEAR_DATA)
        }
        BrowserHarnessStart.PROFILES -> {
            surface.showChrome()
            surface.openOverlay(BrowserOverlay.PROFILES)
        }
    }
}

private fun isHarnessDirectionKey(keyCode: Int): Boolean =
    keyCode == KeyEvent.KEYCODE_DPAD_UP ||
        keyCode == KeyEvent.KEYCODE_DPAD_DOWN ||
        keyCode == KeyEvent.KEYCODE_DPAD_LEFT ||
        keyCode == KeyEvent.KEYCODE_DPAD_RIGHT

private class BrowserHarnessActions : BrowserSurfaceActions {
    override fun dispatch(input: BrowserNativeInput) = Unit
    override fun viewport(): Pair<Int, Int> = 1920 to 1080
    override fun goBack() = Unit
    override fun goForward() = Unit
    override fun reload() = Unit
    override fun stopLoading() = Unit
    override fun navigate(url: String) = Unit
    override fun openTab() = Unit
    override fun selectTab(tabId: Long) = Unit
    override fun closeTab(tabId: Long) = Unit
    override fun closeActiveTab() = Unit
    override fun startFind(query: String) = Unit
    override fun findNext() = Unit
    override fun findPrevious() = Unit
    override fun clearFind() = Unit
    override fun selectTvProfile(profileId: String) = Unit
    override fun selectConnectedDeviceProfile() = Unit
    override fun createTvProfile(name: String) = Unit
    override fun renameTvProfile(profileId: String, name: String) = Unit
    override fun deleteTvProfile(profileId: String) = Unit
    override fun closeBrowser() = Unit
    override fun dismissNotice() = Unit
    override fun cancelDialog() = Unit
    override fun exitFullscreen() = Unit
    override fun notice(message: String) = Unit
}
