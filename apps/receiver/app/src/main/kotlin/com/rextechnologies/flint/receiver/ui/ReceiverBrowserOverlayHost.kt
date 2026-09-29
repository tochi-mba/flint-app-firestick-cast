package com.rextechnologies.flint.receiver.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.ReceiverService
import com.rextechnologies.flint.receiver.ReceiverUiState
import com.rextechnologies.flint.receiver.browser.BrowserKeyboard
import com.rextechnologies.flint.receiver.browser.BrowserOverlay
import com.rextechnologies.flint.receiver.browserNetworkSettings
import com.rextechnologies.flint.receiver.browserVpnCapability
import com.rextechnologies.flint.receiver.browserVpnState
import com.rextechnologies.flint.receiver.clearBrowserVpn
import com.rextechnologies.flint.receiver.connectBrowserVpn
import com.rextechnologies.flint.receiver.cycleBrowserSearchEngine
import com.rextechnologies.flint.receiver.cycleBrowserUserAgent
import com.rextechnologies.flint.receiver.navigateBrowserFromTv
import com.rextechnologies.flint.receiver.removeBrowserBookmark
import com.rextechnologies.flint.receiver.requestBrowserDataClear
import com.rextechnologies.flint.receiver.resetBrowserZoom
import com.rextechnologies.flint.receiver.toggleBrowserBookmark
import com.rextechnologies.flint.receiver.toggleBrowserDarkMode
import com.rextechnologies.flint.receiver.toggleBrowserRequireVpn
import com.rextechnologies.flint.receiver.toggleBrowserVpnAutoConnect
import com.rextechnologies.flint.receiver.toggleBrowserVpnEnabled
import com.rextechnologies.flint.receiver.zoomBrowserIn
import com.rextechnologies.flint.receiver.zoomBrowserOut

/** Exhaustive renderer for every modal state owned by the browser surface controller. */
@Composable
internal fun ReceiverBrowserOverlayHost(
    service: ReceiverService,
    state: ReceiverUiState,
    controller: BrowserSurfaceController,
) {
    when (controller.overlay) {
        BrowserOverlay.NONE -> Unit

        BrowserOverlay.OMNIBOX -> ReceiverBrowserOmnibox(
            state = controller.keyboardState,
            keyboard = remember { BrowserKeyboard() },
            suggestions = omniboxSuggestions(controller),
            selectedSuggestion = null,
            desktopTypingAvailable = state.browserProfiles.deviceAvailable,
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

        BrowserOverlay.PROFILE_NAME -> ReceiverBrowserOmnibox(
            state = controller.keyboardState,
            keyboard = remember { BrowserKeyboard() },
            suggestions = emptyList(),
            selectedSuggestion = null,
            desktopTypingAvailable = false,
            heading = if (controller.editingProfileId == null) {
                "NAME THE NEW TV PROFILE"
            } else {
                "RENAME TV PROFILE"
            },
            fieldDescription = "TV profile name",
            placeholder = "Type a short name",
            submitLabel = "save",
            sheetTag = ReceiverTags.BROWSER_PROFILE_NAME,
        )

        BrowserOverlay.TABS -> ReceiverBrowserTabSwitcher(
            tabs = state.browser.tabs,
            activeId = state.browser.activeTabId,
            onSelect = controller::selectTab,
            onClose = controller::closeTab,
            onNew = controller::newTab,
        )

        BrowserOverlay.MENU -> ReceiverBrowserMenuSheet(
            view = state.browserView,
            bookmarkCount = state.browserLibrary.bookmarks.size,
            historyCount = state.browserLibrary.history.size,
            bookmarked = state.browserLibrary.bookmarks.any { it.url == state.browser.url },
            profiles = state.browserProfiles,
            onZoomOut = service::zoomBrowserOut,
            onZoomReset = service::resetBrowserZoom,
            onZoomIn = service::zoomBrowserIn,
            onUserAgent = service::cycleBrowserUserAgent,
            onDarkMode = service::toggleBrowserDarkMode,
            onFind = { controller.openOverlay(BrowserOverlay.FIND) },
            onBookmarks = { controller.openOverlay(BrowserOverlay.BOOKMARKS) },
            onHistory = { controller.openOverlay(BrowserOverlay.HISTORY) },
            onBookmark = service::toggleBrowserBookmark,
            onSearchEngine = service::cycleBrowserSearchEngine,
            onProfiles = { controller.openOverlay(BrowserOverlay.PROFILES) },
            onClearData = { controller.openOverlay(BrowserOverlay.CLEAR_DATA) },
            onWorkspace = { controller.openOverlay(BrowserOverlay.WORKSPACE) },
            onNetwork = { controller.openOverlay(BrowserOverlay.NETWORK) },
        )

        BrowserOverlay.WORKSPACE -> Unit // The root switches surfaces; never mount two browser hosts.

        BrowserOverlay.NETWORK -> NetworkOverlay(service = service, controller = controller)

        BrowserOverlay.BOOKMARKS -> ReceiverBrowserLibrarySheet(
            title = "Bookmarks · ${state.browserProfiles.activeName}",
            entries = state.browserLibrary.bookmarks,
            onOpen = { entry ->
                controller.dismissOverlays()
                service.navigateBrowserFromTv(entry.url)
            },
            onRemove = { entry -> service.removeBrowserBookmark(entry.url) },
        )

        BrowserOverlay.HISTORY -> ReceiverBrowserLibrarySheet(
            title = "History · ${state.browserProfiles.activeName}",
            entries = state.browserLibrary.history,
            onOpen = { entry ->
                controller.dismissOverlays()
                service.navigateBrowserFromTv(entry.url)
            },
        )

        BrowserOverlay.CLEAR_DATA -> ReceiverBrowserClearDataPrompt(
            profiles = state.browserProfiles,
            onCancel = { controller.openOverlay(BrowserOverlay.MENU) },
            onClear = {
                controller.dismissOverlays()
                service.requestBrowserDataClear()
            },
        )

        BrowserOverlay.PROFILES -> ReceiverBrowserProfilesSheet(
            profiles = state.browserProfiles,
            onSelectTv = { controller.chooseTvProfile(it.id) },
            onSelectDevice = controller::chooseConnectedDeviceProfile,
            onCreate = controller::beginCreateProfile,
            onRename = controller::beginRenameProfile,
            onDelete = controller::requestDeleteProfile,
        )

        BrowserOverlay.PROFILE_DELETE -> controller.deletingProfile?.let { profile ->
            ReceiverBrowserProfileDeletePrompt(
                profile = profile,
                onCancel = controller::cancelDeleteProfile,
                onDelete = controller::confirmDeleteProfile,
            )
        } ?: ReceiverBrowserProfilesSheet(
            profiles = state.browserProfiles,
            onSelectTv = { controller.chooseTvProfile(it.id) },
            onSelectDevice = controller::chooseConnectedDeviceProfile,
            onCreate = controller::beginCreateProfile,
            onRename = controller::beginRenameProfile,
            onDelete = controller::requestDeleteProfile,
        )
    }
}

@Composable
private fun NetworkOverlay(service: ReceiverService, controller: BrowserSurfaceController) {
    var settings by remember { mutableStateOf(service.browserNetworkSettings()) }
    var vpnState by remember { mutableStateOf(service.browserVpnState().value) }
    val capability = remember { service.browserVpnCapability() }

    LaunchedEffect(service) {
        service.browserVpnState().collect { vpnState = it }
    }

    fun refreshSettings() {
        settings = service.browserNetworkSettings()
    }

    ReceiverBrowserNetworkSheet(
        settings = settings,
        capability = capability,
        vpnState = vpnState,
        onToggleEnabled = {
            service.toggleBrowserVpnEnabled()
            refreshSettings()
        },
        onToggleAutoConnect = {
            service.toggleBrowserVpnAutoConnect()
            refreshSettings()
        },
        onToggleRequireVpn = {
            service.toggleBrowserRequireVpn()
            refreshSettings()
        },
        onConnect = {
            service.connectBrowserVpn()
            refreshSettings()
        },
        onClear = {
            service.clearBrowserVpn()
            refreshSettings()
        },
        onClose = { controller.openOverlay(BrowserOverlay.MENU) },
    )
}

/** Suggestions remain deliberately small until the typed string can be matched safely. */
private fun omniboxSuggestions(controller: BrowserSurfaceController): List<OmniboxSuggestion> {
    val typed = controller.keyboardState.text.trim()
    if (typed.isEmpty()) return emptyList()
    return listOf(
        OmniboxSuggestion(
            glyph = "⌕",
            primary = "Search ${controller.searchEngine.name} for “$typed”",
        ),
    )
}
