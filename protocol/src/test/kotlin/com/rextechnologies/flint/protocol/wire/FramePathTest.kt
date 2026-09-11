package com.rextechnologies.flint.protocol.wire

import com.rextechnologies.flint.protocol.BinaryData
import java.io.ByteArrayOutputStream
import java.nio.ByteBuffer
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertTrue

/**
 * The allocation-free path must produce exactly the bytes the ordinary codec produces.
 *
 * This is the test that makes the fast path safe to use. It is a second way of writing one format,
 * not a second format, and the moment the two disagree the receiver is being sent something the
 * golden corpus never described.
 */
class FrameWriterTest {
    private fun reference(frame: WireFrame): ByteArray = WireCodec.encode(frame)

    private fun fast(block: (FrameWriter, ByteArrayOutputStream) -> Unit): ByteArray {
        val output = ByteArrayOutputStream()
        block(FrameWriter(), output)
        return output.toByteArray()
    }

    @Test
    fun `a video packet is byte-identical to the one the codec writes`() {
        val payload = ByteArray(4_096) { (it * 31).toByte() }
        listOf(true, false).forEach { keyFrame ->
            (ProtocolVersion.MIN_SUPPORTED..ProtocolVersion.CURRENT).forEach { version ->
                val expected = reference(
                    WireFrame(version, VideoPacket(1_234_567, keyFrame, BinaryData.of(payload))),
                )
                val actual = fast { writer, output ->
                    writer.writeVideoPacket(output, version, 1_234_567, keyFrame, payload, 0, payload.size)
                }
                assertContentEquals(expected, actual, "version $version keyFrame $keyFrame")
            }
        }
    }

    @Test
    fun `a slice of a larger buffer is written without copying the whole thing`() {
        val backing = ByteArray(1_000) { it.toByte() }
        val expected = reference(
            WireFrame(4, VideoPacket(9, true, BinaryData.of(backing.copyOfRange(100, 300)))),
        )
        val actual = fast { writer, output ->
            writer.writeVideoPacket(output, 4, 9, true, backing, 100, 200)
        }
        assertContentEquals(expected, actual)
    }

    @Test
    fun `a direct buffer produces the same bytes as an array`() {
        val payload = ByteArray(200_000) { (it % 251).toByte() }
        val expected = reference(WireFrame(4, VideoPacket(42, false, BinaryData.of(payload))))
        val actual = fast { writer, output ->
            writer.writeVideoPacket(output, 4, 42, false, ByteBuffer.wrap(payload))
        }
        assertContentEquals(expected, actual)
    }

    @Test
    fun `a direct buffer longer than one scratch block is drained in full`() {
        // The drain loop is the only place a copy survives, and a payload that spans several blocks
        // is where an off-by-one in it would show.
        val payload = ByteArray(64 * 1024 * 3 + 17) { (it % 97).toByte() }
        val expected = reference(WireFrame(4, VideoPacket(1, true, BinaryData.of(payload))))
        val actual = fast { writer, output ->
            writer.writeVideoPacket(output, 4, 1, true, ByteBuffer.wrap(payload))
        }
        assertContentEquals(expected, actual)
    }

    @Test
    fun `an audio packet is byte-identical too`() {
        val payload = ByteArray(512) { (it * 7).toByte() }
        val expected = reference(WireFrame(3, AudioPacket(77, BinaryData.of(payload))))
        val actual = fast { writer, output ->
            writer.writeAudioPacket(output, 3, 77, payload, 0, payload.size)
        }
        assertContentEquals(expected, actual)
    }

    @Test
    fun `what it writes is what the decoder reads back`() {
        val payload = ByteArray(1_024) { (it * 13).toByte() }
        val encoded = fast { writer, output ->
            writer.writeVideoPacket(output, 4, 555, true, payload, 0, payload.size)
        }
        val frame = WireCodec.decode(encoded)
        assertEquals(4, frame.protocolVersion)
        val packet = frame.message as VideoPacket
        assertEquals(555, packet.presentationTimeUs)
        assertTrue(packet.keyFrame)
        assertContentEquals(payload, packet.data.toByteArray())
    }

    @Test
    fun `one writer serves a whole session`() {
        // The header array is reused between frames; a second frame must not inherit the first's.
        val writer = FrameWriter()
        val output = ByteArrayOutputStream()
        val first = ByteArray(16) { 1 }
        val second = ByteArray(32) { 2 }
        writer.writeVideoPacket(output, 4, 1, true, first, 0, first.size)
        writer.writeVideoPacket(output, 4, 2, false, second, 0, second.size)

        val expected = ByteArrayOutputStream().apply {
            write(reference(WireFrame(4, VideoPacket(1, true, BinaryData.of(first)))))
            write(reference(WireFrame(4, VideoPacket(2, false, BinaryData.of(second)))))
        }.toByteArray()
        assertContentEquals(expected, output.toByteArray())
    }

    @Test
    fun `an unsupported version is refused rather than written`() {
        assertFailsWith<UnsupportedProtocolVersionException> {
            fast { writer, output -> writer.writeVideoPacket(output, 0, 1, true, ByteArray(4), 0, 4) }
        }
        assertFailsWith<UnsupportedProtocolVersionException> {
            fast { writer, output ->
                writer.writeVideoPacket(output, ProtocolVersion.CURRENT + 1, 1, true, ByteArray(4), 0, 4)
            }
        }
    }

    @Test
    fun `a range outside the buffer is refused`() {
        assertFailsWith<IllegalArgumentException> {
            fast { writer, output -> writer.writeVideoPacket(output, 4, 1, true, ByteArray(4), 2, 4) }
        }
        assertFailsWith<IllegalArgumentException> {
            fast { writer, output -> writer.writeVideoPacket(output, 4, 1, true, ByteArray(4), -1, 2) }
        }
    }

    @Test
    fun `a packet with nothing in it is refused, exactly as the model refuses it`() {
        assertFailsWith<IllegalArgumentException> {
            fast { writer, output -> writer.writeVideoPacket(output, 4, 1, true, ByteArray(0), 0, 0) }
        }
        assertFailsWith<IllegalArgumentException> {
            fast { writer, output -> writer.writeAudioPacket(output, 4, 1, ByteArray(0), 0, 0) }
        }
        assertFailsWith<IllegalArgumentException> {
            fast { writer, output ->
                writer.writeVideoPacket(output, 4, 1, true, ByteBuffer.allocate(0))
            }
        }
    }

    @Test
    fun `a negative presentation time is refused, exactly as the model refuses it`() {
        assertFailsWith<IllegalArgumentException> {
            fast { writer, output -> writer.writeVideoPacket(output, 4, -1, true, ByteArray(4), 0, 4) }
        }
        assertFailsWith<IllegalArgumentException> {
            fast { writer, output -> writer.writeAudioPacket(output, 4, -1, ByteArray(4), 0, 4) }
        }
    }

    @Test
    fun `a packet too large for the wire is refused before anything is written`() {
        // The buffer is real rather than a claimed length: the range check runs first, so a short
        // buffer with a long length would fail for the wrong reason and prove nothing about the cap.
        val oversized = ByteArray(15 * 1024 * 1024 + 1)
        assertFailsWith<WireFormatException> {
            fast { writer, output ->
                writer.writeVideoPacket(output, 4, 1, true, oversized, 0, oversized.size)
            }
        }
    }
}
