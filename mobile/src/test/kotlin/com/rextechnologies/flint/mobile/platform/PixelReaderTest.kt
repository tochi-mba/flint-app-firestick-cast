package com.rextechnologies.flint.mobile.platform

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * The arithmetic the second-screen probe reads a pixel with.
 *
 * A plain JVM test: nothing here touches a display, which is the reason the arithmetic was moved out
 * of the probe in the first place. Every case below is one the probe used to get wrong by indexing
 * bytes 0, 1 and 2 absolutely.
 */
class PixelReaderTest {
    @Test
    fun `a packed plane reads the first pixel from the start`() {
        val bytes = byteArrayOf(0xD7.toByte(), 0xFF.toByte(), 0x3F, 0xFF.toByte())
        val pixel = assertNotNull(PixelReader.channelsAt(bytes, position = 0, rowStride = 4, pixelStride = 4))
        assertEquals(PixelReader.Rgb(0xD7, 0xFF, 0x3F), pixel)
    }

    @Test
    fun `the buffer's own position is honoured`() {
        // Two junk bytes, then the pixel. Absolute indexing read the junk.
        val bytes = byteArrayOf(0, 0, 10, 20, 30, 40)
        val pixel = assertNotNull(PixelReader.channelsAt(bytes, position = 2, rowStride = 4, pixelStride = 4))
        assertEquals(PixelReader.Rgb(10, 20, 30), pixel)
    }

    @Test
    fun `a padded row is walked by its stride, not by its width`() {
        // A 2-pixel-wide image whose rows are padded to 16 bytes. The pixel at row 1, column 1 lives
        // at 16 + 4, not at 2 * 4 + 4.
        val bytes = ByteArray(32)
        bytes[20] = 1
        bytes[21] = 2
        bytes[22] = 3
        val pixel = PixelReader.channelsAt(bytes, position = 0, rowStride = 16, pixelStride = 4, column = 1, row = 1)
        assertEquals(PixelReader.Rgb(1, 2, 3), assertNotNull(pixel))
    }

    @Test
    fun `a pixel stride above four is legal and respected`() {
        val bytes = ByteArray(24)
        bytes[8] = 9
        bytes[9] = 8
        bytes[10] = 7
        val pixel = PixelReader.channelsAt(bytes, position = 0, rowStride = 24, pixelStride = 8, column = 1)
        assertEquals(PixelReader.Rgb(9, 8, 7), assertNotNull(pixel))
    }

    @Test
    fun `a buffer that does not reach the pixel answers null rather than throwing`() {
        val bytes = ByteArray(5)
        assertNull(PixelReader.channelsAt(bytes, position = 3, rowStride = 4, pixelStride = 4))
        assertNull(PixelReader.channelsAt(bytes, position = 0, rowStride = 4, pixelStride = 4, row = 1))
        assertNull(PixelReader.channelsAt(ByteArray(0), position = 0, rowStride = 4, pixelStride = 4))
    }

    @Test
    fun `nonsense geometry answers null rather than reading somewhere`() {
        val bytes = ByteArray(16)
        assertNull(PixelReader.channelsAt(bytes, position = -1, rowStride = 4, pixelStride = 4))
        assertNull(PixelReader.channelsAt(bytes, position = 0, rowStride = 0, pixelStride = 4))
        assertNull(PixelReader.channelsAt(bytes, position = 0, rowStride = 4, pixelStride = 0))
        assertNull(PixelReader.channelsAt(bytes, position = 0, rowStride = 4, pixelStride = 4, column = -1))
    }

    @Test
    fun `channels are read unsigned`() {
        val bytes = byteArrayOf(0xFF.toByte(), 0x80.toByte(), 0x7F, 0)
        assertEquals(PixelReader.Rgb(255, 128, 127), assertNotNull(PixelReader.channelsAt(bytes, 0, 4, 4)))
    }

    @Test
    fun `the painted colour matches within the tolerance and black does not`() {
        val painted = PixelReader.Rgb(0xD7, 0xFF, 0x3F)
        assertTrue(painted.matches(0xD7, 0xFF, 0x3F))
        assertTrue(PixelReader.Rgb(0xD7 + PixelReader.CHANNEL_TOLERANCE, 0xFF, 0x3F).matches(0xD7, 0xFF, 0x3F))
        assertTrue(PixelReader.Rgb(0xD7, 0xFF - PixelReader.CHANNEL_TOLERANCE, 0x3F).matches(0xD7, 0xFF, 0x3F))
        assertFalse(PixelReader.Rgb(0xD7 + PixelReader.CHANNEL_TOLERANCE + 1, 0xFF, 0x3F).matches(0xD7, 0xFF, 0x3F))
        // The case that must fail: a correctly shaped buffer full of nothing.
        assertFalse(PixelReader.Rgb(0, 0, 0).matches(0xD7, 0xFF, 0x3F))
    }
}
