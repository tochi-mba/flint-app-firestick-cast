package com.rextechnologies.flint.castcore.setup

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith

class AdbPortScanTest {
    @Test
    fun `the documented range is thirty-one ports from 5555`() {
        assertEquals(31, AdbPortScan.PORTS.count())
        assertEquals(5_555, AdbPortScan.PORTS.first)
        assertEquals(5_585, AdbPortScan.PORTS.last)
    }

    @Test
    fun `a port that answered before is tried first and only once`() {
        val order = AdbPortScan.order(5_560)
        assertEquals(5_560, order.first())
        assertEquals(31, order.size)
        assertEquals(31, order.distinct().size)
        assertEquals(AdbPortScan.PORTS.toList(), AdbPortScan.order(0))
    }

    @Test
    fun `a known port outside the range is still tried first, then the range`() {
        val order = AdbPortScan.order(5_037)
        assertEquals(5_037, order.first())
        assertEquals(32, order.size)
    }

    @Test
    fun `a port that is not one is refused`() {
        assertFailsWith<IllegalArgumentException> { AdbPortScan.order(-1) }
        assertFailsWith<IllegalArgumentException> { AdbPortScan.order(70_000) }
    }
}
