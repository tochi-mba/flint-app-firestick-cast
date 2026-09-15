package com.rextechnologies.flint.receiver.snapshot

import android.graphics.Bitmap
import kotlin.math.abs

/**
 * How a rendered frame differs from its approved image.
 *
 * @property width Width of both images, in pixels.
 * @property height Height of both images, in pixels.
 * @property differingPixels Pixels whose difference exceeded the channel tolerance.
 * @property worstChannelDifference The largest single-channel difference found, on a 0-255 scale.
 *   Reported even on success, because a run sitting just under the tolerance is worth knowing about
 *   before it starts failing.
 */
data class ImageComparison(
    val width: Int,
    val height: Int,
    val differingPixels: Int,
    val worstChannelDifference: Int,
) {
    /** Total pixels in the compared area. */
    val totalPixels: Int get() = width * height

    /** The fraction of pixels that differed, as a percentage. */
    val differingPercent: Double
        get() = if (totalPixels == 0) 0.0 else differingPixels.toDouble() / totalPixels * 100.0

    /** Whether the difference is within [maximumPercent]. */
    fun isWithin(maximumPercent: Double): Boolean = differingPercent <= maximumPercent

    override fun toString(): String =
        "$differingPixels/$totalPixels pixels differ (${"%.3f".format(differingPercent)}%), " +
            "worst channel difference $worstChannelDifference/255"
}

/**
 * Compares two bitmaps pixel by pixel, with a tolerance for rendering noise.
 *
 * Deliberately the same two-tolerance scheme as the Windows app's comparer, and for the same
 * reasons. The channel tolerance absorbs text antialiasing, which varies between machines and
 * platform versions; without it, approved images would have to be regenerated wherever the tests
 * happened to run, and a team that regenerates images routinely stops reading the diffs. The
 * differing-pixel budget is what actually catches regressions, because a moved control or a wrong
 * colour token moves thousands of pixels while antialiasing moves a few hundred.
 */
object ImageComparer {
    /**
     * How far a single channel may differ before the pixel counts as changed.
     *
     * Eight of 255, matching the Windows suite. Narrower than the closest pair of REX surface
     * tokens, so a card rendered on the wrong surface still fails.
     */
    const val DEFAULT_CHANNEL_TOLERANCE: Int = 8

    /**
     * The share of pixels that may differ before a comparison fails, as a percentage.
     *
     * A tenth of one percent. On a 1920x1080 television frame that is about two thousand pixels:
     * far more than antialiasing produces, far less than any visible element occupies.
     */
    const val DEFAULT_MAXIMUM_DIFFERING_PERCENT: Double = 0.1

    /**
     * Compares two bitmaps of the same size.
     *
     * @throws IllegalArgumentException when the two differ in size, which is a caller bug rather
     *   than a failed comparison and must not be reported as one.
     */
    fun compare(
        expected: Bitmap,
        actual: Bitmap,
        channelTolerance: Int = DEFAULT_CHANNEL_TOLERANCE,
    ): ImageComparison {
        require(expected.width == actual.width && expected.height == actual.height) {
            "expected ${expected.width}x${expected.height}, got ${actual.width}x${actual.height}"
        }

        val width = expected.width
        val height = expected.height
        val expectedPixels = expected.toPixels()
        val actualPixels = actual.toPixels()

        var differing = 0
        var worst = 0
        for (index in expectedPixels.indices) {
            val difference = worstChannel(expectedPixels[index], actualPixels[index])
            if (difference > worst) {
                worst = difference
            }
            if (difference > channelTolerance) {
                differing++
            }
        }

        return ImageComparison(width, height, differing, worst)
    }

    /**
     * Builds an image highlighting the pixels that differed.
     *
     * Unchanged pixels are kept, dimmed, so the changed ones can be located in context rather than
     * floating in a black field. Changed pixels are painted in the REX `Live` orange, which is the
     * palette's "something is wrong" colour and cannot occur in a dimmed grey background.
     */
    fun buildDiff(
        expected: Bitmap,
        actual: Bitmap,
        channelTolerance: Int = DEFAULT_CHANNEL_TOLERANCE,
    ): Bitmap {
        val width = expected.width
        val height = expected.height
        val expectedPixels = expected.toPixels()
        val actualPixels = actual.toPixels()
        val diff = IntArray(expectedPixels.size)

        for (index in expectedPixels.indices) {
            val source = expectedPixels[index]
            diff[index] =
                if (worstChannel(source, actualPixels[index]) > channelTolerance) {
                    LIVE
                } else {
                    val luminance =
                        (((source shr 16 and 0xFF) + (source shr 8 and 0xFF) + (source and 0xFF)) / 3) / 4
                    0xFF shl 24 or (luminance shl 16) or (luminance shl 8) or luminance
                }
        }

        return Bitmap.createBitmap(diff, width, height, Bitmap.Config.ARGB_8888)
    }

    /** The largest per-channel difference between two ARGB pixels. */
    private fun worstChannel(left: Int, right: Int): Int {
        var worst = 0
        for (shift in intArrayOf(24, 16, 8, 0)) {
            val difference = abs((left shr shift and 0xFF) - (right shr shift and 0xFF))
            if (difference > worst) {
                worst = difference
            }
        }
        return worst
    }

    private fun Bitmap.toPixels(): IntArray {
        val pixels = IntArray(width * height)
        getPixels(pixels, 0, width, 0, 0, width, height)
        return pixels
    }

    /** The REX `Live` accent, #FF774D, opaque. */
    private const val LIVE: Int = 0xFFFF774D.toInt()
}
