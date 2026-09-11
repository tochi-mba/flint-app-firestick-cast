package com.rextechnologies.flint.design

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.defaultMinSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.BasicText
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import java.util.Locale

/**
 * Every piece of text in the system.
 *
 * A thin wrapper over [BasicText] rather than a Material `Text`, so the app carries no Material
 * dependency and every string inherits the centred metrics the theme provides.
 */
@Composable
fun FlintText(
    text: String,
    modifier: Modifier = Modifier,
    style: TextStyle = LocalFlintTextStyle.current,
    maxLines: Int = Int.MAX_VALUE,
    overflow: TextOverflow = TextOverflow.Clip,
) {
    BasicText(
        text = text,
        modifier = modifier,
        style = LocalFlintTextStyle.current.merge(style),
        maxLines = maxLines,
        overflow = overflow,
    )
}

/**
 * A section heading.
 *
 * Upper-cased here rather than at the call site, so the rhythm of the system cannot be broken by
 * somebody typing a heading in sentence case.
 */
@Composable
fun SectionLabel(text: String, modifier: Modifier = Modifier, tone: Tone = Tone.Neutral) {
    FlintText(
        text = text.uppercase(Locale.ROOT),
        modifier = modifier.semantics { heading() },
        style = FlintType.LabelSmall.copy(color = tone.color),
    )
}

/**
 * A status pill.
 *
 * Outline only, never filled: a filled pill in Signal would out-shout the thing it is labelling, and
 * the point of the accent is that it is rare. The border and the text share one tone, so a pill can
 * never say one thing in its colour and another in its words.
 */
@Composable
fun Pill(text: String, tone: Tone, modifier: Modifier = Modifier) {
    Box(
        modifier = modifier
            .border(FlintSpace.Hairline, tone.color, FlintShapes.Pill)
            .padding(horizontal = 10.dp, vertical = 5.dp),
    ) {
        FlintText(
            text = text.uppercase(Locale.ROOT),
            style = FlintType.LabelSmall.copy(color = tone.color),
            maxLines = 1,
        )
    }
}

/**
 * A nine-dp dot.
 *
 * Decorative: it always sits beside text that says the same thing, so it is hidden from the screen
 * reader rather than announced as an unlabelled image.
 */
@Composable
fun StatusDot(tone: Tone, modifier: Modifier = Modifier) {
    Box(
        modifier = modifier
            .clearAndSetSemantics { }
            .size(9.dp)
            .background(tone.color, CircleShape),
    )
}

/**
 * The card everything sits in.
 *
 * The border tone is the entire status language of this system. A card with a Signal border is
 * saying something is ready; a Live border is saying something is wrong; a Line border is saying
 * nothing at all, which is what an informational card should say.
 */
@Composable
fun InfoCard(
    modifier: Modifier = Modifier,
    borderTone: Tone = Tone.Line,
    shape: Shape = FlintShapes.Medium,
    content: @Composable androidx.compose.foundation.layout.ColumnScope.() -> Unit,
) {
    Column(
        modifier = modifier
            .fillMaxWidth()
            .background(FlintColors.Panel, shape)
            .border(FlintSpace.Hairline, borderTone.color, shape)
            .padding(FlintSpace.CardPadding),
        verticalArrangement = Arrangement.spacedBy(FlintSpace.Small),
        content = content,
    )
}

/**
 * The inset block under a card's reason.
 *
 * One step further off the ground than the card, and its body is painted in the primary text colour
 * rather than the muted one — brighter than the reason above it, because the reason explains and this
 * tells somebody what to do.
 */
@Composable
fun AdvisoryBlock(heading: String, body: String, modifier: Modifier = Modifier) {
    Column(
        modifier = modifier
            .fillMaxWidth()
            .background(FlintColors.Raised, FlintShapes.Small)
            .padding(horizontal = FlintSpace.Compact, vertical = 10.dp),
        verticalArrangement = Arrangement.spacedBy(5.dp),
    ) {
        FlintText(text = heading, style = FlintType.LabelSmall)
        FlintText(text = body, style = FlintType.BodySmall.copy(color = FlintColors.Text))
    }
}

/**
 * A page's heading.
 *
 * The eyebrow is Signal, the headline is the page's name, and any status pill is aligned to the
 * bottom of the headline rather than its centre, so a long headline that wraps does not drag the
 * pill upward with it.
 */
@Composable
fun PageHeading(
    eyebrow: String,
    headline: String,
    modifier: Modifier = Modifier,
    statusText: String? = null,
    statusTone: Tone = Tone.Neutral,
) {
    Row(
        modifier = modifier.fillMaxWidth(),
        verticalAlignment = Alignment.Bottom,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            FlintText(
                text = eyebrow.uppercase(Locale.ROOT),
                style = FlintType.LabelSmall.copy(color = FlintColors.Signal),
            )
            Spacer(Modifier.height(FlintSpace.Tiny))
            FlintText(
                text = headline,
                style = FlintType.HeadlineLarge,
                modifier = Modifier.semantics { heading() },
            )
        }
        if (statusText != null) {
            Spacer(Modifier.width(FlintSpace.Compact))
            Pill(text = statusText, tone = statusTone)
        }
    }
}

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

/**
 * What a screen shows when it has nothing.
 *
 * The glyph is a letterform rather than an icon, which is what makes an empty state read as part of
 * the same instrument rather than as a picture that failed to load.
 */
@Composable
fun EmptyState(
    glyph: String,
    title: String,
    body: String,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier
            .fillMaxWidth()
            .background(FlintColors.Panel, FlintShapes.Medium)
            .border(FlintSpace.Hairline, FlintColors.Line, FlintShapes.Medium)
            .padding(FlintSpace.Large),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(FlintSpace.Small),
    ) {
        Box(
            modifier = Modifier
                .clearAndSetSemantics { }
                .size(FlintSpace.Huge)
                .border(FlintSpace.Hairline, FlintColors.Line, FlintShapes.Small),
            contentAlignment = Alignment.Center,
        ) {
            FlintText(
                text = glyph,
                style = FlintType.TitleMedium.copy(color = FlintColors.Muted),
            )
        }
        FlintText(text = title, style = FlintType.TitleMedium)
        FlintText(text = body, style = FlintType.BodySmall)
    }
}

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
        modifier = modifier
            .defaultMinSize(minHeight = FlintSpace.TouchTarget)
            .height(FlintSpace.TouchTarget)
            .flintClickable(enabled = enabled, onClick = onClick)
            .background(
                if (enabled) FlintColors.Signal else FlintColors.Line,
                FlintShapes.Pill,
            )
            .padding(horizontal = FlintSpace.Large),
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
            .height(FlintSpace.TouchTarget)
            .flintClickable(enabled = enabled, onClick = onClick)
            .border(
                FlintSpace.Hairline,
                if (enabled) tone.color else FlintColors.Line,
                FlintShapes.Pill,
            )
            .padding(horizontal = FlintSpace.Large),
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
