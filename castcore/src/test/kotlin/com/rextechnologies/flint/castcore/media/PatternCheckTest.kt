package com.rextechnologies.flint.castcore.media

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class PatternCheckTest {
    @Test
    fun `the frame that was drawn passes, and says so in one sentence`() {
        val verdict = PatternCheck.verdict(TestPattern.colours)
        assertTrue(verdict.passed, verdict.detail)
        assertTrue(verdict.detail.endsWith("."), verdict.detail)
    }

    @Test
    fun `a frame that drifted by less than the tolerance in every channel still passes`() {
        val drifted = TestPattern.colours.mapValues { (_, colour) ->
            Rgb(
                (colour.red + PatternCheck.TOLERANCE).coerceAtMost(255),
                (colour.green - PatternCheck.TOLERANCE).coerceAtLeast(0),
                colour.blue,
            )
        }
        assertTrue(PatternCheck.verdict(drifted).passed)
    }

    @Test
    fun `one channel one step past the tolerance fails, and the detail names the quadrant`() {
        val expected = TestPattern.colours.getValue(Quadrant.BOTTOM_LEFT)
        val tooFar = TestPattern.colours + (
            Quadrant.BOTTOM_LEFT to expected.copy(blue = expected.blue - PatternCheck.TOLERANCE - 1)
            )
        val verdict = PatternCheck.verdict(tooFar)
        assertFalse(verdict.passed)
        assertTrue(verdict.detail.contains("bottom left"), verdict.detail)
        assertTrue(verdict.detail.contains("${PatternCheck.TOLERANCE + 1} away"), verdict.detail)
    }

    @Test
    fun `a flat frame of any colour fails, including the green a broken pipeline once produced`() {
        val flats = listOf(Rgb(0, 0, 0), Rgb(0, 255, 0), Rgb(128, 128, 128), Rgb(255, 255, 255)) +
            TestPattern.colours.values
        flats.forEach { flat ->
            val frame = Quadrant.entries.associateWith { flat }
            assertFalse(PatternCheck.verdict(frame).passed, "a flat $flat frame passed")
        }
    }

    @Test
    fun `the right colours in the wrong places fail`() {
        val rotated = Quadrant.entries.associateWith { quadrant ->
            val next = Quadrant.entries[(quadrant.ordinal + 1) % Quadrant.entries.size]
            TestPattern.colours.getValue(next)
        }
        assertFalse(PatternCheck.verdict(rotated).passed)
    }

    @Test
    fun `a quadrant that could not be read fails rather than being skipped`() {
        val partial = TestPattern.colours - Quadrant.TOP_RIGHT
        val verdict = PatternCheck.verdict(partial)
        assertFalse(verdict.passed)
        assertTrue(verdict.detail.contains("top right"), verdict.detail)
    }

    @Test
    fun `the quadrant colours are far enough apart that no single colour is near all four`() {
        // The tolerance is only safe if no colour can sit within it of every quadrant at once. Two
        // quadrants at most 2 * TOLERANCE apart in every channel would let one colour pass both.
        val colours = TestPattern.colours.values.toList()
        for (i in colours.indices) {
            for (j in i + 1 until colours.size) {
                assertTrue(
                    colours[i].distance(colours[j]) > 2 * PatternCheck.TOLERANCE,
                    "${colours[i]} and ${colours[j]} are too close for the tolerance",
                )
            }
        }
    }

    @Test
    fun `sample points sit at quadrant centres, away from the seams and the midline`() {
        assertEquals(160 to 90, TestPattern.samplePoint(Quadrant.TOP_LEFT, 640, 360))
        assertEquals(480 to 90, TestPattern.samplePoint(Quadrant.TOP_RIGHT, 640, 360))
        assertEquals(160 to 270, TestPattern.samplePoint(Quadrant.BOTTOM_LEFT, 640, 360))
        assertEquals(480 to 270, TestPattern.samplePoint(Quadrant.BOTTOM_RIGHT, 640, 360))
        assertFailsWith<IllegalArgumentException> { TestPattern.samplePoint(Quadrant.TOP_LEFT, 2, 2) }
    }

    @Test
    fun `a channel outside a byte is refused`() {
        assertFailsWith<IllegalArgumentException> { Rgb(256, 0, 0) }
        assertFailsWith<IllegalArgumentException> { Rgb(0, -1, 0) }
    }
}

class YuvToRgbTest {
    @Test
    fun `every pattern colour survives a trip through limited-range yuv within a few steps`() {
        TestPattern.colours.values.forEach { colour ->
            val (y, u, v) = YuvToRgb.encode(colour)
            val back = YuvToRgb.convert(y, u, v)
            assertTrue(back.isNear(colour, 4), "$colour came back as $back")
        }
    }

    @Test
    fun `black and white land on the limited-range anchors`() {
        assertEquals(listOf(16, 128, 128), YuvToRgb.encode(Rgb(0, 0, 0)).toList())
        assertEquals(listOf(235, 128, 128), YuvToRgb.encode(Rgb(255, 255, 255)).toList())
        assertEquals(Rgb(0, 0, 0), YuvToRgb.convert(16, 128, 128))
        assertEquals(Rgb(255, 255, 255), YuvToRgb.convert(235, 128, 128))
    }

    @Test
    fun `values past the anchors clamp rather than wrap`() {
        assertEquals(Rgb(0, 0, 0), YuvToRgb.convert(0, 128, 128))
        assertEquals(Rgb(255, 255, 255), YuvToRgb.convert(255, 128, 128))
        val extreme = YuvToRgb.convert(128, 255, 255)
        assertTrue(extreme.red == 255 && extreme.blue == 255 && extreme.green == 0, extreme.toString())
    }
}

class YuvSamplerTest {
    /** A 4x4 image whose planes are padded and, for chroma, interleaved -- what real hardware hands back. */
    private fun planes(colour: Rgb): Triple<PlaneBytes, PlaneBytes, PlaneBytes> {
        val (y, u, v) = YuvToRgb.encode(colour)
        val lumaRowStride = 8
        val luma = ByteArray(lumaRowStride * 4) { index -> if (index % lumaRowStride < 4) y.toByte() else 0x7f }
        // Semi-planar chroma: U and V interleaved in one buffer, so each plane has a pixel stride
        // of two and the V plane starts one byte in.
        val chromaRowStride = 8
        val chroma = ByteArray(chromaRowStride * 2)
        for (row in 0 until 2) {
            for (column in 0 until 2) {
                chroma[row * chromaRowStride + column * 2] = u.toByte()
                chroma[row * chromaRowStride + column * 2 + 1] = v.toByte()
            }
        }
        return Triple(
            PlaneBytes(luma, lumaRowStride, 1),
            PlaneBytes(chroma, chromaRowStride, 2),
            PlaneBytes(chroma.copyOfRange(1, chroma.size), chromaRowStride, 2),
        )
    }

    @Test
    fun `a pixel is read through the row and pixel strides rather than as a packed array`() {
        TestPattern.colours.values.forEach { colour ->
            val (luma, cb, cr) = planes(colour)
            val sampled = assertNotNull(YuvSampler.sample(luma, cb, cr, 3, 3))
            assertTrue(sampled.isNear(colour, 4), "$colour was read as $sampled")
        }
    }

    @Test
    fun `a pixel outside the buffer is null rather than a wrong colour`() {
        val (luma, cb, cr) = planes(Rgb(200, 40, 40))
        assertNull(YuvSampler.sample(luma, cb, cr, 3, 4))
        assertNull(YuvSampler.sample(luma, cb, cr, -1, 0))
    }

    @Test
    fun `a stride that is not positive is refused up front`() {
        assertFailsWith<IllegalArgumentException> { PlaneBytes(ByteArray(4), 0, 1) }
        assertFailsWith<IllegalArgumentException> { PlaneBytes(ByteArray(4), 4, 0) }
    }
}
