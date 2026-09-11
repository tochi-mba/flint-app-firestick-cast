package com.rextechnologies.flint.mobile.platform

import kotlin.math.abs

/**
 * Reads one pixel out of an `ImageReader` plane.
 *
 * Its own file, and free of every Android type, because it is the part of the second-screen probe
 * with arithmetic in it -- and the only part a unit test can reach without a display. Inside the
 * probe it was three absolute array indices that ignored the buffer's position and the plane's
 * strides, which is wrong on any hardware that pads a row, and there was nowhere to put a test that
 * would have said so.
 */
object PixelReader {
    /** How far a channel may drift from the colour that was painted and still count as it. */
    const val CHANNEL_TOLERANCE: Int = 2

    /** One pixel's colour channels, in the order an RGBA_8888 plane stores them. */
    internal data class Rgb(val red: Int, val green: Int, val blue: Int) {
        /**
         * Compared with a small tolerance rather than exactly.
         *
         * RGBA_8888 is not supposed to alter what was written, but the path from a View's background
         * to a VirtualDisplay's buffer goes through a compositor that some vendors let apply
         * dithering or a colour transform. A channel two values out is still evidently the painted
         * colour; black is not, which is the case that has to fail.
         */
        fun matches(red: Int, green: Int, blue: Int): Boolean =
            abs(this.red - red) <= CHANNEL_TOLERANCE &&
                abs(this.green - green) <= CHANNEL_TOLERANCE &&
                abs(this.blue - blue) <= CHANNEL_TOLERANCE
    }

    /**
     * The colour of one pixel, or `null` when the buffer does not reach it.
     *
     * Pure, and separated from the probe for that reason: it is the part with arithmetic in it and
     * the part a unit test can reach without a display. It reads through the plane's own strides
     * rather than assuming the buffer is packed. It is not: a `rowStride` larger than
     * `width * pixelStride` is the ordinary case on hardware that aligns each row, and a
     * `pixelStride` above four is legal. An earlier version indexed bytes 0, 1 and 2 absolutely,
     * which also ignored the buffer's own position.
     */
    internal fun channelsAt(
        bytes: ByteArray,
        position: Int,
        rowStride: Int,
        pixelStride: Int,
        column: Int = 0,
        row: Int = 0,
    ): Rgb? {
        if (position < 0 || rowStride <= 0 || pixelStride <= 0 || column < 0 || row < 0) return null
        val offset = position.toLong() + row.toLong() * rowStride + column.toLong() * pixelStride
        if (offset < 0 || offset + 3 > bytes.size) return null
        val at = offset.toInt()
        return Rgb(
            red = bytes[at].toInt() and 0xff,
            green = bytes[at + 1].toInt() and 0xff,
            blue = bytes[at + 2].toInt() and 0xff,
        )
    }
}
