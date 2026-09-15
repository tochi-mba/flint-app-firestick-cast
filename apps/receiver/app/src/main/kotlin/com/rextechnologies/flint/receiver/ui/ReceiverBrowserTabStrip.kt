package com.rextechnologies.flint.receiver.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.browser.BrowserTab

/**
 * The row of open pages.
 *
 * Titles are short here because a television reads at a glance rather than a scan, and because a
 * row that grows with its content pushes the new-tab button off the safe area. Each chip carries
 * its own close affordance so shutting a page never means opening a menu first.
 */
@Composable
internal fun ReceiverBrowserTabStrip(
    tabs: List<BrowserTab>,
    activeId: Long,
    onSelect: (Long) -> Unit,
    onClose: (Long) -> Unit,
    onNew: () -> Unit,
) {
    if (tabs.size <= 1) {
        // One page is not a set of tabs, and a strip showing a single chip is chrome that earns
        // nothing while costing a row of the safe area.
        return
    }

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .testTag(ReceiverTags.BROWSER_TAB_STRIP),
        horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        tabs.forEach { tab ->
            TabChip(
                tab = tab,
                active = tab.id == activeId,
                onSelect = { onSelect(tab.id) },
                onClose = { onClose(tab.id) },
                modifier = Modifier.weight(1f, fill = false),
            )
        }

        Box(
            modifier = Modifier
                .defaultMinSize(minWidth = 48.dp, minHeight = 44.dp)
                .background(ReceiverColors.Panel, ReceiverShapes.Small)
                .tvClickable(shape = ReceiverShapes.Small, onClick = onNew)
                .semantics { contentDescription = "New tab" },
            contentAlignment = Alignment.Center,
        ) {
            Text(text = "+", style = ReceiverType.BodyStrong, color = ReceiverColors.Text)
        }
    }
}

@Composable
private fun TabChip(
    tab: BrowserTab,
    active: Boolean,
    onSelect: () -> Unit,
    onClose: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Row(
        modifier = modifier
            .widthIn(min = 120.dp, max = 260.dp)
            .defaultMinSize(minHeight = 44.dp)
            .background(
                if (active) ReceiverColors.Raised else ReceiverColors.Panel,
                ReceiverShapes.Small,
            )
            .border(
                width = if (active) 2.dp else ReceiverSpace.Hairline,
                color = if (active) ReceiverColors.Signal else ReceiverColors.Line,
                shape = ReceiverShapes.Small,
            )
            .tvClickable(shape = ReceiverShapes.Small, onClick = onSelect)
            .padding(horizontal = ReceiverSpace.Compact, vertical = ReceiverSpace.Tiny)
            .semantics {
                selected = active
                contentDescription = "${tab.displayLabel()}${if (tab.live) "" else ", suspended"}"
            },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small),
    ) {
        // A letter tile until favicons arrive. Better than a blank square, and it survives a site
        // that never sends an icon at all.
        Box(
            modifier = Modifier
                .defaultMinSize(minWidth = 24.dp, minHeight = 24.dp)
                .background(
                    if (tab.live) ReceiverColors.Signal.copy(alpha = 0.18f) else ReceiverColors.Line,
                    ReceiverShapes.Small,
                ),
            contentAlignment = Alignment.Center,
        ) {
            Text(
                text = tab.initial(),
                style = ReceiverType.Label,
                color = if (tab.live) ReceiverColors.Signal else ReceiverColors.Muted,
            )
        }

        Text(
            text = tab.displayLabel(),
            style = ReceiverType.Caption,
            color = if (active) ReceiverColors.Text else ReceiverColors.Muted,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )

        Box(
            modifier = Modifier
                .defaultMinSize(minWidth = 28.dp, minHeight = 28.dp)
                .tvClickable(shape = ReceiverShapes.Small, onClick = onClose)
                .semantics { contentDescription = "Close ${tab.displayLabel()}" },
            contentAlignment = Alignment.Center,
        ) {
            Text(text = "✕", style = ReceiverType.Caption, color = ReceiverColors.Muted)
        }
    }
}

/** A title if the page has one, otherwise the host, otherwise a plain new tab. */
internal fun BrowserTab.displayLabel(): String = when {
    title.isNotBlank() -> title
    url.isNotBlank() -> url.substringAfter("://", url).substringBefore('/')
    else -> "New tab"
}

internal fun BrowserTab.initial(): String =
    displayLabel().firstOrNull { it.isLetterOrDigit() }?.uppercase() ?: "•"
