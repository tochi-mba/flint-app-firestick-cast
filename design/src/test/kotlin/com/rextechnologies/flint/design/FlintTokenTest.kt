package com.rextechnologies.flint.design

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.sp
import org.junit.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/**
 * Pins the REX ink/signal visual system.
 *
 * The mirror image of `tests/Flint.App.Tests/RexDesignSystemTests.cs`, which asserts the same values
 * on the desktop. The tokens are shared with that shell and with the Fire TV receiver, and drifting
 * one of them here would silently break the family resemblance across three codebases — nothing
 * fails to compile, the products simply stop looking like one product. So they are asserted
 * directly, as literals, rather than compared against another copy of themselves.
 */
class FlintColorsTest {
    @Test
    fun `the palette matches the desktop exactly`() {
        assertEquals(Color(0xFF080A09), FlintColors.Ink)
        assertEquals(Color(0xFF111512), FlintColors.Panel)
        assertEquals(Color(0xFF181E19), FlintColors.Raised)
        assertEquals(Color(0xFF29302A), FlintColors.Line)
        assertEquals(Color(0xFFF2F5EE), FlintColors.Text)
        assertEquals(Color(0xFF858D83), FlintColors.Muted)
        assertEquals(Color(0xFFD7FF3F), FlintColors.Signal)
        assertEquals(Color(0xFFFF774D), FlintColors.Live)
    }

    @Test
    fun `muted is the desktop's value and deliberately not the television's`() {
        // The receiver lifts it to #A3ABA0 for legibility across a room. A phone is held at arm's
        // length, where the lifted value reads as washed out rather than as secondary.
        assertEquals(Color(0xFF858D83), FlintColors.Muted)
        assertTrue(FlintColors.Muted != Color(0xFFA3ABA0))
    }

    @Test
    fun `the ground is never pure black and the text never pure white`() {
        assertTrue(FlintColors.Ink != Color.Black)
        assertTrue(FlintColors.Text != Color.White)
    }

    @Test
    fun `the washes are the accent and the failure tone at ten per cent`() {
        // sRGB alpha is stored in 8-bit steps, so 10% is encoded as 26/255 rather than exactly 0.10.
        val tenPercentAlpha = 26f / 255f
        assertEquals(tenPercentAlpha, FlintColors.SignalWash.alpha, 0.0001f)
        assertEquals(tenPercentAlpha, FlintColors.LiveWash.alpha, 0.0001f)
        assertEquals(FlintColors.Signal.red, FlintColors.SignalWash.red)
        assertEquals(FlintColors.Live.red, FlintColors.LiveWash.red)
    }

    @Test
    fun `only a genuinely available thing earns the accent`() {
        assertEquals(FlintColors.Signal, Tone.Signal.color)
        assertEquals(FlintColors.Live, Tone.Live.color)
        assertEquals(FlintColors.Muted, Tone.Neutral.color)
        assertEquals(FlintColors.Line, Tone.Line.color)
    }

    @Test
    fun `every tone has a wash as well as a colour`() {
        Tone.entries.forEach { tone ->
            assertTrue(tone.wash.alpha < 1f, "$tone should wash rather than fill")
        }
    }
}

class FlintTypeTest {
    @Test
    fun `the type scale carries the desktop metrics one for one`() {
        assertEquals(28.sp, FlintType.HeadlineLarge.fontSize)
        assertEquals(32.sp, FlintType.HeadlineLarge.lineHeight)
        assertEquals(18.sp, FlintType.TitleLarge.fontSize)
        assertEquals(22.sp, FlintType.TitleLarge.lineHeight)
        assertEquals(15.sp, FlintType.TitleMedium.fontSize)
        assertEquals(19.sp, FlintType.TitleMedium.lineHeight)
        assertEquals(13.sp, FlintType.BodyMedium.fontSize)
        assertEquals(19.sp, FlintType.BodyMedium.lineHeight)
        assertEquals(12.sp, FlintType.BodySmall.fontSize)
        assertEquals(17.sp, FlintType.BodySmall.lineHeight)
        assertEquals(9.sp, FlintType.LabelSmall.fontSize)
    }

    @Test
    fun `the label keeps the tracking that defines the system`() {
        // 1.4 on a 9sp bold label is what makes a REX screen read as an instrument.
        assertEquals(1.4.sp, FlintType.LabelSmall.letterSpacing)
    }

    @Test
    fun `a readout uses tabular figures so it does not jitter as digits change`() {
        assertEquals("tnum", FlintType.Readout.fontFeatureSettings)
        assertEquals(15.sp, FlintType.Readout.fontSize)
    }

    @Test
    fun `secondary text is muted and primary text is not`() {
        assertEquals(FlintColors.Muted, FlintType.BodySmall.color)
        assertEquals(FlintColors.Text, FlintType.BodyMedium.color)
        assertEquals(FlintColors.Muted, FlintType.LabelSmall.color)
    }

    @Test
    fun `the legacy font padding is dropped once, for everything`() {
        val platform = FlintType.CenteredMetrics.platformStyle
        assertTrue(platform != null)
        assertTrue(FlintType.CenteredMetrics.lineHeightStyle != null)
    }
}

class FlintSpaceTest {
    @Test
    fun `the spacing ramp is the one the desktop uses`() {
        assertEquals(1, FlintSpace.Hairline.value.toInt())
        assertEquals(4, FlintSpace.Tiny.value.toInt())
        assertEquals(8, FlintSpace.Small.value.toInt())
        assertEquals(12, FlintSpace.Compact.value.toInt())
        assertEquals(16, FlintSpace.Medium.value.toInt())
        assertEquals(24, FlintSpace.Large.value.toInt())
        assertEquals(32, FlintSpace.XLarge.value.toInt())
        assertEquals(48, FlintSpace.Huge.value.toInt())
    }

    @Test
    fun `the page grid matches the desktop's`() {
        assertEquals(28, FlintSpace.PageMargin.value.toInt())
        assertEquals(16, FlintSpace.CardPadding.value.toInt())
        assertEquals(12, FlintSpace.CardSpacing.value.toInt())
    }

    @Test
    fun `nothing is allowed to be smaller than a thumb`() {
        assertEquals(48, FlintSpace.TouchTarget.value.toInt())
    }
}
