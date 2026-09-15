package com.rextechnologies.flint.mobile.platform

import java.nio.ByteBuffer
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class PlaneBufferTest {
    @Test
    fun `interleaved direct chroma views read their own samples across padded rows`() {
        val storage = ByteBuffer.allocateDirect(10).apply {
            put(byteArrayOf(16, 128.toByte(), 17, 129.toByte(), 0, 0, 18, 130.toByte(), 19, 131.toByte()))
            flip()
        }
        val cbBytes = storage.duplicate().apply { limit(9) }.slice().asReadOnlyBuffer()
        val crBytes = storage.duplicate().apply { position(1) }.slice()
        val cb = PlaneBuffer(cbBytes, rowStride = 6, pixelStride = 2)
        val cr = PlaneBuffer(crBytes, rowStride = 6, pixelStride = 2)

        assertEquals(16, cb.at(0, 0))
        assertEquals(17, cb.at(1, 0))
        assertEquals(18, cb.at(0, 1))
        assertEquals(19, cb.at(1, 1))
        assertEquals(128, cr.at(0, 0))
        assertEquals(129, cr.at(1, 0))
        assertEquals(130, cr.at(0, 1))
        assertEquals(131, cr.at(1, 1))
        assertEquals(0, cbBytes.position())
        assertEquals(0, crBytes.position())
    }

    @Test
    fun `the original position locates the plane and reads leave buffer state unchanged`() {
        val bytes = ByteBuffer.allocateDirect(6).apply {
            put(byteArrayOf(1, 2, 255.toByte(), 128.toByte(), 127, 99))
            position(2)
            limit(5)
        }
        val plane = PlaneBuffer(bytes, rowStride = 3, pixelStride = 1)

        assertEquals(255, plane.at(0, 0))
        assertEquals(128, plane.at(1, 0))
        assertEquals(127, plane.at(2, 0))
        assertEquals(2, bytes.position())
        assertEquals(5, bytes.limit())

        bytes.position(3)
        assertEquals(255, plane.at(0, 0), "The captured origin must not move with the caller's cursor")
        assertEquals(3, bytes.position())
    }

    @Test
    fun `a final row needs only bytes through its last pixel`() {
        // Two 3-pixel rows, stride 8: the second row ends at byte 10, with no trailing padding.
        val bytes = ByteBuffer.allocateDirect(11).apply {
            put(0, 10)
            put(2, 12)
            put(8, 20)
            put(10, 22)
        }
        val plane = PlaneBuffer(bytes, rowStride = 8, pixelStride = 1)

        assertEquals(10, plane.at(0, 0))
        assertEquals(12, plane.at(2, 0))
        assertEquals(20, plane.at(0, 1))
        assertEquals(22, plane.at(2, 1))
        assertNull(plane.at(3, 1))
        assertNull(plane.at(0, 2))
    }

    @Test
    fun `the limit bounds readable data even when capacity extends further`() {
        val bytes = ByteBuffer.allocateDirect(16).apply {
            put(4, 42)
            put(5, 99)
            position(4)
            limit(5)
        }
        val plane = PlaneBuffer(bytes, rowStride = 1, pixelStride = 1)

        assertEquals(42, plane.at(0, 0))
        assertNull(plane.at(1, 0))
        assertNull(plane.at(0, 1))
        bytes.limit(4)
        assertNull(plane.at(0, 0))
        assertNull(PlaneBuffer(ByteBuffer.allocate(0), 1, 1).at(0, 0))
    }

    @Test
    fun `invalid geometry does not access the buffer`() {
        val bytes = ByteBuffer.allocate(1)
        for (stride in listOf(0, -1, Int.MIN_VALUE)) {
            assertNull(PlaneBuffer(bytes, rowStride = stride, pixelStride = 1).at(0, 0))
            assertNull(PlaneBuffer(bytes, rowStride = 1, pixelStride = stride).at(0, 0))
        }
        val plane = PlaneBuffer(bytes, rowStride = 1, pixelStride = 1)
        assertNull(plane.at(-1, 0))
        assertNull(plane.at(0, -1))
        assertNull(plane.at(Int.MIN_VALUE, Int.MIN_VALUE))
    }

    @Test
    fun `large offsets cannot wrap into a readable byte`() {
        val bytes = ByteBuffer.allocate(8).apply { position(3) }
        val plane = PlaneBuffer(bytes, rowStride = 65_536, pixelStride = 65_536)
        assertNull(plane.at(65_536, 0))
        assertNull(plane.at(0, 65_536))
        assertNull(PlaneBuffer(bytes, Int.MAX_VALUE, Int.MAX_VALUE).at(Int.MAX_VALUE, Int.MAX_VALUE))
        assertEquals(3, bytes.position())
    }
}
