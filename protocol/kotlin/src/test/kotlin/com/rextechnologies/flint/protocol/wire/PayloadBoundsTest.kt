package com.rextechnologies.flint.protocol.wire

import com.rextechnologies.flint.protocol.BinaryData
import java.io.ByteArrayOutputStream
import java.io.DataOutputStream
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith

/**
 * The guards that decide how much memory a stranger's length field may claim.
 *
 * Every one of these reads a number off the wire and allocates from it. They were written with
 * bounds and never tested against a number outside them, which is the one case they exist for: a
 * television that trusts a four-byte length allocates two gigabytes and dies.
 */
class PayloadBoundsTest {
    @Test
    fun `the writer refuses values its field cannot hold`() {
        val writer = PayloadWriter()

        assertFailsWith<WireFormatException> { writer.u8(-1) }
        assertFailsWith<WireFormatException> { writer.u8(0x100) }
        assertFailsWith<WireFormatException> { writer.u16(-1) }
        assertFailsWith<WireFormatException> { writer.u16(0x1_0000) }
        assertFailsWith<WireFormatException> { writer.utf8U16("x".repeat(11), 10) }
        assertFailsWith<WireFormatException> { writer.utf8I32("x".repeat(11), 10) }
        assertFailsWith<WireFormatException> { writer.utf8U32("x".repeat(11), 10) }
        assertFailsWith<WireFormatException> { writer.binary(BinaryData.of(ByteArray(11)), 10) }
    }

    @Test
    fun `a length longer than the payload is refused before anything is allocated`() {
        // Each of these claims far more bytes than follow it. The guard must reject on the
        // number, not by trying to read and running out.
        val hugeUtf8U16 = bytes {
            writeShort(0xffff)
            write(byteArrayOf(1, 2, 3))
        }
        assertFailsWith<WireFormatException> { PayloadReader(hugeUtf8U16).utf8U16(0xffff, "caption") }

        val hugeUtf8I32 = bytes {
            writeInt(Int.MAX_VALUE)
            write(byteArrayOf(1, 2, 3))
        }
        assertFailsWith<WireFormatException> { PayloadReader(hugeUtf8I32).utf8I32(1024, "text") }

        val negativeUtf8I32 = bytes { writeInt(-1) }
        assertFailsWith<WireFormatException> { PayloadReader(negativeUtf8I32).utf8I32(1024, "text") }

        val hugeUtf8U32 = bytes { writeInt(-1) }
        assertFailsWith<WireFormatException> { PayloadReader(hugeUtf8U32).utf8U32(1024, "config") }

        val hugeBinary = bytes {
            writeInt(Int.MAX_VALUE)
            write(byteArrayOf(1, 2, 3))
        }
        assertFailsWith<WireFormatException> { PayloadReader(hugeBinary).binary(1024, "payload") }

        val negativeBinary = bytes { writeInt(-1) }
        assertFailsWith<WireFormatException> { PayloadReader(negativeBinary).binary(1024, "payload") }

        val overMaximumBinary = bytes {
            writeInt(2048)
            write(ByteArray(2048))
        }
        assertFailsWith<WireFormatException> { PayloadReader(overMaximumBinary).binary(1024, "payload") }
    }

    @Test
    fun `a truncated field is refused rather than read short`() {
        val truncated = bytes {
            writeShort(8)
            write(byteArrayOf(1, 2))
        }
        assertFailsWith<WireFormatException> { PayloadReader(truncated).utf8U16(1024, "caption") }

        assertFailsWith<Throwable> { PayloadReader(ByteArray(0)).u8("empty") }
        assertFailsWith<Throwable> { PayloadReader(byteArrayOf(1)).i32("short") }
    }

    @Test
    fun `a payload with bytes left over is refused`() {
        val reader = PayloadReader(byteArrayOf(1, 2))
        assertEquals(1, reader.u8("first"))
        assertFailsWith<WireFormatException> { reader.requireFinished() }
    }

    @Test
    fun `a browser surface needs version two and an unsupported version is named`() {
        val browser = WireCodec.encode(WireFrame(2, SurfaceMessage(SurfaceMode.BROWSER, "Browser")))
        val payload = browser.copyOfRange(12, browser.size)

        assertFailsWith<WireFormatException> { WireMessageCodec.decode(1, WireMessageType.SURFACE.id, payload) }
        assertFailsWith<UnsupportedProtocolVersionException> {
            WireMessageCodec.decode(ProtocolVersion.CURRENT + 1, WireMessageType.SURFACE.id, payload)
        }
        assertFailsWith<WireFormatException> {
            WireCodec.encode(WireFrame(1, SurfaceMessage(SurfaceMode.BROWSER, "Browser")))
        }
    }

    @Test
    fun `workspace resizing is refused below protocol version four`() {
        val message = BrowserWorkspaceResizeMessage(4, 150, 140, 7000, 3000)
        val encoded = WireCodec.encode(WireFrame(ProtocolVersion.CURRENT, message))
        val payload = encoded.copyOfRange(12, encoded.size)

        assertFailsWith<WireFormatException> { WireMessageCodec.decode(3, message.typeId, payload) }
        assertFailsWith<WireFormatException> { WireMessageCodec.decode(2, message.typeId, payload) }
    }

    private fun bytes(block: DataOutputStream.() -> Unit): ByteArray {
        val buffer = ByteArrayOutputStream()
        DataOutputStream(buffer).use(block)
        return buffer.toByteArray()
    }
}
