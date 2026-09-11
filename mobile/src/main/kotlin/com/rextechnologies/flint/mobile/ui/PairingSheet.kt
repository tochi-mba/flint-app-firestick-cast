package com.rextechnologies.flint.mobile.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.ime
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.OffsetMapping
import androidx.compose.ui.text.input.TransformedText
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.rextechnologies.flint.castcore.copy.PairingCopy
import com.rextechnologies.flint.design.FlintColors
import com.rextechnologies.flint.design.FlintSpace
import com.rextechnologies.flint.design.FlintText
import com.rextechnologies.flint.design.FlintType
import com.rextechnologies.flint.design.InfoCard
import com.rextechnologies.flint.design.OutlineAction
import com.rextechnologies.flint.design.SectionLabel
import com.rextechnologies.flint.design.SignalButton
import com.rextechnologies.flint.design.TextEntry
import com.rextechnologies.flint.design.Tone

/**
 * The six-digit code from the television.
 *
 * The entry is monospaced and grouped in threes, and it refuses anything that is not a digit rather
 * than accepting it and complaining afterwards. What it does not do is say whether a well-formed
 * code is the right one: only the television can answer that, and guessing here would be a second
 * opinion about something this end cannot know.
 */
@Composable
fun PairingSheet(
    error: String?,
    connecting: Boolean,
    onSubmit: (String) -> Unit,
    onDismiss: () -> Unit,
) {
    var code by remember { mutableStateOf("") }
    val field = remember { FocusRequester() }

    // The keyboard comes up on its own. A dialog whose only purpose is six digits, that then waits
    // for somebody to tap the field before offering a keypad, has made them do the work twice.
    LaunchedEffect(Unit) { runCatching { field.requestFocus() } }

    Dialog(
        onDismissRequest = onDismiss,
        // Without this the dialog takes the platform's default width, which leaves the scrim short
        // of the screen edges and squeezes the card into the middle of a dimmed rectangle.
        properties = DialogProperties(usePlatformDefaultWidth = false),
    ) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(FlintColors.Scrim)
                // The keyboard this dialog opens itself would otherwise cover the field it opened
                // for, and the card scrolls rather than being squashed on a short screen.
                .windowInsetsPadding(WindowInsets.ime)
                .verticalScroll(rememberScrollState())
                .padding(FlintSpace.Large),
            contentAlignment = Alignment.Center,
        ) {
            InfoCard(borderTone = if (error != null) Tone.Live else Tone.Line) {
                SectionLabel(PairingCopy.EYEBROW)
                FlintText(text = PairingCopy.TITLE, style = FlintType.TitleLarge)
                FlintText(text = PairingCopy.BODY, style = FlintType.BodyMedium)

                // The value is the digits, not the grouped display of them: handing a transformed
                // string back as the field's own value puts the caret in the wrong place the moment
                // somebody edits the middle of it.
                TextEntry(
                    value = code,
                    onValueChange = { entered ->
                        code = entered.filter { it.isDigit() }.take(PairingCopy.DIGITS)
                    },
                    modifier = Modifier.focusRequester(field),
                    placeholder = "000 000",
                    enabled = !connecting,
                    keyboardOptions = KeyboardOptions(
                        keyboardType = KeyboardType.NumberPassword,
                        imeAction = ImeAction.Done,
                    ),
                    keyboardActions = KeyboardActions(
                        onDone = { if (PairingCopy.isWellFormed(code)) onSubmit(code) },
                    ),
                    visualTransformation = GroupedDigits,
                    contentDescription = PairingCopy.TITLE,
                    textStyle = FlintType.Readout.copy(fontFamily = FontFamily.Monospace),
                )

                error?.let {
                    FlintText(text = it, style = FlintType.BodySmall.copy(color = FlintColors.Live))
                }
                if (connecting) {
                    FlintText(
                        text = PairingCopy.CONNECTING,
                        style = FlintType.BodySmall.copy(color = FlintColors.Muted),
                    )
                }

                Row(horizontalArrangement = Arrangement.spacedBy(FlintSpace.Small)) {
                    SignalButton(
                        text = "Pair",
                        onClick = { onSubmit(code) },
                        enabled = PairingCopy.isWellFormed(code) && !connecting,
                    )
                    OutlineAction(text = "Cancel", onClick = onDismiss)
                }
            }
        }
    }
}

/**
 * Six digits shown as two groups of three.
 *
 * The KDoc above promised this and nothing applied it, so the field showed an unbroken run of six
 * that is measurably harder to check against a television across a room. The offset mapping is what
 * keeps the caret where the person put it: one space appears after the third digit, so every
 * position past it shifts by exactly one.
 */
private object GroupedDigits : VisualTransformation {
    override fun filter(text: AnnotatedString): TransformedText {
        val digits = text.text.take(PairingCopy.DIGITS)
        val grouped = PairingCopy.grouped(digits)
        val split = PairingCopy.DIGITS / 2
        return TransformedText(
            AnnotatedString(grouped),
            object : OffsetMapping {
                override fun originalToTransformed(offset: Int): Int =
                    if (offset <= split) offset else offset + 1

                override fun transformedToOriginal(offset: Int): Int =
                    if (offset <= split) offset else (offset - 1).coerceAtMost(digits.length)
            },
        )
    }
}
