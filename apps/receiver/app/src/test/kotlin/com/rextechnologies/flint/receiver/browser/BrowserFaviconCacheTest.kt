package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertNotEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

class BrowserFaviconCacheTest {
    @Test
    fun `a valid bounded png is cached and returned defensively`() {
        val cache = BrowserFaviconCache()
        val bytes = png(12)

        val accepted = assertIs<BrowserFaviconOffer.Accepted>(cache.offer(32, 32, bytes))
        bytes[8] = 99
        val cached = cache.get(accepted.favicon.id)!!

        assertContentEquals(png(12), cached.pngBytes)
        assertEquals(32, cached.width)
        assertEquals(32, cached.height)
    }

    @Test
    fun `same png receives the same stable id even after eviction`() {
        val cache = BrowserFaviconCache(maxEntries = 1)
        val first = assertIs<BrowserFaviconOffer.Accepted>(cache.offer(16, 16, png(1))).favicon.id
        cache.offer(16, 16, png(2))

        val repeated = assertIs<BrowserFaviconOffer.Accepted>(cache.offer(16, 16, png(1))).favicon.id

        assertEquals(first, repeated)
    }

    @Test
    fun `different content receives a different id`() {
        val cache = BrowserFaviconCache()

        val first = assertIs<BrowserFaviconOffer.Accepted>(cache.offer(16, 16, png(1))).favicon.id
        val second = assertIs<BrowserFaviconOffer.Accepted>(cache.offer(16, 16, png(2))).favicon.id

        assertNotEquals(first, second)
    }

    @Test
    fun `oversized dimensions and payload are rejected`() {
        val cache = BrowserFaviconCache()

        assertEquals(
            BrowserFaviconRejection.INVALID_DIMENSIONS,
            assertIs<BrowserFaviconOffer.Rejected>(cache.offer(65, 64, png(1))).reason,
        )
        assertEquals(
            BrowserFaviconRejection.PAYLOAD_TOO_LARGE,
            assertIs<BrowserFaviconOffer.Rejected>(
                cache.offer(64, 64, png(1, BrowserFaviconCache.MAX_PNG_BYTES + 1)),
            ).reason,
        )
    }

    @Test
    fun `non png and empty payloads are rejected`() {
        val cache = BrowserFaviconCache()

        assertEquals(
            BrowserFaviconRejection.NOT_PNG,
            assertIs<BrowserFaviconOffer.Rejected>(cache.offer(16, 16, ByteArray(12))).reason,
        )
        assertEquals(
            BrowserFaviconRejection.EMPTY_PAYLOAD,
            assertIs<BrowserFaviconOffer.Rejected>(cache.offer(16, 16, byteArrayOf())).reason,
        )
    }

    @Test
    fun `least recently read entry is evicted`() {
        val cache = BrowserFaviconCache(maxEntries = 2)
        val one = assertIs<BrowserFaviconOffer.Accepted>(cache.offer(16, 16, png(1))).favicon.id
        val two = assertIs<BrowserFaviconOffer.Accepted>(cache.offer(16, 16, png(2))).favicon.id
        cache.get(one)

        val three = assertIs<BrowserFaviconOffer.Accepted>(cache.offer(16, 16, png(3))).favicon.id

        assertTrue(cache.contains(one))
        assertNull(cache.get(two))
        assertTrue(cache.contains(three))
    }

    @Test
    fun `clear releases every cached byte`() {
        val cache = BrowserFaviconCache()
        cache.offer(16, 16, png(1))
        cache.offer(16, 16, png(2))

        cache.clear()

        assertEquals(0, cache.size)
        assertEquals(0, cache.totalBytes)
    }

    @Test
    fun `wire bounds are inclusive and every invalid dimension is rejected`() {
        val cache = BrowserFaviconCache()
        val exact = assertIs<BrowserFaviconOffer.Accepted>(
            cache.offer(
                BrowserFaviconCache.MAX_WIDTH,
                BrowserFaviconCache.MAX_HEIGHT,
                png(7, BrowserFaviconCache.MAX_PNG_BYTES),
            ),
        )

        assertEquals(BrowserFaviconCache.MAX_PNG_BYTES, exact.favicon.byteCount)
        listOf(0 to 1, -1 to 1, 1 to 0, 1 to -1, 1 to 65).forEach { (width, height) ->
            assertEquals(
                BrowserFaviconRejection.INVALID_DIMENSIONS,
                assertIs<BrowserFaviconOffer.Rejected>(cache.offer(width, height, png(1))).reason,
            )
        }
    }

    @Test
    fun `a duplicate refreshes LRU without increasing bytes and is returned defensively`() {
        val cache = BrowserFaviconCache(maxEntries = 2)
        val oneBytes = png(1)
        val one = assertIs<BrowserFaviconOffer.Accepted>(cache.offer(16, 16, oneBytes))
        val two = assertIs<BrowserFaviconOffer.Accepted>(cache.offer(16, 16, png(2)))

        val duplicate = assertIs<BrowserFaviconOffer.Accepted>(cache.offer(16, 16, oneBytes))
        assertFalse(duplicate.inserted)
        assertEquals(one.favicon.id, duplicate.favicon.id)
        assertEquals(one.favicon.byteCount + two.favicon.byteCount, cache.totalBytes)
        duplicate.favicon.pngBytes.fill(0)
        cache.offer(16, 16, png(3))

        assertTrue(cache.contains(one.favicon.id))
        assertNull(cache.get(two.favicon.id))
        assertContentEquals(oneBytes, cache.get(one.favicon.id)!!.pngBytes)
    }

    private fun png(marker: Int, size: Int = 12): ByteArray = ByteArray(size.coerceAtLeast(8)).also {
        PNG_SIGNATURE.copyInto(it)
        if (it.size > 8) it[8] = marker.toByte()
    }

    private companion object {
        val PNG_SIGNATURE = byteArrayOf(-119, 80, 78, 71, 13, 10, 26, 10)
    }
}
