package com.rextechnologies.flint.design

import androidx.compose.ui.text.PlatformTextStyle
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.LineHeightStyle
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * The REX type scale.
 *
 * The desktop's sizes rather than the television's. `FlintTypography.axaml` records that Android `sp`
 * and Avalonia device-independent pixels map one for one, so these are the same numbers the Windows
 * shell uses — which is the point: a phone is held at the same distance as a laptop screen, and the
 * TV scale exists for a viewer ten feet away.
 *
 * The signature of the system is [LabelSmall]: 9sp, bold, heavily tracked, and always upper-cased by
 * the component rather than at the call site. It is what makes a REX screen read as an instrument
 * rather than a web page, and it is used for every eyebrow, pill and status word.
 */
object FlintType {
    /**
     * Inter is the face this system is drawn in, on the desktop and on the site.
     *
     * It is not bundled here. Dropping the four weights into `apps/phone/design/src/main/res/font/`
     * and pointing this one value at them is the whole change; until then the platform's own
     * sans-serif is used, which is metrically close enough that no layout depends on the
     * difference.
     */
    val fontFamily: FontFamily = FontFamily.SansSerif

    val HeadlineLarge: TextStyle = TextStyle(
        fontFamily = fontFamily,
        fontSize = 28.sp,
        lineHeight = 32.sp,
        fontWeight = FontWeight.Bold,
        color = FlintColors.Text,
    )

    val TitleLarge: TextStyle = TextStyle(
        fontFamily = fontFamily,
        fontSize = 18.sp,
        lineHeight = 22.sp,
        fontWeight = FontWeight.Bold,
        color = FlintColors.Text,
    )

    val TitleMedium: TextStyle = TextStyle(
        fontFamily = fontFamily,
        fontSize = 15.sp,
        lineHeight = 19.sp,
        fontWeight = FontWeight.Bold,
        color = FlintColors.Text,
    )

    val BodyMedium: TextStyle = TextStyle(
        fontFamily = fontFamily,
        fontSize = 13.sp,
        lineHeight = 19.sp,
        fontWeight = FontWeight.Normal,
        color = FlintColors.Text,
    )

    val BodySmall: TextStyle = TextStyle(
        fontFamily = fontFamily,
        fontSize = 12.sp,
        lineHeight = 17.sp,
        fontWeight = FontWeight.Normal,
        color = FlintColors.Muted,
    )

    /** The eyebrow. The component upper-cases it; the tracking does the rest. */
    val LabelSmall: TextStyle = TextStyle(
        fontFamily = fontFamily,
        fontSize = 9.sp,
        lineHeight = 12.sp,
        fontWeight = FontWeight.Bold,
        letterSpacing = 1.4.sp,
        color = FlintColors.Muted,
    )

    /**
     * Numerals in a telemetry strip.
     *
     * Tabular figures stop a readout jittering as its digits change, which matters when a value
     * updates sixty times a second.
     */
    val Readout: TextStyle = TextStyle(
        fontFamily = fontFamily,
        fontSize = 15.sp,
        lineHeight = 19.sp,
        fontWeight = FontWeight.Bold,
        fontFeatureSettings = "tnum",
        color = FlintColors.Text,
    )

    /**
     * Carried verbatim from the receiver, where it was learned the hard way.
     *
     * Legacy Android font metrics reserve extra space above a glyph's ascent, a leftover from
     * pre-Lollipop text rendering that Compose still defaults to for compatibility. A system built on
     * 9sp all-caps labels inside small pills is exactly the case where that shows: the label sits
     * visibly above the centre of its container rather than in it. Applied once through the theme, it
     * fixes every piece of text in the app.
     */
    val CenteredMetrics: TextStyle = TextStyle(
        platformStyle = PlatformTextStyle(includeFontPadding = false),
        lineHeightStyle = LineHeightStyle(
            alignment = LineHeightStyle.Alignment.Center,
            trim = LineHeightStyle.Trim.Both,
        ),
    )
}

/**
 * One spacing ramp, so rows line up without each component inventing its own gaps.
 *
 * [TouchTarget] is the one number here that is not a rhythm choice: it is the minimum size a control
 * may be and still be reliably hit by a thumb, and nothing in this app is allowed below it.
 */
object FlintSpace {
    val Hairline: Dp = 1.dp
    val Tiny: Dp = 4.dp
    val Small: Dp = 8.dp
    val Compact: Dp = 12.dp
    val Medium: Dp = 16.dp
    val Large: Dp = 24.dp
    val XLarge: Dp = 32.dp
    val Huge: Dp = 48.dp

    /** The page's side margin, matching the desktop's. */
    val PageMargin: Dp = 28.dp

    /** Inside a card. */
    val CardPadding: Dp = 16.dp

    /** Between cards. */
    val CardSpacing: Dp = 12.dp

    val TouchTarget: Dp = 48.dp
}
