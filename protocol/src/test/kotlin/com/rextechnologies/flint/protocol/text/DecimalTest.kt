package com.rextechnologies.flint.protocol.text

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith

class DecimalTest {
    @Test
    fun `one decimal place, rounded rather than truncated`() {
        assertEquals("0.0", Decimal.oneDecimal(0.0))
        assertEquals("1.0", Decimal.oneDecimal(1.0))
        assertEquals("12.4", Decimal.oneDecimal(12.36))
        assertEquals("8.5", Decimal.oneDecimal(8.46))
        assertEquals("0.1", Decimal.oneDecimal(0.05))
    }

    @Test
    fun `a negative value carries one sign, in front`() {
        assertEquals("-1.2", Decimal.oneDecimal(-1.2))
        assertEquals("-12.4", Decimal.oneDecimal(-12.36))
    }

    @Test
    fun `a negative value that rounds to zero is not printed as minus zero`() {
        assertEquals("0.0", Decimal.oneDecimal(-0.0))
        assertEquals("0.0", Decimal.oneDecimal(-0.01))
    }

    @Test
    fun `a value too large to print saturates at the documented magnitude, not at Long`() {
        assertEquals("1000000000.0", Decimal.oneDecimal(Double.MAX_VALUE))
        assertEquals("-1000000000.0", Decimal.oneDecimal(-Double.MAX_VALUE))
        assertEquals("1000000000.0", Decimal.oneDecimal(Decimal.MAXIMUM_MAGNITUDE * 4))
    }

    @Test
    fun `a tie rounds away from zero, not to the nearest even number`() {
        // The rule Math.rint would apply instead: 0.5 to 0, 1.5 to 2, 2.5 to 2.
        assertEquals("1", Decimal.whole(0.5))
        assertEquals("2", Decimal.whole(1.5))
        assertEquals("3", Decimal.whole(2.5))
        assertEquals("-1", Decimal.whole(-0.5))
        assertEquals("-3", Decimal.whole(-2.5))
        assertEquals("0.3", Decimal.oneDecimal(0.25))
        assertEquals("-0.3", Decimal.oneDecimal(-0.25))
    }

    @Test
    fun `whole numbers round half away from zero and saturate the same way`() {
        assertEquals("0", Decimal.whole(0.4))
        assertEquals("1", Decimal.whole(0.5))
        assertEquals("13", Decimal.whole(12.7))
        assertEquals("-13", Decimal.whole(-12.7))
        assertEquals("1000000000", Decimal.whole(Double.MAX_VALUE))
        assertEquals("-1000000000", Decimal.whole(-Double.MAX_VALUE))
    }

    @Test
    fun `a value that is not a number has no printed form`() {
        assertFailsWith<IllegalArgumentException> { Decimal.oneDecimal(Double.NaN) }
        assertFailsWith<IllegalArgumentException> { Decimal.oneDecimal(Double.POSITIVE_INFINITY) }
        assertFailsWith<IllegalArgumentException> { Decimal.whole(Double.NaN) }
        assertFailsWith<IllegalArgumentException> { Decimal.whole(Double.NEGATIVE_INFINITY) }
    }

    @Test
    fun `printing and reading back returns the value it was given`() {
        var value = 0.0
        while (value < 2_000.0) {
            val printed = Decimal.oneDecimal(value)
            assertEquals(value, printed.toDouble(), 0.06, "round trip failed for $value")
            value += 0.37
        }
    }
}
