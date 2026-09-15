package com.rextechnologies.flint.mobile.platform

import java.nio.ByteBuffer

/**
 * Reads only requested samples from an image plane while its Image is still open.
 *
 * A plane may have padding between rows without mapping padding after its last pixel. Copying the
 * entire native buffer can therefore touch memory outside the image. Absolute single-byte reads
 * avoid that padding and preserve the caller's buffer position.
 */
internal class PlaneBuffer(private val bytes: ByteBuffer, val rowStride: Int, val pixelStride: Int) {
    private val start = bytes.position()

    /** The unsigned sample at ([column], [row]), or `null` for invalid or unavailable coordinates. */
    fun at(column: Int, row: Int): Int? {
        if (rowStride <= 0 || pixelStride <= 0 || column < 0 || row < 0) return null
        val index = start.toLong() + row.toLong() * rowStride + column.toLong() * pixelStride
        if (index >= bytes.limit()) return null
        return bytes.get(index.toInt()).toInt() and 0xff
    }
}
