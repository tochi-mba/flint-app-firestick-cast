package com.rextechnologies.flint.mobile.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import com.rextechnologies.flint.castcore.copy.OnboardingCopy
import com.rextechnologies.flint.design.FlintColors
import com.rextechnologies.flint.design.FlintSpace
import com.rextechnologies.flint.design.FlintText
import com.rextechnologies.flint.design.FlintType
import com.rextechnologies.flint.design.OutlineAction
import com.rextechnologies.flint.design.PageHeading
import com.rextechnologies.flint.design.SignalButton

/**
 * The introduction: six steps, skippable, and reachable again from Settings.
 *
 * Full-bleed ink rather than cards, so it reads as a different kind of screen from the instrument
 * behind it. It ends by running the probe, which is why the last button is worded as an action
 * rather than as "Done" — the introduction finishes on an answer instead of an empty screen.
 */
@Composable
fun OnboardingScreen(onFinish: () -> Unit, onSkip: () -> Unit) {
    var index by remember { mutableIntStateOf(0) }
    val step = OnboardingCopy.steps[index]
    val isLast = index == OnboardingCopy.steps.lastIndex

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(FlintColors.Ink)
            .windowInsetsPadding(WindowInsets.safeDrawing)
            .padding(FlintSpace.PageMargin),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                .fillMaxWidth()
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(FlintSpace.Medium),
        ) {
            PageHeading(
                eyebrow = step.eyebrow,
                headline = step.title,
                statusText = "${index + 1} of ${OnboardingCopy.steps.size}",
                statusTone = step.tone.toDesignTone(),
            )

            FlintText(text = step.body, style = FlintType.BodyMedium)

            step.displayPoints.forEach { point ->
                Row(verticalAlignment = Alignment.Top) {
                    FlintText(
                        text = point.marker,
                        style = FlintType.LabelSmall.copy(color = FlintColors.Signal),
                        modifier = Modifier.width(FlintSpace.Large),
                    )
                    FlintText(text = point.text, style = FlintType.BodyMedium)
                }
            }
        }

        Spacer(Modifier.height(FlintSpace.Medium))
        Row(horizontalArrangement = Arrangement.spacedBy(FlintSpace.Small)) {
            SignalButton(
                text = if (isLast) OnboardingCopy.FINAL_ACTION else "Next",
                onClick = { if (isLast) onFinish() else index++ },
            )
            OutlineAction(text = OnboardingCopy.SKIP_ACTION, onClick = onSkip)
        }
    }
}
