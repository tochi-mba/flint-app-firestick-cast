package com.rextechnologies.flint.mobile.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.window.Dialog
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
fun PairingSheet(error: String?, onSubmit: (String) -> Unit, onDismiss: () -> Unit) {
    var code by remember { mutableStateOf("") }

    Dialog(onDismissRequest = onDismiss) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(FlintColors.Scrim)
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
                    placeholder = "000 000",
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                    textStyle = FlintType.Readout.copy(fontFamily = FontFamily.Monospace),
                )

                error?.let {
                    FlintText(text = it, style = FlintType.BodySmall.copy(color = FlintColors.Live))
                }

                Row(horizontalArrangement = Arrangement.spacedBy(FlintSpace.Small)) {
                    SignalButton(
                        text = "Pair",
                        onClick = { onSubmit(code) },
                        enabled = PairingCopy.isWellFormed(code),
                    )
                    OutlineAction(text = "Cancel", onClick = onDismiss)
                }
            }
        }
    }
}
