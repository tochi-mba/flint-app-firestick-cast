package com.rextechnologies.flint.receiver.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
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
import androidx.compose.ui.text.style.TextOverflow
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.browser.BrowserLibraryEntry
import com.rextechnologies.flint.receiver.browser.BrowserProfileSource
import com.rextechnologies.flint.receiver.browser.BrowserProfilesUiState

@Composable
internal fun ReceiverBrowserLibrarySheet(
    title: String,
    entries: List<BrowserLibraryEntry>,
    onOpen: (BrowserLibraryEntry) -> Unit,
    onRemove: ((BrowserLibraryEntry) -> Unit)? = null,
) {
    val firstFocus = remember { FocusRequester() }
    LaunchedEffect(entries) { if (entries.isNotEmpty()) firstFocus.requestFocus() }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(ReceiverColors.Scrim)
            .testTag(ReceiverTags.BROWSER_LIBRARY),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize(ReceiverOverscan.CONTENT_FRACTION)
                .background(ReceiverColors.Panel, ReceiverShapes.Large)
                .border(ReceiverSpace.Hairline, ReceiverColors.Line, ReceiverShapes.Large)
                .padding(ReceiverSpace.Large),
            verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Medium),
        ) {
            Text(text = title.uppercase(), style = ReceiverType.Label, color = ReceiverColors.Muted)
            if (entries.isEmpty()) {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text(
                        text = if (title.equals("Bookmarks", true)) {
                            "Pages you save will appear here."
                        } else {
                            "Pages you visit will appear here."
                        },
                        style = ReceiverType.Body,
                        color = ReceiverColors.Muted,
                    )
                }
            } else {
                LazyColumn(verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Small)) {
                    items(entries, key = { "${it.url}:${it.lastVisitedMs}" }) { entry ->
                        LibraryRow(
                            entry = entry,
                            onOpen = { onOpen(entry) },
                            onRemove = onRemove?.let { remove -> { remove(entry) } },
                            modifier = if (entry == entries.first()) {
                                Modifier.focusRequester(firstFocus)
                            } else {
                                Modifier
                            },
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun LibraryRow(
    entry: BrowserLibraryEntry,
    onOpen: () -> Unit,
    onRemove: (() -> Unit)?,
    modifier: Modifier = Modifier,
) {
    Row(
        modifier = modifier
            .fillMaxWidth()
            .background(ReceiverColors.Raised, ReceiverShapes.Small)
            .tvClickable(shape = ReceiverShapes.Small, onClick = onOpen)
            .semantics { contentDescription = "Open ${entry.title}, ${entry.url}" }
            .padding(ReceiverSpace.Compact),
        horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Compact),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(text = entry.title.take(1).uppercase(), style = ReceiverType.Title, color = ReceiverColors.Signal)
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = entry.title,
                style = ReceiverType.BodyStrong,
                color = ReceiverColors.Text,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = entry.url,
                style = ReceiverType.Caption,
                color = ReceiverColors.Muted,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        if (onRemove != null) {
            Box(
                modifier = Modifier
                    .tvClickable(shape = ReceiverShapes.Small, onClick = onRemove)
                    .semantics { contentDescription = "Remove ${entry.title}" }
                    .padding(ReceiverSpace.Compact),
            ) {
                Text(text = "REMOVE", style = ReceiverType.Label, color = ReceiverColors.Live)
            }
        }
    }
}

@Composable
internal fun ReceiverBrowserClearDataPrompt(
    profiles: BrowserProfilesUiState,
    onCancel: () -> Unit,
    onClear: () -> Unit,
) {
    val cancelFocus = remember { FocusRequester() }
    LaunchedEffect(Unit) { cancelFocus.requestFocus() }
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(ReceiverColors.Scrim)
            .testTag(ReceiverTags.BROWSER_CLEAR_DATA),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier
                .background(ReceiverColors.Raised, ReceiverShapes.Large)
                .padding(ReceiverSpace.XLarge),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Medium),
        ) {
            Text(text = "Clear browser data?", style = ReceiverType.Title, color = ReceiverColors.Text)
            Text(
                text = if (profiles.activeSource == BrowserProfileSource.TV) {
                    "This clears shared TV site sign-ins, cookies, cache and form data, plus ${profiles.activeName}'s bookmarks and history. Other TV profile libraries stay."
                } else {
                    "This clears site sign-ins, cookies, cache and form data from this TV. ${profiles.activeName}'s bookmarks and history stay on that device."
                },
                style = ReceiverType.Body,
                color = ReceiverColors.Muted,
            )
            Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Compact)) {
                TvActionButton("Keep data", true, onCancel, Modifier.focusRequester(cancelFocus))
                TvActionButton("Clear data", true, onClear)
            }
        }
    }
}
