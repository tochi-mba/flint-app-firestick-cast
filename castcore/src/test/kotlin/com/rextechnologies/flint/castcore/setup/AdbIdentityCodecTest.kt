package com.rextechnologies.flint.castcore.setup

import com.rextechnologies.flint.protocol.adb.AdbAuth
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class AdbIdentityCodecTest {
    @Test
    fun `an identity survives the round trip with the same keys and the comment asked for`() {
        val original = AdbAuth.generateIdentity("flint-mobile@android")
        val restored = assertNotNull(AdbIdentityCodec.decode(AdbIdentityCodec.encode(original)))
        assertEquals(original.privateKey, restored.privateKey)
        assertEquals(original.publicKey, restored.publicKey)
        assertEquals(AdbIdentityCodec.DEFAULT_COMMENT, restored.comment)
        assertEquals(
            "other@phone",
            assertNotNull(AdbIdentityCodec.decode(AdbIdentityCodec.encode(original), "other@phone")).comment,
        )
    }

    @Test
    fun `the television sees the same fingerprint before and after a restart`() {
        val original = AdbAuth.generateIdentity()
        val restored = assertNotNull(AdbIdentityCodec.decode(AdbIdentityCodec.encode(original)))
        assertEquals(AdbAuth.fingerprint(original.publicKey), AdbAuth.fingerprint(restored.publicKey))
        val token = ByteArray(AdbAuth.TOKEN_BYTES) { it.toByte() }
        assertTrue(AdbAuth.verifyTokenSignature(original.publicKey, token, AdbAuth.signToken(restored, token)))
    }

    @Test
    fun `bytes that are not a key are refused rather than thrown`() {
        assertNull(AdbIdentityCodec.decode(ByteArray(0)))
        assertNull(AdbIdentityCodec.decode(byteArrayOf(1, 2, 3)))
        val truncated = AdbIdentityCodec.encode(AdbAuth.generateIdentity()).copyOf(64)
        assertNull(AdbIdentityCodec.decode(truncated))
    }
}
