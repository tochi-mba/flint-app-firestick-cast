package com.rextechnologies.flint.receiver.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.browser.BrowserUserAgentMode
import com.rextechnologies.flint.receiver.browser.BrowserProfileSource
import com.rextechnologies.flint.receiver.browser.BrowserProfilesUiState
import com.rextechnologies.flint.receiver.browser.BrowserViewState

/** The page tools that do not deserve permanent space in the omnibar. */
@Composable
internal fun ReceiverBrowserMenuSheet(
    view: BrowserViewState,
    bookmarkCount: Int,
    historyCount: Int,
    bookmarked: Boolean,
    profiles: BrowserProfilesUiState,
    onZoomOut: () -> Unit,
    onZoomReset: () -> Unit,
    onZoomIn: () -> Unit,
    onUserAgent: () -> Unit,
    onDarkMode: () -> Unit,
    onFind: () -> Unit,
    onBookmarks: () -> Unit,
    onHistory: () -> Unit,
    onBookmark: () -> Unit,
    onSearchEngine: () -> Unit,
    onProfiles: () -> Unit,
    onClearData: () -> Unit,
    onWorkspace: () -> Unit,
    onNetwork: () -> Unit,
) {
    val firstFocus = remember { FocusRequester() }
    LaunchedEffect(Unit) { firstFocus.requestFocus() }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(ReceiverColors.Scrim)
            .testTag(ReceiverTags.BROWSER_MENU),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth(ReceiverOverscan.CONTENT_FRACTION)
                .background(ReceiverColors.Panel, ReceiverShapes.Large)
                .border(ReceiverSpace.Hairline, ReceiverColors.Line, ReceiverShapes.Large)
                .padding(ReceiverSpace.Large),
            verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Medium),
        ) {
            Text(text = "BROWSER TOOLS", style = ReceiverType.Label, color = ReceiverColors.Muted)

            Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small)) {
                MenuAction(
                    glyph = if (profiles.activeSource == BrowserProfileSource.TV) "THIS TV" else "DEVICE",
                    label = profiles.activeName,
                    onClick = onProfiles,
                    modifier = Modifier.weight(1f),
                    detail = profiles.storageDescription,
                )
            }

            Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small)) {
                MenuAction("−", "Zoom out", onZoomOut, Modifier.focusRequester(firstFocus))
                MenuAction("${view.zoomPercent}%", "Reset zoom", onZoomReset, Modifier.weight(1f))
                MenuAction("+", "Zoom in", onZoomIn)
                MenuAction("FIND", "Find in page", onFind, Modifier.weight(1f))
            }

            Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small)) {
                MenuAction(
                    glyph = when (view.userAgentMode) {
                        BrowserUserAgentMode.TV -> "TV"
                        BrowserUserAgentMode.DESKTOP -> "PC"
                        BrowserUserAgentMode.MOBILE -> "MOBILE"
                    },
                    label = "Site layout",
                    onClick = onUserAgent,
                    modifier = Modifier.weight(1f),
                )
                MenuAction(
                    glyph = if (view.darkModeEnabled) "DARK ON" else "DARK OFF",
                    label = "Page darkening",
                    onClick = onDarkMode,
                    modifier = Modifier.weight(1f),
                )
                MenuAction("SEARCH", view.searchEngine.name, onSearchEngine, Modifier.weight(1f))
            }

            Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small)) {
                MenuAction(
                    if (bookmarked) "★ SAVED" else "☆ SAVE",
                    "Current page",
                    onBookmark,
                    Modifier.weight(1f),
                )
                MenuAction("★ $bookmarkCount", "Bookmarks", onBookmarks, Modifier.weight(1f))
                MenuAction("↶ $historyCount", "History", onHistory, Modifier.weight(1f))
                MenuAction("CLEAR", "TV browser data", onClearData, Modifier.weight(1f), destructive = true)
            }

            Text(text = "WORKSPACE & PRIVACY", style = ReceiverType.Label, color = ReceiverColors.Muted)
            Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small)) {
                MenuAction(
                    glyph = "▦ WORKSPACE",
                    label = "Open separate web pages",
                    onClick = onWorkspace,
                    modifier = Modifier.weight(1f),
                    detail = "Independent pages with pane controls",
                )
                MenuAction(
                    glyph = "NET",
                    label = "Network / VPN",
                    onClick = onNetwork,
                    modifier = Modifier.weight(1f),
                    detail = "One optional VPN for this TV profile",
                )
            }
        }
    }
}

@Composable
private fun MenuAction(
    glyph: String,
    label: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    destructive: Boolean = false,
    detail: String? = null,
) {
    Column(
        modifier = modifier
            .defaultMinSize(minWidth = 100.dp, minHeight = 72.dp)
            .background(ReceiverColors.Raised, ReceiverShapes.Small)
            .tvClickable(shape = ReceiverShapes.Small, onClick = onClick)
            .semantics {
                contentDescription = listOfNotNull(label, glyph, detail).joinToString(", ")
            }
            .padding(horizontal = ReceiverSpace.Compact, vertical = ReceiverSpace.Small),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(
            text = glyph,
            style = ReceiverType.BodyStrong,
            color = if (destructive) ReceiverColors.Live else ReceiverColors.Text,
        )
        Text(text = label, style = ReceiverType.Label, color = ReceiverColors.Muted)
        detail?.let {
            Text(text = it, style = ReceiverType.Caption, color = ReceiverColors.Muted)
        }
    }
}
