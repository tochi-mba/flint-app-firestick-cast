package com.rextechnologies.flint.mobile.platform

import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNotNull
import kotlin.test.assertNull

/** The stored-token format, tested without a keystore because it is the half that fails silently. */
class TokenEnvelopeTest {
    @Test
    fun `packing and unpacking is the identity`() {
        val iv = ByteArray(12) { it.toByte() }
        val ciphertext = ByteArray(40) { (it * 7).toByte() }
        val unpacked = assertNotNull(TokenEnvelope.unpack(TokenEnvelope.pack(iv, ciphertext)))
        assertContentEquals(iv, unpacked.iv)
        assertContentEquals(ciphertext, unpacked.ciphertext)
    }

    @Test
    fun `the nonce length is carried rather than assumed`() {
        // Twelve bytes is the usual GCM nonce and sixteen is a provider's right. Both must survive.
        listOf(1, 12, 16, 255).forEach { length ->
            val iv = ByteArray(length) { 0x5A }
            val unpacked = assertNotNull(TokenEnvelope.unpack(TokenEnvelope.pack(iv, byteArrayOf(1))), "$length")
            assertEquals(length, unpacked.iv.size)
        }
    }

    @Test
    fun `a nonce too long for the length byte is refused at pack time`() {
        assertFailsWith<IllegalArgumentException> { TokenEnvelope.pack(ByteArray(256), byteArrayOf(1)) }
        assertFailsWith<IllegalArgumentException> { TokenEnvelope.pack(ByteArray(0), byteArrayOf(1)) }
        assertFailsWith<IllegalArgumentException> { TokenEnvelope.pack(ByteArray(12), ByteArray(0)) }
    }

    @Test
    fun `a stored value that is not an envelope is a lost pairing, not a crash`() {
        assertNull(TokenEnvelope.unpack(ByteArray(0)))
        assertNull(TokenEnvelope.unpack(byteArrayOf(12)))
        // A length byte of zero, which no packer writes.
        assertNull(TokenEnvelope.unpack(byteArrayOf(0, 1, 2)))
        // A length byte that claims more nonce than there are bytes.
        assertNull(TokenEnvelope.unpack(byteArrayOf(12, 1, 2, 3)))
        // Exactly the nonce and nothing after it: nothing was stored.
        assertNull(TokenEnvelope.unpack(byteArrayOf(2, 1, 2)))
    }

    @Test
    fun `the length byte is read unsigned`() {
        // 200 as a signed byte is negative; read signed it would fail the "at least one" check and
        // silently forget a pairing whose provider used a long nonce.
        val iv = ByteArray(200) { 1 }
        val unpacked = assertNotNull(TokenEnvelope.unpack(TokenEnvelope.pack(iv, byteArrayOf(9))))
        assertEquals(200, unpacked.iv.size)
    }

    @Test
    fun `unpacked values compare by content`() {
        val first = TokenEnvelope.unpack(TokenEnvelope.pack(byteArrayOf(1, 2), byteArrayOf(3)))
        val second = TokenEnvelope.unpack(TokenEnvelope.pack(byteArrayOf(1, 2), byteArrayOf(3)))
        assertEquals(first, second)
        assertEquals(first.hashCode(), second.hashCode())
    }
}
