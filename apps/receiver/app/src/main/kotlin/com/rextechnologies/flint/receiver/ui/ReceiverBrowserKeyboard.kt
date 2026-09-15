package com.rextechnologies.flint.receiver.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.browser.BrowserKey
import com.rextechnologies.flint.receiver.browser.BrowserKeyCursor
import com.rextechnologies.flint.receiver.browser.BrowserKeyboard
import com.rextechnologies.flint.receiver.browser.BrowserKeyboardState

/**
 * The keyboard someone types an address on with a remote.
 *
 * Selection is drawn from the model's cursor rather than from Compose focus. A grid this dense
 * confuses two-dimensional focus search — a wide `space` key sitting under three narrow ones has no
 * single correct answer — and the model already guarantees wrap-around and that every cell exists.
 * Nothing here is focusable, so the sheet keeps the key events and the selection can never disagree
 * with what a press will do.
 */
@Composable
internal fun ReceiverBrowserKeyboard(
    state: BrowserKeyboardState,
    keyboard: BrowserKeyboard,
    submitLabel: String = "go",
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier.testTag(ReceiverTags.BROWSER_KEYBOARD),
        verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Small),
    ) {
        keyboard.rows(state.page).forEachIndexed { rowIndex, row ->
            Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small)) {
                row.forEachIndexed { columnIndex, key ->
                    KeyCap(
                        label = if (key is BrowserKey.Submit) submitLabel else keyboard.label(key, state.shifted),
                        selected = state.cursor == BrowserKeyCursor(rowIndex, columnIndex),
                        emphasis = key is BrowserKey.Submit,
                        wide = key is BrowserKey.Space,
                        armed = key is BrowserKey.Shift && state.shifted,
                    )
                }
            }
        }
    }
}

@Composable
private fun KeyCap(
    label: String,
    selected: Boolean,
    emphasis: Boolean,
    wide: Boolean,
    armed: Boolean,
) {
    val background = when {
        selected && emphasis -> ReceiverColors.Signal
        selected -> ReceiverColors.Raised
        armed -> ReceiverColors.Signal.copy(alpha = 0.18f)
        else -> ReceiverColors.Panel
    }
    val foreground = when {
        selected && emphasis -> ReceiverColors.Ink
        selected -> ReceiverColors.Text
        else -> ReceiverColors.Muted
    }

    Box(
        modifier = Modifier
            .then(if (wide) Modifier.width(148.dp) else Modifier.defaultMinSize(minWidth = 58.dp))
            .defaultMinSize(minHeight = 52.dp)
            .background(background, ReceiverShapes.Small)
            .border(
                width = if (selected) 2.dp else ReceiverSpace.Hairline,
                color = if (selected) ReceiverColors.Signal else ReceiverColors.Line,
                shape = ReceiverShapes.Small,
            )
            .padding(horizontal = ReceiverSpace.Compact, vertical = ReceiverSpace.Small)
            .semantics {
                this.selected = selected
                contentDescription = label
            },
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = label,
            style = if (label.length == 1) ReceiverType.BodyStrong else ReceiverType.Caption,
            color = foreground,
        )
    }
}
