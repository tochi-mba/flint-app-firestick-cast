package com.rextechnologies.flint.protocol.wire

import com.rextechnologies.flint.protocol.BinaryData
import java.io.ByteArrayInputStream
import java.io.InputStream
import java.nio.ByteBuffer
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class WireModelValidationTest {
    private val data = BinaryData.of(byteArrayOf(1))

    @Test
    fun `identifier lookups preserve known and unknown values`() {
        WireMessageType.entries.forEach { assertEquals(it, WireMessageType.fromId(it.id)) }
        AuthMethod.entries.forEach { assertEquals(it, AuthMethod.fromId(it.id)) }
        TransportAction.entries.forEach { assertEquals(it, TransportAction.fromId(it.id)) }
        PointerAction.entries.forEach { assertEquals(it, PointerAction.fromId(it.id)) }
        KeyAction.entries.forEach { assertEquals(it, KeyAction.fromId(it.id)) }
        ByeReason.entries.forEach { assertEquals(it, ByeReason.fromId(it.id)) }
        assertNull(WireMessageType.fromId(999))
        assertNull(AuthMethod.fromId(999))
        assertNull(TransportAction.fromId(999))
        assertNull(PointerAction.fromId(999))
        assertNull(KeyAction.fromId(999))
        assertNull(ByeReason.fromId(999))
        assertEquals(CodecId(500), CodecId(500))
        assertFailsWith<IllegalArgumentException> { CodecId(0) }
        assertFailsWith<IllegalArgumentException> { CodecId(65_536) }
    }

    @Test
    fun `hello auth and config models reject nonsensical values`() {
        assertFailsWith<IllegalArgumentException> { HelloMessage(0, 1, "TV", emptySet(), 1, 1, 1) }
        assertFailsWith<IllegalArgumentException> { HelloMessage(2, 1, "TV", emptySet(), 1, 1, 1) }
        assertFailsWith<IllegalArgumentException> { HelloMessage(1, 1, " ", emptySet(), 1, 1, 1) }
        assertFailsWith<IllegalArgumentException> { HelloMessage(1, 1, "TV", emptySet(), 0, 1, 1) }
        assertFailsWith<IllegalArgumentException> { HelloMessage(1, 1, "TV", emptySet(), 1, 0, 1) }
        assertFailsWith<IllegalArgumentException> { HelloMessage(1, 1, "TV", emptySet(), 1, 1, 0) }
        assertFailsWith<IllegalArgumentException> { AuthMessage(AuthMethod.PAIRING_CODE, BinaryData.EMPTY) }
        assertFailsWith<IllegalArgumentException> { AuthMessage(AuthMethod.PAIRING_CODE, data, " ") }
        assertFailsWith<IllegalArgumentException> { VideoConfigMessage(CodecId.H264, 0, 1, emptyList()) }
        assertFailsWith<IllegalArgumentException> { VideoConfigMessage(CodecId.H264, 1, 0, emptyList()) }
        assertFailsWith<IllegalArgumentException> { VideoConfigMessage(CodecId.H264, 1, 1, List(17) { data }) }
        assertFailsWith<IllegalArgumentException> { AudioConfigMessage(CodecId.AAC_LC, 0, 1) }
        assertFailsWith<IllegalArgumentException> { AudioConfigMessage(CodecId.AAC_LC, 768_001, 1) }
        assertFailsWith<IllegalArgumentException> { AudioConfigMessage(CodecId.AAC_LC, 48_000, 0) }
        assertFailsWith<IllegalArgumentException> { AudioConfigMessage(CodecId.AAC_LC, 48_000, 33) }
    }

    @Test
    fun `packet control and stats models enforce timing and finite ranges`() {
        assertFailsWith<IllegalArgumentException> { VideoPacket(-1, false, data) }
        assertFailsWith<IllegalArgumentException> { VideoPacket(0, false, BinaryData.EMPTY) }
        assertFailsWith<IllegalArgumentException> { AudioPacket(-1, data) }
        assertFailsWith<IllegalArgumentException> { AudioPacket(0, BinaryData.EMPTY) }
        assertFailsWith<IllegalArgumentException> { TransportControl(TransportAction.PLAY, 0) }
        assertFailsWith<IllegalArgumentException> { TransportControl(TransportAction.SEEK_TO, -1) }
        assertFailsWith<IllegalArgumentException> { PointerControl(PointerAction.MOVE, Float.NaN, 0f) }
        assertFailsWith<IllegalArgumentException> { PointerControl(PointerAction.MOVE, 0f, Float.POSITIVE_INFINITY) }
        assertFailsWith<IllegalArgumentException> { KeyControl(KeyAction.DOWN, -1) }
        assertFailsWith<IllegalArgumentException> { VolumeControl(-0.01f) }
        assertFailsWith<IllegalArgumentException> { VolumeControl(1.01f) }
        assertFailsWith<IllegalArgumentException> { VolumeControl(Float.NaN) }
        assertFailsWith<IllegalArgumentException> { ControlMessage(-1, TextControl("x")) }
        assertFailsWith<IllegalArgumentException> { StatsMessage(-1, 0, 0, 0) }
        assertFailsWith<IllegalArgumentException> { StatsMessage(0, -1, 0, 0) }
        assertFailsWith<IllegalArgumentException> { StatsMessage(0, 0, -1, 0) }
        assertFailsWith<IllegalArgumentException> { StatsMessage(0, 0, 0, -1) }
    }

    @Test
    fun `unknown and frame models cannot impersonate known or invalid identifiers`() {
        assertFailsWith<IllegalArgumentException> { UnknownMessage(0, BinaryData.EMPTY) }
        assertFailsWith<IllegalArgumentException> { UnknownMessage(65_536, BinaryData.EMPTY) }
        assertFailsWith<IllegalArgumentException> { UnknownMessage(WireMessageType.HELLO.id, BinaryData.EMPTY) }
        assertFailsWith<IllegalArgumentException> { WireFrame(0, ByeMessage(ByeReason.NORMAL)) }
        assertFailsWith<IllegalArgumentException> { WireFrame(1, ByeMessage(ByeReason.NORMAL), -1) }
        assertFailsWith<IllegalArgumentException> { WireFrame(1, ByeMessage(ByeReason.NORMAL), 65_536) }
    }

    @Test
    fun `payload decoder rejects every invalid discriminant and bound`() {
        assertFailsWith<WireFormatException> {
            WireMessageCodec.decode(
                1,
                WireMessageType.HELLO.id,
                buffer(2 + 2 + 2 + 2 + 12) {
                    putShort(1)
                    putShort(1)
                    putShort(0)
                    putShort(257)
                    putInt(1)
                    putInt(1)
                    putInt(1)
                },
            )
        }
        assertFailsWith<WireFormatException> {
            WireMessageCodec.decode(
                1,
                WireMessageType.VIDEO_CONFIG.id,
                buffer(11) {
                    putShort(1)
                    putInt(1)
                    putInt(1)
                    put(17)
                },
            )
        }
        assertFailsWith<WireFormatException> {
            WireMessageCodec.decode(1, WireMessageType.AUTH.id, byteArrayOf(99))
        }
        val invalidPresence = buffer(7) {
            put(1)
            putInt(1)
            put(1)
            put(2)
        }
        assertFailsWith<WireFormatException> { WireMessageCodec.decode(1, WireMessageType.AUTH.id, invalidPresence) }
        val invalidBoolean = buffer(13) {
            putLong(0)
            put(2)
            putInt(0)
        }
        assertFailsWith<WireFormatException> { WireMessageCodec.decode(1, WireMessageType.VIDEO.id, invalidBoolean) }
        val negativeBinary = buffer(12) {
            putLong(0)
            putInt(-1)
        }
        assertFailsWith<WireFormatException> { WireMessageCodec.decode(1, WireMessageType.AUDIO.id, negativeBinary) }
        val truncatedBinary = buffer(13) {
            putLong(0)
            putInt(2)
            put(1)
        }
        assertFailsWith<WireFormatException> { WireMessageCodec.decode(1, WireMessageType.AUDIO.id, truncatedBinary) }

        listOf(
            control(1, 99, byteArrayOf()),
            control(2, 99, ByteArray(12)),
            control(3, 99, ByteArray(4)),
        ).forEach {
            assertFailsWith<WireFormatException> { WireMessageCodec.decode(1, WireMessageType.CONTROL.id, it) }
        }
        val invalidText = buffer(8 + 1 + 4 + 1) {
            putLong(0)
            put(4)
            putInt(1)
            put(0xc3.toByte())
        }
        assertFailsWith<WireFormatException> { WireMessageCodec.decode(1, WireMessageType.CONTROL.id, invalidText) }
        val invalidVolume = buffer(8 + 1 + 4) {
            putLong(0)
            put(5)
            putFloat(Float.NaN)
        }
        assertFailsWith<WireFormatException> { WireMessageCodec.decode(1, WireMessageType.CONTROL.id, invalidVolume) }
    }

    @Test
    fun `oversize known payload fields fail before frame allocation`() {
        assertFailsWith<WireFormatException> {
            WireCodec.encode(WireFrame(1, AuthMessage(AuthMethod.PUBLIC_KEY_PROOF, BinaryData.of(ByteArray(4097)))))
        }
        assertFailsWith<WireFormatException> {
            WireCodec.encode(WireFrame(1, AuthMessage(AuthMethod.PUBLIC_KEY_PROOF, data, "x".repeat(513))))
        }
        assertFailsWith<WireFormatException> {
            WireCodec.encode(
                WireFrame(1, VideoConfigMessage(CodecId.H264, 1, 1, listOf(BinaryData.of(ByteArray(1_048_577))))),
            )
        }
        assertFailsWith<WireFormatException> {
            WireCodec.encode(WireFrame(1, ControlMessage(1, TextControl("x".repeat(16_385)))))
        }
        assertFailsWith<WireFormatException> {
            WireCodec.encode(WireFrame(1, ByeMessage(ByeReason.NORMAL, "x".repeat(1_025))))
        }
    }

    @Test
    fun `stream reader tolerates legal zero-byte bulk reads`() {
        val frame = WireFrame(1, ByeMessage(ByeReason.NORMAL))
        assertEquals(frame, WireCodec.readFrom(ZeroThenDataInputStream(WireCodec.encode(frame))))
    }

    private fun control(eventId: Int, action: Int, remainder: ByteArray): ByteArray =
        buffer(8 + 1 + 1 + remainder.size) {
            putLong(0)
            put(eventId.toByte())
            put(action.toByte())
            put(remainder)
        }

    private fun buffer(size: Int, write: ByteBuffer.() -> Unit): ByteArray =
        ByteBuffer.allocate(size).apply(write).array()

    private class ZeroThenDataInputStream(bytes: ByteArray) : InputStream() {
        private val delegate = ByteArrayInputStream(bytes)
        private var returnedZero = false
        override fun read(): Int = delegate.read()
        override fun read(destination: ByteArray, offset: Int, length: Int): Int {
            if (!returnedZero) {
                returnedZero = true
                return 0
            }
            return delegate.read(destination, offset, length)
        }
    }
}
