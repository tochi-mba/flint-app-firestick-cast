package com.rextechnologies.flint.protocol.wire

import com.rextechnologies.flint.protocol.BinaryData
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.EOFException
import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertIs
import kotlin.test.assertNull
import kotlin.test.assertTrue

class WireCodecTest {
    @Test
    fun `every version-one message round trips`() {
        val messages = listOf<WireMessage>(
            HelloMessage(1, 3, "Living Room ðŸ“º", setOf(CodecId.H264, CodecId.H265), 3840, 2160, 320),
            AuthMessage(AuthMethod.PAIRING_CODE, bytes("012345")),
            AuthMessage(AuthMethod.SESSION_TOKEN, bytes("token"), "sha256:01:02"),
            AuthMessage(AuthMethod.PUBLIC_KEY_PROOF, bytes("proof")),
            VideoConfigMessage(CodecId.H265, 1920, 1080, listOf(bytes(1, 2), bytes(3, 4, 5))),
            VideoPacket(9_876_543, true, bytes(0, 0, 1, 0x65)),
            VideoPacket(9_900_000, false, bytes(0x41, 0x42)),
            AudioConfigMessage(CodecId.AAC_LC, 48_000, 2, bytes(0x12, 0x10)),
            AudioConfigMessage(CodecId.OPUS, 48_000, 6),
            AudioPacket(9_876_000, bytes(9, 8, 7)),
            ControlMessage(1, TransportControl(TransportAction.PLAY)),
            ControlMessage(2, TransportControl(TransportAction.SEEK_TO, 123_456)),
            ControlMessage(3, PointerControl(PointerAction.MOVE, 0.25f, 0.75f, 1)),
            ControlMessage(4, KeyControl(KeyAction.DOWN, 23)),
            ControlMessage(5, TextControl("Hello, TV ðŸ‘‹")),
            ControlMessage(6, VolumeControl(0.42f)),
            StatsMessage(3, 17_000, 8_000, 12),
            ByeMessage(ByeReason.NORMAL),
            ByeMessage(ByeReason.PROTOCOL_ERROR, "malformed packet"),
        )

        messages.forEachIndexed { index, message ->
            val original = WireFrame(1, message, flags = index)
            assertEquals(original, WireCodec.decode(WireCodec.encode(original)), message.toString())
        }
    }

    @Test
    fun `unknown message and flags survive a future-version round trip`() {
        val original = WireFrame(99, UnknownMessage(0xfefe, bytes(1, 2, 3)), flags = 0xabcd)
        val decoded = WireCodec.decode(WireCodec.encode(original))
        assertEquals(original, decoded)
        assertContentEquals(byteArrayOf(1, 2, 3), (decoded.message as UnknownMessage).payload.toByteArray())
    }

    @Test
    fun `stream reader reads consecutive frames and clean eof`() {
        val stream = ByteArrayOutputStream()
        WireCodec.writeTo(stream, WireFrame(1, ByeMessage(ByeReason.NORMAL)))
        WireCodec.writeTo(stream, WireFrame(1, StatsMessage(0, 0, 0, 0)))
        val input = ByteArrayInputStream(stream.toByteArray())
        assertIs<ByeMessage>(WireCodec.readFrom(input)?.message)
        assertIs<StatsMessage>(WireCodec.readFrom(input)?.message)
        assertNull(WireCodec.readFrom(input))
    }

    @Test
    fun `version negotiation chooses highest overlap`() {
        val remote = HelloMessage(2, 4, "TV", setOf(CodecId.H264), 1280, 720, 240)
        assertEquals(3, VersionNegotiator.negotiate(1, 3, remote))
        assertNull(VersionNegotiator.negotiate(5, 6, remote))
        assertFailsWith<IllegalArgumentException> { VersionNegotiator.negotiate(3, 2, remote) }
    }

    @Test
    fun `unsupported known payload version is rejected`() {
        val message = ByeMessage(ByeReason.NORMAL)
        assertFailsWith<UnsupportedProtocolVersionException> {
            WireCodec.encode(WireFrame(ProtocolVersion.CURRENT + 1, message))
        }
        val raw = rawFrame(ProtocolVersion.CURRENT + 1, WireMessageType.BYE.id, byteArrayOf(1, 0, 0))
        assertFailsWith<UnsupportedProtocolVersionException> { WireCodec.decode(raw) }
    }

    @Test
    fun `envelope rejects invalid lengths magic identifiers truncation and trailing bytes`() {
        assertFailsWith<WireFormatException> { WireCodec.decode(byteArrayOf()) }
        assertFailsWith<EOFException> { WireCodec.decode(byteArrayOf(0, 0)) }
        assertFailsWith<WireFormatException> { WireCodec.decode(intBytes(7) + ByteArray(7)) }
        assertFailsWith<WireFormatException> {
            WireCodec.decode(intBytes(WireCodec.MAX_FRAME_LENGTH + 1) + ByteArray(8))
        }
        assertFailsWith<EOFException> { WireCodec.decode(intBytes(8) + ByteArray(7)) }

        val valid = WireCodec.encode(WireFrame(1, ByeMessage(ByeReason.NORMAL)))
        assertFailsWith<WireFormatException> { WireCodec.decode(valid + 0) }
        assertFailsWith<WireFormatException> { WireCodec.decode(valid.copyOf().also { it[4] = 0 }) }
        assertFailsWith<WireFormatException> {
            WireCodec.decode(
                valid.copyOf().also {
                    it[6] = 0
                    it[7] = 0
                },
            )
        }
        assertFailsWith<WireFormatException> {
            WireCodec.decode(
                valid.copyOf().also {
                    it[8] = 0
                    it[9] = 0
                },
            )
        }
    }

    @Test
    fun `known payloads reject trailing malformed and invalid fields`() {
        val validBye = WireCodec.encode(WireFrame(1, ByeMessage(ByeReason.NORMAL)))
        val withPayloadTrailing = validBye.copyOf(validBye.size + 1).also {
            ByteBuffer.wrap(it).putInt(it.size - 4)
        }
        assertFailsWith<WireFormatException> { WireCodec.decode(withPayloadTrailing) }

        assertFailsWith<WireFormatException> {
            WireCodec.decode(rawFrame(1, WireMessageType.BYE.id, byteArrayOf(99.toByte(), 0, 0)))
        }
        assertFailsWith<WireFormatException> {
            WireCodec.decode(rawFrame(1, WireMessageType.AUTH.id, byteArrayOf(99.toByte())))
        }
        assertFailsWith<WireFormatException> {
            WireCodec.decode(rawFrame(1, WireMessageType.VIDEO.id, ByteArray(9)))
        }

        val invalidUtf8Hello = ByteArrayOutputStream().apply {
            write(byteArrayOf(0, 1, 0, 1, 0, 1, 0xc3.toByte()))
            write(byteArrayOf(0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1))
        }.toByteArray()
        assertFailsWith<WireFormatException> {
            WireCodec.decode(rawFrame(1, WireMessageType.HELLO.id, invalidUtf8Hello))
        }

        val unknownControl = ByteBuffer.allocate(9).putLong(0).put(99.toByte()).array()
        assertFailsWith<WireFormatException> {
            WireCodec.decode(rawFrame(1, WireMessageType.CONTROL.id, unknownControl))
        }
    }

    @Test
    fun `oversize frame and fields are rejected before writing`() {
        assertFailsWith<WireFormatException> {
            WireCodec.encode(WireFrame(1, HelloMessage(1, 1, "x".repeat(256), emptySet(), 1, 1, 1)))
        }
        assertFailsWith<WireFormatException> {
            WireCodec.encode(
                WireFrame(
                    44,
                    UnknownMessage(1000, BinaryData.of(ByteArray(WireCodec.MAX_FRAME_LENGTH))),
                ),
            )
        }
    }

    @Test
    fun `binary data copies inputs and uses content identity`() {
        val source = byteArrayOf(1, 2, 3)
        val data = BinaryData.of(source)
        source[0] = 9
        assertEquals(BinaryData.of(byteArrayOf(1, 2, 3)), data)
        assertEquals(BinaryData.of(byteArrayOf(1, 2, 3)).hashCode(), data.hashCode())
        assertTrue(data.toString().contains("3 bytes"))
        val output = data.toByteArray()
        output[1] = 9
        assertContentEquals(byteArrayOf(1, 2, 3), data.toByteArray())
        assertTrue(BinaryData.EMPTY.isEmpty)
    }

    private fun bytes(vararg values: Int): BinaryData =
        BinaryData.of(ByteArray(values.size) { values[it].toByte() })

    private fun bytes(value: String): BinaryData = BinaryData.of(value.toByteArray())

    private fun intBytes(value: Int): ByteArray = ByteBuffer.allocate(4).putInt(value).array()

    private fun rawFrame(version: Int, type: Int, payload: ByteArray): ByteArray =
        ByteBuffer.allocate(12 + payload.size).order(ByteOrder.BIG_ENDIAN)
            .putInt(8 + payload.size)
            .putShort(0x5243.toShort())
            .putShort(version.toShort())
            .putShort(type.toShort())
            .putShort(0)
            .put(payload)
            .array()
}
