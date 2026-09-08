package com.rextechnologies.flint.protocol.http

import java.security.SecureRandom
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNotEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class TokenPathTest {
    @Test
    fun `session tokens are canonical immutable and constant-time comparable`() {
        val token = SessionToken.generate(FixedRandom(0x2a))
        val text = token.toString()
        assertEquals(43, text.length)
        assertEquals(token, SessionToken.parse(text))
        assertEquals(token.hashCode(), SessionToken.parse(text).hashCode())
        assertTrue(token.constantTimeMatches(text))
        assertFalse(token.constantTimeMatches(text.dropLast(1) + "A"))
        assertNull(SessionToken.parseOrNull("short"))
        assertFailsWith<IllegalArgumentException> { SessionToken.parse("=".repeat(43)) }
        assertFailsWith<IllegalArgumentException> { SessionToken.parse("A".repeat(42) + "B") }
    }

    @Test
    fun `shared item ids validate canonical opaque shape`() {
        val id = SharedItemId.generate(FixedRandom(7))
        assertEquals(22, id.toString().length)
        assertEquals(id, SharedItemId.parse(id.toString()))
        assertEquals(id.hashCode(), SharedItemId.parse(id.toString()).hashCode())
        assertNull(SharedItemId.parseOrNull("../secret"))
        assertFailsWith<IllegalArgumentException> { SharedItemId.parse("x") }
        assertNotEquals(id, SharedItemId.generate(FixedRandom(8)))
    }

    @Test
    fun `token path accepts only exact authorised structure`() {
        val token = SessionToken.generate(FixedRandom(1))
        val id = SharedItemId.generate(FixedRandom(2))
        val path = TokenPath.build(token, id)
        assertEquals(id, assertNotNull(TokenPath.parse(path, token)).itemId)
        assertEquals(id, assertNotNull(TokenPath.parse("$path?download=1", token)).itemId)

        val otherToken = SessionToken.generate(FixedRandom(3))
        val invalid = listOf(
            "",
            "x".repeat(513),
            path.replace("/media/", "/Media/"),
            path.replace("/v1/", "/v2/"),
            path.replace(token.toString(), otherToken.toString()),
            "$path/extra",
            path.drop(1),
            path.replace(id.toString(), "../secret"),
            path.replace(id.toString(), "%2e%2e"),
            path.replace('/', '\\'),
            "$path#fragment",
            path + "\u0000",
            path + "\u0001",
        )
        invalid.forEach { assertNull(TokenPath.parse(it, token), it) }
    }

    @Test
    fun `registry resolves only explicitly shared values and supports revocation`() {
        val token = SessionToken.generate(FixedRandom(4))
        val registry = TokenPathRegistry<String>(token, IncrementingRandom())
        val first = registry.share("movie")
        val second = registry.share("photo")
        assertNotEquals(first, second)
        assertEquals("movie", registry.resolve(first))
        assertEquals("photo", registry.resolve(second + "?cache=no"))
        assertNull(registry.resolve(first.replace(token.toString(), SessionToken.generate().toString())))
        assertTrue(registry.revoke(first))
        assertFalse(registry.revoke(first))
        assertNull(registry.resolve(first))
        registry.clear()
        assertNull(registry.resolve(second))
    }

    private class FixedRandom(private val byte: Int) : SecureRandom() {
        override fun nextBytes(bytes: ByteArray) = bytes.fill(byte.toByte())
    }

    private class IncrementingRandom : SecureRandom() {
        private var next = 0
        override fun nextBytes(bytes: ByteArray) {
            bytes.fill(next++.toByte())
        }
    }
}

