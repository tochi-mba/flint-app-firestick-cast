package com.rextechnologies.flint.protocol

import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertSame

class BinaryDataRegionTest {
    @Test
    fun `a region is copied, not borrowed`() {
        val source = byteArrayOf(1, 2, 3, 4, 5)
        val region = BinaryData.of(source, 1, 3)
        source[2] = 9
        assertContentEquals(byteArrayOf(2, 3, 4), region.toByteArray())
        assertEquals(3, region.size)
    }

    @Test
    fun `an empty region is the shared empty value`() {
        assertSame(BinaryData.EMPTY, BinaryData.of(byteArrayOf(1, 2), 1, 0))
    }

    @Test
    fun `a region outside the array is refused`() {
        assertFailsWith<IllegalArgumentException> { BinaryData.of(byteArrayOf(1, 2), 1, 2) }
        assertFailsWith<IllegalArgumentException> { BinaryData.of(byteArrayOf(1, 2), -1, 1) }
        assertFailsWith<IllegalArgumentException> { BinaryData.of(byteArrayOf(1, 2), 0, -1) }
    }
}
