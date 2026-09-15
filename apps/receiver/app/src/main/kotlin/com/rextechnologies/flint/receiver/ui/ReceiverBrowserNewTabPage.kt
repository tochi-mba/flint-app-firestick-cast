package com.rextechnologies.flint.receiver.ui

import androidx.compose.foundation.background
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

/** A deliberate empty-tab surface; a blank WebView must never look like a failed load. */
@Composable
internal fun ReceiverBrowserNewTabPage(
    onSearch: () -> Unit,
    /** False while the omnibar owns the remote so this page cannot steal Select. */
    claimFocus: Boolean = true,
) {
    val searchFocus = remember { FocusRequester() }
    LaunchedEffect(claimFocus) {
        if (claimFocus) {
            runCatching { searchFocus.requestFocus() }
        }
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(ReceiverColors.Ink)
            .testTag(ReceiverTags.BROWSER_NEW_TAB),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier.fillMaxSize(ReceiverOverscan.CONTENT_FRACTION),
            verticalArrangement = Arrangement.Center,
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Text(text = "FLINT WEB", style = ReceiverType.Label, color = ReceiverColors.Signal)
            Text(
                text = "Where do you want to go?",
                style = ReceiverType.Display,
                color = ReceiverColors.Text,
                modifier = Modifier.padding(top = ReceiverSpace.Small, bottom = ReceiverSpace.Large),
            )
            Row(
                modifier = Modifier
                    .fillMaxWidth(0.68f)
                    .defaultMinSize(minHeight = 68.dp)
                    .background(ReceiverColors.Raised, ReceiverShapes.Pill)
                    .focusRequester(searchFocus)
                    .tvClickable(shape = ReceiverShapes.Pill, onClick = onSearch)
                    .semantics { contentDescription = "Search or enter an address" }
                    .padding(horizontal = ReceiverSpace.Large),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Compact),
            ) {
                Text(text = "⌕", style = ReceiverType.Title, color = ReceiverColors.Signal)
                Text(
                    text = "Search or enter an address",
                    style = ReceiverType.Body,
                    color = ReceiverColors.Muted,
                )
            }
            Text(
                text = "Press SEARCH anytime · Hold SELECT on the page to switch pointer mode · Hold BACK for tabs",
                style = ReceiverType.Caption,
                color = ReceiverColors.Muted,
                modifier = Modifier.padding(top = ReceiverSpace.Large),
            )
        }
    }
}
