package com.rextechnologies.flint.protocol.http

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNull

class RangeRequestTest {
    @Test
    fun `no header means full response`() {
        assertNull(RangeRequest.parse(null, 100))
        assertNull(RangeRequest.parse(null, 0))
    }

    @Test
    fun `closed open and suffix ranges resolve against resource`() {
        assertEquals(ByteRange(2, 5), RangeRequest.parse("bytes=2-5", 10))
        assertEquals(ByteRange(2, 9), RangeRequest.parse("BYTES = 2-", 10))
        assertEquals(ByteRange(7, 9), RangeRequest.parse("bytes=-3", 10))
        assertEquals(ByteRange(0, 9), RangeRequest.parse("bytes=-99", 10))
        assertEquals(ByteRange(8, 9), RangeRequest.parse("bytes=8-999", 10))
    }

    @Test
    fun `byte range reports length and content range`() {
        val range = ByteRange(3, 7)
        assertEquals(5, range.length)
        assertEquals("bytes 3-7/10", range.contentRange(10))
        assertFailsWith<IllegalArgumentException> { ByteRange(-1, 2) }
        assertFailsWith<IllegalArgumentException> { ByteRange(3, 2) }
        assertFailsWith<IllegalArgumentException> { range.contentRange(7) }
    }

    @Test
    fun `invalid ranges consistently map to 416 metadata`() {
        val invalid = listOf(
            "items=0-1",
            "bytes=",
            "bytes=0-1,3-4",
            "bytes=0--1",
            "bytes=0 -1",
            "bytes=-",
            "bytes=a-1",
            "bytes=1-b",
            "bytes=-0",
            "bytes=10-",
            "bytes=8-7",
            "bytes=999999999999999999999999-",
            "bytes=-999999999999999999999999",
            "bytes=0-999999999999999999999999",
        )
        invalid.forEach { header ->
            val failure = assertFailsWith<RangeNotSatisfiableException>(header) {
                RangeRequest.parse(header, 10)
            }
            assertEquals(10, failure.resourceLength)
            assertEquals("bytes */10", failure.contentRangeHeader)
        }
    }

    @Test
    fun `empty resource rejects every supplied range and negative lengths are programming errors`() {
        assertFailsWith<RangeNotSatisfiableException> { RangeRequest.parse("bytes=0-", 0) }
        assertFailsWith<IllegalArgumentException> { RangeRequest.parse(null, -1) }
    }
}


