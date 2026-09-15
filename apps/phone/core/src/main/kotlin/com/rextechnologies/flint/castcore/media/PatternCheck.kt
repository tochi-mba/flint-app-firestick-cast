package com.rextechnologies.flint.castcore.media

import kotlin.math.abs
import kotlin.math.roundToInt

/** One pixel's colour, as the eight-bit channels a display or a decoder hands back. */
data class Rgb(val red: Int, val green: Int, val blue: Int) {
    init {
        require(red in 0..255 && green in 0..255 && blue in 0..255) { "A channel is one byte" }
    }

    /** Whether every channel is within [tolerance] of [other]'s. */
    fun isNear(other: Rgb, tolerance: Int): Boolean = distance(other) <= tolerance

    /** The largest channel difference from [other], so a failure can say how far off the frame was. */
    fun distance(other: Rgb): Int =
        maxOf(abs(red - other.red), abs(green - other.green), abs(blue - other.blue))

    override fun toString(): String = "rgb($red, $green, $blue)"
}

enum class Quadrant {
    TOP_LEFT,
    TOP_RIGHT,
    BOTTOM_LEFT,
    BOTTOM_RIGHT,
}

/**
 * The frame the encoder round trip draws, and then looks for on the far side of a decoder.
 *
 * Four quadrants in four colours that are far from one another and far from black, from grey and
 * from the flat green a broken pipeline in this project once produced. A frame that comes back as
 * any one colour fails three quadrants; a frame that comes back swapped fails four; only the frame
 * that was drawn passes. The colours stop short of full saturation because a limited-range YUV
 * conversion clips the extremes, and a clipped channel would read as a codec fault it is not.
 */
object TestPattern {
    val colours: Map<Quadrant, Rgb> = mapOf(
        Quadrant.TOP_LEFT to Rgb(200, 40, 40),
        Quadrant.TOP_RIGHT to Rgb(40, 200, 40),
        Quadrant.BOTTOM_LEFT to Rgb(40, 40, 200),
        Quadrant.BOTTOM_RIGHT to Rgb(200, 200, 40),
    )

    /**
     * Where a quadrant is sampled: its centre.
     *
     * Well clear of the seams between quadrants, where chroma subsampling blends neighbours, and
     * clear of the horizontal midline, which is where the round trip draws the moving marker that
     * keeps frames flowing.
     */
    fun samplePoint(quadrant: Quadrant, width: Int, height: Int): Pair<Int, Int> {
        require(width >= 4 && height >= 4) { "A pattern needs room for four quadrants" }
        val left = quadrant == Quadrant.TOP_LEFT || quadrant == Quadrant.BOTTOM_LEFT
        val top = quadrant == Quadrant.TOP_LEFT || quadrant == Quadrant.TOP_RIGHT
        val x = if (left) width / 4 else width * 3 / 4
        val y = if (top) height / 4 else height * 3 / 4
        return x to y
    }
}

/**
 * BT.601 limited-range conversion, both ways.
 *
 * Limited range and 601 because that is what an Android video encoder assumes for a surface it is
 * handed with no colour information attached, and what its decoder therefore produces. A phone
 * that used 709 instead shifts these saturated colours by a few tens of a channel at most, which is
 * why [PatternCheck.TOLERANCE] is as wide as it is and no wider.
 */
object YuvToRgb {
    fun convert(y: Int, u: Int, v: Int): Rgb {
        val luma = 1.164 * (y - 16)
        val cb = u - 128.0
        val cr = v - 128.0
        return Rgb(
            red = clamp(luma + 1.596 * cr),
            green = clamp(luma - 0.392 * cb - 0.813 * cr),
            blue = clamp(luma + 2.017 * cb),
        )
    }

    /** The inverse, for tests that build a frame and for anything that has to paint in YUV. */
    fun encode(colour: Rgb): IntArray {
        val r = colour.red.toDouble()
        val g = colour.green.toDouble()
        val b = colour.blue.toDouble()
        return intArrayOf(
            clamp(16 + 0.257 * r + 0.504 * g + 0.098 * b),
            clamp(128 - 0.148 * r - 0.291 * g + 0.439 * b),
            clamp(128 + 0.439 * r - 0.368 * g - 0.071 * b),
        )
    }

    private fun clamp(value: Double): Int = value.roundToInt().coerceIn(0, 255)
}

/**
 * One plane of a 4:2:0 image, as bytes plus the strides the platform reported for it.
 *
 * The strides are the whole point. A row is commonly padded to an alignment the hardware likes, and
 * the two chroma planes of a semi-planar buffer are interleaved with a pixel stride of two. Indexing
 * the bytes as if they were packed reads the wrong pixel on exactly the devices worth checking.
 */
class PlaneBytes(private val bytes: ByteArray, val rowStride: Int, val pixelStride: Int) {
    init {
        require(rowStride > 0 && pixelStride > 0) { "A stride is a positive number of bytes" }
    }

    /** The sample at ([column], [row]), or `null` when the buffer does not reach it. */
    fun at(column: Int, row: Int): Int? {
        if (column < 0 || row < 0) return null
        val index = row.toLong() * rowStride + column.toLong() * pixelStride
        if (index >= bytes.size) return null
        return bytes[index.toInt()].toInt() and 0xff
    }
}

/** Reads one colour out of a 4:2:0 image whose chroma planes are half the size in each direction. */
object YuvSampler {
    fun sample(luma: PlaneBytes, cb: PlaneBytes, cr: PlaneBytes, x: Int, y: Int): Rgb? {
        val l = luma.at(x, y) ?: return null
        val u = cb.at(x / 2, y / 2) ?: return null
        val v = cr.at(x / 2, y / 2) ?: return null
        return YuvToRgb.convert(l, u, v)
    }
}

/** What a decoded frame turned out to be, in one sentence. */
data class PatternVerdict(val passed: Boolean, val detail: String)

/**
 * Whether a decoded frame is the pattern that was drawn.
 *
 * A codec test that only checks structure is not a codec test. This is the part that looks at the
 * pixels, and it is pure so the rule can be tested against exact values, a flat frame and a swapped
 * frame without a codec in the room.
 */
object PatternCheck {
    /**
     * How far a channel may drift and still count as the colour that was drawn.
     *
     * Wide enough for the encoder's quantisation, chroma subsampling and a 601-versus-709 shift;
     * narrow enough that no single flat colour is within it of all four quadrants at once, because
     * the quadrant colours are at least 160 apart in some channel.
     */
    const val TOLERANCE: Int = 48

    fun verdict(samples: Map<Quadrant, Rgb>): PatternVerdict {
        val missing = Quadrant.entries.filter { it !in samples }
        if (missing.isNotEmpty()) {
            return PatternVerdict(false, "No pixel could be read for ${missing.joinToString { name(it) }}.")
        }
        for (quadrant in Quadrant.entries) {
            val expected = TestPattern.colours.getValue(quadrant)
            val actual = samples.getValue(quadrant)
            if (!actual.isNear(expected, TOLERANCE)) {
                return PatternVerdict(
                    false,
                    "The ${name(quadrant)} quadrant came back as $actual rather than $expected, " +
                        "${actual.distance(expected)} away where $TOLERANCE is the most allowed.",
                )
            }
        }
        return PatternVerdict(true, "All four quadrants came back within $TOLERANCE of what was drawn.")
    }

    private fun name(quadrant: Quadrant): String = quadrant.name.lowercase().replace('_', ' ')
}
