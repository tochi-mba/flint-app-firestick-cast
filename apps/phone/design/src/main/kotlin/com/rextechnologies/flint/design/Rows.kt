package com.rextechnologies.flint.design

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

/**
 * One row of a diagnostics stack.
 *
 * The label takes 42% and the value 58%, which is the split the desktop uses: enough for a label to
 * stay on one line and enough for an address or a codec list not to wrap at every value. The hairline
 * belongs to the row above it, so the last row in a stack does not draw a line into empty space.
 */
@Composable
fun DiagnosticRow(
    label: String,
    value: String,
    modifier: Modifier = Modifier,
    isLast: Boolean = false,
    valueTone: Tone? = null,
) {
    Column(modifier = modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 9.dp),
            verticalAlignment = Alignment.Top,
        ) {
            FlintText(
                text = label,
                style = FlintType.BodySmall,
                modifier = Modifier.weight(0.42f),
            )
            FlintText(
                text = value,
                style = FlintType.BodyMedium.copy(
                    color = valueTone?.color ?: FlintColors.Text,
                ),
                modifier = Modifier.weight(0.58f),
            )
        }
        if (!isLast) {
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .height(FlintSpace.Hairline)
                    .background(FlintColors.Line),
            )
        }
    }
}

/** A telemetry value. Tabular figures, so the row does not jitter as digits change. */
@Composable
fun Readout(value: String, modifier: Modifier = Modifier, tone: Tone? = null) {
    FlintText(
        text = value,
        modifier = modifier,
        style = FlintType.Readout.copy(color = tone?.color ?: FlintColors.Text),
        maxLines = 1,
    )
}
