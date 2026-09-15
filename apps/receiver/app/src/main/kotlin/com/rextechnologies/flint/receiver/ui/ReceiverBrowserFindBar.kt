package com.rextechnologies.flint.receiver.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.browser.BrowserFindState

/** Persistent controls for an active find query; result count is honest while WebView is counting. */
@Composable
internal fun ReceiverBrowserFindBar(
    find: BrowserFindState,
    onPrevious: () -> Unit,
    onNext: () -> Unit,
    onClose: () -> Unit,
    modifier: Modifier = Modifier,
) {
    if (!find.active) return
    Row(
        modifier = modifier
            .background(ReceiverColors.Panel, ReceiverShapes.Pill)
            .border(ReceiverSpace.Hairline, ReceiverColors.Line, ReceiverShapes.Pill)
            .padding(horizontal = ReceiverSpace.Compact, vertical = ReceiverSpace.Small)
            .testTag(ReceiverTags.BROWSER_FIND_BAR),
        horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(text = "FIND", style = ReceiverType.Label, color = ReceiverColors.Signal)
        Text(text = find.query, style = ReceiverType.BodyStrong, color = ReceiverColors.Text)
        Text(
            text = if (!find.doneCounting) "Counting…" else "${find.currentMatch} of ${find.totalMatches}",
            style = ReceiverType.Caption,
            color = ReceiverColors.Muted,
        )
        FindButton("↑", "Previous match", onPrevious)
        FindButton("↓", "Next match", onNext)
        FindButton("×", "Close find", onClose)
    }
}

@Composable
private fun FindButton(glyph: String, description: String, onClick: () -> Unit) {
    Box(
        modifier = Modifier
            .defaultMinSize(minWidth = ReceiverSpace.TouchTarget, minHeight = ReceiverSpace.TouchTarget)
            .tvClickable(shape = ReceiverShapes.Small, onClick = onClick)
            .semantics { contentDescription = description },
        contentAlignment = Alignment.Center,
    ) {
        Text(text = glyph, style = ReceiverType.BodyStrong, color = ReceiverColors.Text)
    }
}
