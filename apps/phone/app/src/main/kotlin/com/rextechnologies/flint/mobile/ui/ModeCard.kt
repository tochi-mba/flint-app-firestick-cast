package com.rextechnologies.flint.mobile.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import com.rextechnologies.flint.castcore.capability.ModePresentation
import com.rextechnologies.flint.castcore.capability.ModeVerdict
import com.rextechnologies.flint.design.AdvisoryBlock
import com.rextechnologies.flint.design.FlintColors
import com.rextechnologies.flint.design.FlintSpace
import com.rextechnologies.flint.design.FlintText
import com.rextechnologies.flint.design.FlintType
import com.rextechnologies.flint.design.InfoCard
import com.rextechnologies.flint.design.Pill

/**
 * One mode's card.
 *
 * The presentation comes from `:castcore`, already decided. Nothing here chooses a tone, a word or
 * whether an advisory block appears — the point of putting that in a pure module was that the rule
 * "an impossible mode is never painted like an available one" could be unit-tested, and this
 * composable exists only to draw the answer.
 */
@Composable
fun ModeCard(verdict: ModeVerdict, modifier: Modifier = Modifier) {
    val presentation = ModePresentation.of(verdict)
    InfoCard(modifier = modifier, borderTone = presentation.tone.toDesignTone()) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            FlintText(
                text = presentation.title,
                style = FlintType.TitleMedium,
                modifier = Modifier.weight(1f),
            )
            Spacer(Modifier.width(FlintSpace.Small))
            Pill(text = presentation.statusWord, tone = presentation.tone.toDesignTone())
        }

        FlintText(
            text = presentation.reason,
            style = FlintType.BodyMedium.copy(color = FlintColors.Muted),
        )

        presentation.advisoryHeading?.let { heading ->
            AdvisoryBlock(heading = heading, body = presentation.advisoryBody)
        }
    }
}

/** A stack of cards, twelve apart, matching the desktop's page grid. */
@Composable
fun ModeCards(verdicts: List<ModeVerdict>, modifier: Modifier = Modifier) {
    androidx.compose.foundation.layout.Column(
        modifier = modifier.fillMaxWidth(),
        verticalArrangement = Arrangement.spacedBy(FlintSpace.CardSpacing),
    ) {
        verdicts.forEach { ModeCard(it) }
    }
}
