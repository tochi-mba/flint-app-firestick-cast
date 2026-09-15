package com.rextechnologies.flint.receiver.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.browser.BrowserTab

/**
 * Every open page at once.
 *
 * Reached by holding Back, which is the fastest route between two sites a remote has. Cards rather
 * than a list because at ten feet a grid is read by position, and position is what people remember
 * about their own tabs.
 *
 * Suspended tabs say so. A tab whose renderer has been released will take a moment to come back,
 * and a card that hides that reads as a slow browser rather than a deliberate memory budget.
 */
@Composable
internal fun ReceiverBrowserTabSwitcher(
    tabs: List<BrowserTab>,
    activeId: Long,
    onSelect: (Long) -> Unit,
    onClose: (Long) -> Unit,
    onNew: () -> Unit,
) {
    // The sheet takes focus itself once it is laid out. Nothing outside it can do this: the surface
    // deliberately stops reaching in when an overlay is up, and without a request here the cards
    // render with focus still behind them.
    val firstCard = remember { FocusRequester() }
    LaunchedEffect(Unit) {
        withFrameNanos { }
        runCatching { firstCard.requestFocus() }
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(ReceiverColors.Scrim)
            .testTag(ReceiverTags.BROWSER_TAB_SWITCHER),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize(ReceiverOverscan.CONTENT_FRACTION)
                .padding(ReceiverSpace.Large),
            verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Medium),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = "OPEN PAGES · ${tabs.size}",
                    style = ReceiverType.Label,
                    color = ReceiverColors.Muted,
                )
                Text(
                    text = "SELECT A PAGE, OR ADD ONE",
                    style = ReceiverType.Label,
                    color = ReceiverColors.Muted,
                )
            }

            // Three across is what fits legibly on a 1080p panel with overscan taken off; wrapping
            // by hand rather than a lazy grid because eight cards never needs virtualising.
            // New Tab is a card in the grid, not a button in the header.
            //
            // In the header it sat to the right of a label with no card beneath it, and Compose's
            // two-dimensional focus search needs overlap on the perpendicular axis to move between
            // controls — so from the first card, Up found nothing and the button was simply
            // unreachable with a remote. Found on a Fire TV Stick 4K. As the last cell it is always
            // one step from the last page, which is also where a browser puts it.
            val slots: List<BrowserTab?> = tabs + listOf(null)
            slots.chunked(COLUMNS).forEach { row ->
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Medium),
                ) {
                    row.forEach { tab ->
                        if (tab == null) {
                            NewTabCard(onNew = onNew, modifier = Modifier.weight(1f))
                        } else {
                            TabCard(
                                tab = tab,
                                active = tab.id == activeId,
                                onSelect = { onSelect(tab.id) },
                                onClose = { onClose(tab.id) },
                                modifier = Modifier
                                    .weight(1f)
                                    .then(
                                        // Focus opens on the page being looked at, which is where
                                        // someone reaching for another tab starts from.
                                        if (tab.id == activeId) {
                                            Modifier.focusRequester(firstCard)
                                        } else {
                                            Modifier
                                        },
                                    ),
                            )
                        }
                    }
                    repeat(COLUMNS - row.size) {
                        Box(modifier = Modifier.weight(1f))
                    }
                }
            }
        }
    }
}

@Composable
private fun TabCard(
    tab: BrowserTab,
    active: Boolean,
    onSelect: () -> Unit,
    onClose: () -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier
            .background(ReceiverColors.Panel, ReceiverShapes.Medium)
            .border(
                width = if (active) 3.dp else ReceiverSpace.Hairline,
                color = if (active) ReceiverColors.Signal else ReceiverColors.Line,
                shape = ReceiverShapes.Medium,
            )
            .tvClickable(shape = ReceiverShapes.Medium, onClick = onSelect)
            .padding(ReceiverSpace.Compact)
            .semantics {
                selected = active
                contentDescription = buildString {
                    append(tab.displayLabel())
                    if (active) append(", showing now")
                    if (!tab.live) append(", suspended")
                }
            },
        verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Small),
    ) {
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .aspectRatio(16f / 9f)
                .background(ReceiverColors.Ink, ReceiverShapes.Small),
            contentAlignment = Alignment.Center,
        ) {
            // A thumbnail would mean capturing every tab; the letter is honest and free, and the
            // card is identified by its title underneath in any case.
            Text(
                text = tab.initial(),
                style = ReceiverType.Display,
                color = if (tab.live) ReceiverColors.Signal else ReceiverColors.Line,
            )
        }

        Text(
            text = tab.displayLabel(),
            style = ReceiverType.Caption,
            color = ReceiverColors.Text,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = if (tab.live) "Ready" else "Suspended",
                style = ReceiverType.Label,
                color = if (tab.live) ReceiverColors.Success else ReceiverColors.Muted,
            )
            Box(
                modifier = Modifier
                    .defaultMinSize(minWidth = 36.dp, minHeight = 32.dp)
                    .tvClickable(shape = ReceiverShapes.Small, onClick = onClose)
                    .semantics { contentDescription = "Close ${tab.displayLabel()}" },
                contentAlignment = Alignment.Center,
            ) {
                Text(text = "✕", style = ReceiverType.Caption, color = ReceiverColors.Muted)
            }
        }
    }
}

private const val COLUMNS = 3

/** The add-a-page cell, shaped like a card so it sits in the grid's focus order. */
@Composable
private fun NewTabCard(onNew: () -> Unit, modifier: Modifier = Modifier) {
    Column(
        modifier = modifier
            .background(ReceiverColors.Panel, ReceiverShapes.Medium)
            .border(ReceiverSpace.Hairline, ReceiverColors.Line, ReceiverShapes.Medium)
            .tvClickable(shape = ReceiverShapes.Medium, onClick = onNew)
            .padding(ReceiverSpace.Compact)
            .testTag(ReceiverTags.BROWSER_NEW_TAB)
            .semantics { contentDescription = "New tab" },
        verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Small),
    ) {
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .aspectRatio(16f / 9f)
                .background(ReceiverColors.Ink, ReceiverShapes.Small),
            contentAlignment = Alignment.Center,
        ) {
            Text(text = "+", style = ReceiverType.Display, color = ReceiverColors.Signal)
        }
        Text(text = "New tab", style = ReceiverType.Caption, color = ReceiverColors.Text)
        Text(text = "Add a page", style = ReceiverType.Label, color = ReceiverColors.Muted)
    }
}
