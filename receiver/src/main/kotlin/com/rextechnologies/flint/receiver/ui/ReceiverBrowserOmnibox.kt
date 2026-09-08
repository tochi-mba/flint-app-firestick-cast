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
import androidx.compose.foundation.layout.width
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
import com.rextechnologies.flint.receiver.browser.BrowserKeyboard
import com.rextechnologies.flint.receiver.browser.BrowserKeyboardState

/** One row under the address field: a search, a bookmark, or somewhere already visited. */
data class OmniboxSuggestion(
    val glyph: String,
    val primary: String,
    val secondary: String? = null,
)

/**
 * Where an address or a search gets typed.
 *
 * A full-screen sheet rather than an inline field, because at ten feet an address bar with a
 * keyboard under it needs the whole screen to be legible, and because taking over the screen makes
 * it obvious that the D-pad now belongs to the keyboard rather than to the page.
 *
 * The first suggestion is always the search, so the commonest thing anyone does here — look
 * something up — is one press away rather than a full URL of typing.
 */
@Composable
internal fun ReceiverBrowserOmnibox(
    state: BrowserKeyboardState,
    keyboard: BrowserKeyboard,
    suggestions: List<OmniboxSuggestion>,
    selectedSuggestion: Int?,
    desktopTypingAvailable: Boolean,
    heading: String = "SEARCH OR ENTER ADDRESS",
    fieldDescription: String = "Address or search",
    placeholder: String = "Type an address, or a few words to search for",
    submitLabel: String = "go",
    sheetTag: String = ReceiverTags.BROWSER_OMNIBOX,
) {
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(ReceiverColors.Scrim)
            .testTag(sheetTag),
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
            Text(
                text = heading,
                style = ReceiverType.Label,
                color = ReceiverColors.Muted,
            )

            AddressField(
                text = state.text,
                fieldDescription = fieldDescription,
                placeholder = placeholder,
            )

            if (suggestions.isNotEmpty()) {
                Column(verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Tiny)) {
                    suggestions.forEachIndexed { index, suggestion ->
                        SuggestionRow(
                            suggestion = suggestion,
                            selected = selectedSuggestion == index,
                        )
                    }
                }
            }

            ReceiverBrowserKeyboard(
                state = state,
                keyboard = keyboard,
                submitLabel = submitLabel,
                modifier = Modifier.padding(top = ReceiverSpace.Tiny),
            )

            // The desktop is genuinely faster, and saying so is worth more than pretending the
            // remote is pleasant. It is an offer, never a requirement — everything here works
            // without a PC in the room.
            if (desktopTypingAvailable) {
                Text(
                    text = "Flint on your PC can type here",
                    style = ReceiverType.Caption,
                    color = ReceiverColors.Signal,
                    modifier = Modifier.testTag(ReceiverTags.BROWSER_OMNIBOX_DESKTOP_HINT),
                )
            }
        }
    }
}

@Composable
private fun AddressField(text: String, fieldDescription: String, placeholder: String) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .defaultMinSize(minHeight = 62.dp)
            .background(ReceiverColors.Ink, ReceiverShapes.Medium)
            .border(2.dp, ReceiverColors.Signal, ReceiverShapes.Medium)
            .padding(horizontal = ReceiverSpace.Medium, vertical = ReceiverSpace.Compact)
            .testTag(ReceiverTags.BROWSER_OMNIBOX_FIELD)
            .semantics { contentDescription = "$fieldDescription: $text" },
        contentAlignment = Alignment.CenterStart,
    ) {
        if (text.isEmpty()) {
            Text(
                text = placeholder,
                style = ReceiverType.Body,
                color = ReceiverColors.Muted,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        } else {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = text,
                    style = ReceiverType.Title,
                    color = ReceiverColors.Text,
                    maxLines = 1,
                    overflow = TextOverflow.StartEllipsis,
                    modifier = Modifier.weight(1f, fill = false),
                )
                // A drawn caret rather than a blinking one: an animation running behind a keyboard
                // is a recomposition every frame for no information.
                Box(
                    modifier = Modifier
                        .padding(start = 2.dp)
                        .width(3.dp)
                        .defaultMinSize(minHeight = 26.dp)
                        .background(ReceiverColors.Signal),
                )
            }
        }
    }
}

@Composable
private fun SuggestionRow(suggestion: OmniboxSuggestion, selected: Boolean) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(
                if (selected) ReceiverColors.Raised else ReceiverColors.Panel,
                ReceiverShapes.Small,
            )
            .border(
                width = if (selected) 2.dp else ReceiverSpace.Hairline,
                color = if (selected) ReceiverColors.Signal else ReceiverColors.Line,
                shape = ReceiverShapes.Small,
            )
            .padding(horizontal = ReceiverSpace.Medium, vertical = ReceiverSpace.Compact)
            .semantics {
                this.selected = selected
                contentDescription = listOfNotNull(suggestion.primary, suggestion.secondary)
                    .joinToString(", ")
            },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Compact),
    ) {
        Text(text = suggestion.glyph, style = ReceiverType.Body, color = ReceiverColors.Signal)
        Text(
            text = suggestion.primary,
            style = ReceiverType.Body,
            color = ReceiverColors.Text,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        suggestion.secondary?.let {
            Text(
                text = it,
                style = ReceiverType.Caption,
                color = ReceiverColors.Muted,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}
