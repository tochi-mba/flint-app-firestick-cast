package com.rextechnologies.flint.castcore.media

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class PresentationGeometryTest {
    @Test
    fun `the second screen is a landscape 1080p canvas whatever the phone's own screen is`() {
        // The mirror follows the phone; the second screen does not. A portrait phone used to produce
        // a portrait television picture, which is the shape of a mistake rather than of a product.
        val (width, height) = PresentationGeometry.canvas()
        assertEquals(1920, width)
        assertEquals(1080, height)
        assertTrue(width > height)
    }

    @Test
    fun `the canvas is inside the encoder's cap and even in both directions`() {
        val (width, height) = PresentationGeometry.canvas()
        assertTrue(maxOf(width, height) <= EncoderPolicy.MAXIMUM_LONG_EDGE)
        assertEquals(0, width % 2)
        assertEquals(0, height % 2)
    }

    @Test
    fun `the canvas is laid out at the density a 1080p television reports`() {
        // 1920 by 1080 at xhdpi is 960 by 540 dp, which is the geometry the receiver's screens are
        // snapshot at, so the same type scale reads from a sofa on both.
        assertEquals(320, PresentationGeometry.DENSITY_DPI)
        assertEquals(960, PresentationGeometry.CANVAS_WIDTH * 160 / PresentationGeometry.DENSITY_DPI)
    }
}

class SendFailureWatchTest {
    @Test
    fun `a run of refusals one short of the limit does not trip, and a success starts the count again`() {
        val watch = SendFailureWatch(limit = 3)
        assertFalse(watch.onWrite(false))
        assertFalse(watch.onWrite(false))
        assertFalse(watch.onWrite(true))
        assertFalse(watch.onWrite(false))
        assertFalse(watch.onWrite(false))
        assertFalse(watch.tripped)
    }

    @Test
    fun `the write that reaches the limit trips, exactly once`() {
        val watch = SendFailureWatch(limit = 3)
        assertFalse(watch.onWrite(false))
        assertFalse(watch.onWrite(false))
        assertTrue(watch.onWrite(false))
        assertTrue(watch.tripped)
        // Every refusal after the trip is still a refusal, and still not a second trip: the caller
        // stops the output once, not once per frame that arrives while it is stopping.
        assertFalse(watch.onWrite(false))
        assertFalse(watch.onWrite(false))
        assertTrue(watch.tripped)
    }

    @Test
    fun `the default limit is half a second of frames, and a limit below one is refused`() {
        assertEquals(30, SendFailureWatch.DEFAULT_LIMIT)
        assertFailsWith<IllegalArgumentException> { SendFailureWatch(limit = 0) }
    }
}
