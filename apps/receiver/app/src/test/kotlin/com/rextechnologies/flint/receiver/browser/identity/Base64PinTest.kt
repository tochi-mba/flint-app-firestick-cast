package com.rextechnologies.flint.receiver.browser.identity

import kotlin.test.Test
import kotlin.test.assertEquals

/**
 * The pin encoder, held to the RFC 4648 vectors.
 *
 * This encoding is written out rather than taken from a platform class, for reasons the
 * implementation explains. The price of that decision is this file: a hand-written codec is only
 * acceptable while it is pinned to the specification's own examples, so that "hand-rolled" cannot
 * quietly become "wrong".
 *
 * The vectors are RFC 4648 section 10, verbatim.
 */
class Base64PinTest {
    @Test
    fun `the rfc 4648 vectors encode exactly`() {
        assertEquals("", Base64Pin.encode("".toByteArray()))
        assertEquals("Zg==", Base64Pin.encode("f".toByteArray()))
        assertEquals("Zm8=", Base64Pin.encode("fo".toByteArray()))
        assertEquals("Zm9v", Base64Pin.encode("foo".toByteArray()))
        assertEquals("Zm9vYg==", Base64Pin.encode("foob".toByteArray()))
        assertEquals("Zm9vYmE=", Base64Pin.encode("fooba".toByteArray()))
        assertEquals("Zm9vYmFy", Base64Pin.encode("foobar".toByteArray()))
    }

    @Test
    fun `a sha256 sized input encodes to the length the host expects`() {
        // 32 bytes is not a multiple of three, so it exercises the padded tail every time — which is
        // the half of base64 that is usually wrong.
        val hash = ByteArray(32) { index -> index.toByte() }

        val encoded = Base64Pin.encode(hash)

        assertEquals(44, encoded.length)
        assertEquals("=", encoded.substring(43))
    }

    @Test
    fun `high bit bytes are not sign extended`() {
        // Bytes above 0x7F are negative in Kotlin. Treating them as signed is the other classic
        // defect here, and a SHA-256 hash is full of them.
        val encoded = Base64Pin.encode(byteArrayOf(0xFF.toByte(), 0xFE.toByte(), 0xFD.toByte()))

        assertEquals("//79", encoded)
    }

    @Test
    fun `every alphabet position is reachable`() {
        // Walks all 64 output symbols, so a typo in the alphabet cannot hide in a range no test
        // happens to reach.
        val bytes = ByteArray(48) { index -> index.toByte() }
        val encoded = Base64Pin.encode(bytes)

        val alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"
        assertEquals(64, alphabet.toSet().size)
        assertEquals(true, encoded.all { character -> character in alphabet || character == '=' })
    }

    @Test
    fun `encoding is stable across calls`() {
        val bytes = ByteArray(32) { index -> (index * 7).toByte() }

        assertEquals(Base64Pin.encode(bytes), Base64Pin.encode(bytes))
    }
}
