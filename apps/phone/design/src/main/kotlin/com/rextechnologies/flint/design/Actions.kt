package com.rextechnologies.flint.design

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.input.VisualTransformation
import java.util.Locale

/**
 * The primary action.
 *
 * Filled in Signal, because this is the one place the accent is allowed to carry weight — and there
 * is at most one of these on a screen for exactly that reason.
 */
@Composable
fun SignalButton(
    text: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    Box(
        // A minimum rather than a fixed height. Both were set before, which made the minimum dead
        // and clipped the label the moment a large font scale pushed it past 48dp — the one case
        // the minimum exists for.
        modifier = modifier
            .defaultMinSize(minHeight = FlintSpace.TouchTarget)
            .flintClickable(enabled = enabled, onClick = onClick)
            .background(
                if (enabled) FlintColors.Signal else FlintColors.Line,
                FlintShapes.Pill,
            )
            .padding(horizontal = FlintSpace.Large, vertical = FlintSpace.Compact),
        contentAlignment = Alignment.Center,
    ) {
        FlintText(
            text = text.uppercase(Locale.ROOT),
            style = FlintType.LabelSmall.copy(
                color = if (enabled) FlintColors.Ink else FlintColors.Muted,
            ),
            maxLines = 1,
        )
    }
}

/** The secondary action. Outlined, the same height, never competing with the primary one. */
@Composable
fun OutlineAction(
    text: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    tone: Tone = Tone.Neutral,
) {
    Box(
        modifier = modifier
            .defaultMinSize(minHeight = FlintSpace.TouchTarget)
            .flintClickable(enabled = enabled, onClick = onClick)
            .border(
                FlintSpace.Hairline,
                if (enabled) tone.color else FlintColors.Line,
                FlintShapes.Pill,
            )
            .padding(horizontal = FlintSpace.Large, vertical = FlintSpace.Compact),
        contentAlignment = Alignment.Center,
    ) {
        FlintText(
            text = text.uppercase(Locale.ROOT),
            style = FlintType.LabelSmall.copy(
                color = if (enabled) tone.color else FlintColors.Muted,
            ),
            maxLines = 1,
        )
    }
}

/**
 * The one text field in the system.
 *
 * `BasicTextField` rather than a Material one, for the same reason as everything else here: the app
 * takes no Material dependency, so there is no theme for a field to have to agree with. The cursor
 * and selection are painted in Signal, which is the only place in the app that colour appears
 * without meaning "ready".
 */
@Composable
fun TextEntry(
    value: String,
    onValueChange: (String) -> Unit,
    modifier: Modifier = Modifier,
    placeholder: String = "",
    enabled: Boolean = true,
    keyboardOptions: KeyboardOptions = KeyboardOptions.Default,
    keyboardActions: KeyboardActions = KeyboardActions.Default,
    /**
     * How the value is displayed, which is not how it is stored.
     *
     * A field whose own value is the transformed string puts the caret in the wrong place the moment
     * somebody edits the middle of it, so grouping belongs here and nowhere else.
     */
    visualTransformation: VisualTransformation = VisualTransformation.None,
    /** What a screen reader calls this field. A field with no label is a field with no name. */
    contentDescription: String? = null,
    textStyle: TextStyle = FlintType.BodyMedium,
) {
    Box(
        modifier = modifier
            .fillMaxWidth()
            .defaultMinSize(minHeight = FlintSpace.TouchTarget)
            .background(FlintColors.Raised, FlintShapes.Small)
            .border(FlintSpace.Hairline, FlintColors.Line, FlintShapes.Small)
            .padding(horizontal = FlintSpace.Compact, vertical = FlintSpace.Compact),
        contentAlignment = Alignment.CenterStart,
    ) {
        if (value.isEmpty() && placeholder.isNotEmpty()) {
            FlintText(text = placeholder, style = FlintType.BodySmall)
        }
        BasicTextField(
            value = value,
            onValueChange = onValueChange,
            enabled = enabled,
            singleLine = true,
            keyboardOptions = keyboardOptions,
            keyboardActions = keyboardActions,
            visualTransformation = visualTransformation,
            textStyle = LocalFlintTextStyle.current.merge(textStyle),
            cursorBrush = SolidColor(FlintColors.Signal),
            modifier = Modifier
                .fillMaxWidth()
                .then(
                    contentDescription?.let { label ->
                        Modifier.semantics { this.contentDescription = label }
                    } ?: Modifier,
                ),
        )
    }
}
