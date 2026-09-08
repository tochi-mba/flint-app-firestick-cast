package com.rextechnologies.flint.protocol.adb

import com.rextechnologies.flint.protocol.BinaryData
import java.security.KeyPairGenerator
import java.security.interfaces.RSAPublicKey
import java.util.Base64
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class AdbAuthTest {
    private val identity by lazy { AdbAuth.generateIdentity("rexcast@test") }

    @Test
    fun `generated identity signs exact ADB token and verifies it`() {
        val token = ByteArray(AdbAuth.TOKEN_BYTES) { it.toByte() }
        val signature = AdbAuth.signToken(identity, token)
        assertEquals(AdbAuth.RSA_BITS / 8, signature.size)
        assertTrue(AdbAuth.verifyTokenSignature(identity.publicKey, token, signature))
        assertFalse(AdbAuth.verifyTokenSignature(identity.publicKey, token.copyOf().also { it[0]++ }, signature))
        assertFalse(AdbAuth.verifyTokenSignature(identity.publicKey, token.copyOf(19), signature))
        assertFalse(AdbAuth.verifyTokenSignature(identity.publicKey, token, BinaryData.of(ByteArray(1))))
        assertFailsWith<IllegalArgumentException> { AdbAuth.signToken(identity, ByteArray(19)) }
    }

    @Test
    fun `signature from one identity cannot verify with another`() {
        val token = ByteArray(AdbAuth.TOKEN_BYTES) { 9 }
        val other = AdbAuth.generateIdentity()
        assertFalse(AdbAuth.verifyTokenSignature(other.publicKey, token, AdbAuth.signToken(identity, token)))
    }

    @Test
    fun `public key payload has Android wire shape and messages use correct auth types`() {
        val payload = AdbAuth.publicKeyPayload(identity).toByteArray()
        assertEquals(0, payload.last().toInt())
        val line = payload.copyOf(payload.size - 1).toString(Charsets.US_ASCII)
        val encoded = line.substringBefore(' ')
        assertEquals("rexcast@test", line.substringAfter(' '))
        val binary = Base64.getDecoder().decode(encoded)
        assertEquals(4 + 4 + 256 + 256 + 4, binary.size)
        assertEquals(64, readUint32Le(binary, 0))
        assertEquals((identity.publicKey as RSAPublicKey).publicExponent.toLong(), readUint32Le(binary, binary.size - 4))

        val token = ByteArray(20)
        val signature = AdbAuth.signatureMessage(identity, token)
        assertEquals(AdbCommands.AUTH, signature.command)
        assertEquals(AdbAuthType.SIGNATURE, signature.argument0)
        val publicKey = AdbAuth.publicKeyMessage(identity)
        assertEquals(AdbAuthType.RSA_PUBLIC_KEY, publicKey.argument0)
        assertEquals(AdbAuth.publicKeyPayload(identity), publicKey.payload)
    }

    @Test
    fun `fingerprints are stable lower-case SHA256 and identity wrapper validates inputs`() {
        val fingerprint = AdbAuth.fingerprint(identity.publicKey)
        assertEquals(95, fingerprint.length)
        assertTrue(fingerprint.matches(Regex("([0-9a-f]{2}:){31}[0-9a-f]{2}")))
        assertEquals(fingerprint, AdbAuth.fingerprint(identity.publicKey))

        val rsa = KeyPairGenerator.getInstance("RSA").apply { initialize(2048) }.generateKeyPair()
        assertEquals(rsa.public, AdbAuth.identity(rsa).publicKey)
        assertFailsWith<IllegalArgumentException> { AdbAuth.identity(rsa, "bad\ncomment") }
        val weak = KeyPairGenerator.getInstance("RSA").apply { initialize(1024) }.generateKeyPair()
        assertFailsWith<IllegalArgumentException> { AdbAuth.identity(weak) }
        val ec = KeyPairGenerator.getInstance("EC").apply { initialize(256) }.generateKeyPair()
        assertFailsWith<IllegalArgumentException> { AdbAuth.identity(ec) }
    }

    private fun readUint32Le(bytes: ByteArray, offset: Int): Long =
        (bytes[offset].toLong() and 0xff) or
            ((bytes[offset + 1].toLong() and 0xff) shl 8) or
            ((bytes[offset + 2].toLong() and 0xff) shl 16) or
            ((bytes[offset + 3].toLong() and 0xff) shl 24)
}

