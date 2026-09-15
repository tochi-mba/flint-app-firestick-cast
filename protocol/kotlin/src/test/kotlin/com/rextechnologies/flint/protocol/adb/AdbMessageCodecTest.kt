package com.rextechnologies.flint.protocol.adb

import com.rextechnologies.flint.protocol.BinaryData
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.EOFException
import java.io.InputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNull

class AdbMessageCodecTest {
    @Test
    fun `command encodes printable little endian ASCII`() {
        val command = AdbCommand.fromAscii("CNXN")
        assertEquals("CNXN", command.ascii)
        assertEquals("CNXN", command.toString())
        assertEquals(0x4e584e43, command.rawValue)
        listOf("", "ABC", "ABCDE", "A\nCD", "Ã©BCD").forEach {
            assertFailsWith<IllegalArgumentException> { AdbCommand.fromAscii(it) }
        }
    }

    @Test
    fun `message factories validate identifiers and add protocol nul terminators`() {
        val connect = AdbMessage.connect("host::features=cmd", 7, 4096)
        assertEquals(AdbCommands.CNXN, connect.command)
        assertEquals(7, connect.argument0)
        assertEquals(4096, connect.argument1)
        assertContentEquals("host::features=cmd\u0000".toByteArray(), connect.payload.toByteArray())

        val open = AdbMessage.open(1, "shell:id")
        assertContentEquals("shell:id\u0000".toByteArray(), open.payload.toByteArray())
        assertEquals(AdbCommands.WRTE, AdbMessage.write(1, 2, byteArrayOf(9)).command)
        assertEquals(AdbCommands.OKAY, AdbMessage.okay(1, 2).command)
        assertEquals(AdbCommands.CLSE, AdbMessage.close(1, 2).command)

        assertFailsWith<IllegalArgumentException> { AdbMessage.connect("bad\u0000id") }
        assertFailsWith<IllegalArgumentException> { AdbMessage.open(0, "shell:id") }
        assertFailsWith<IllegalArgumentException> { AdbMessage.open(1, "") }
        assertFailsWith<IllegalArgumentException> { AdbMessage.open(1, "bad\u0000") }
        assertFailsWith<IllegalArgumentException> { AdbMessage(AdbCommands.OKAY, -1) }
        assertFailsWith<IllegalArgumentException> { AdbMessage(AdbCommands.OKAY, argument1 = 0x1_0000_0000L) }
    }

    @Test
    fun `transport header and payload round trip with checksum and magic`() {
        val message = AdbMessage.write(0x01020304, 0xfedcba98, byteArrayOf(1, 2, 0xff.toByte()))
        val encoded = AdbMessageCodec.encode(message)
        assertEquals(AdbMessageCodec.HEADER_LENGTH + 3, encoded.size)
        val header = ByteBuffer.wrap(encoded).order(ByteOrder.LITTLE_ENDIAN)
        assertEquals(AdbCommands.WRTE.rawValue, header.int)
        assertEquals(0x01020304, header.int)
        assertEquals(0xfedcba98.toInt(), header.int)
        assertEquals(3, header.int)
        assertEquals(258, header.int)
        assertEquals(AdbCommands.WRTE.rawValue xor -1, header.int)
        assertEquals(message, AdbMessageCodec.decode(encoded))
        assertEquals(258, AdbMessageCodec.checksum(message.payload))
    }

    @Test
    fun `stream reader handles consecutive frames clean eof and zero-length read`() {
        val output = ByteArrayOutputStream()
        val first = AdbMessage.okay(1, 2)
        val second = AdbMessage.write(1, 2, byteArrayOf(3, 4))
        AdbMessageCodec.writeTo(output, first)
        AdbMessageCodec.writeTo(output, second)
        val input = ByteArrayInputStream(output.toByteArray())
        assertEquals(first, AdbMessageCodec.readFrom(input))
        assertEquals(second, AdbMessageCodec.readFrom(input))
        assertNull(AdbMessageCodec.readFrom(input))
        assertEquals(second, AdbMessageCodec.readFrom(ZeroThenDataInputStream(AdbMessageCodec.encode(second))))
    }

    @Test
    fun `decoder rejects missing trailing truncated corrupt and oversized messages`() {
        assertFailsWith<AdbFormatException> { AdbMessageCodec.decode(byteArrayOf()) }
        val valid = AdbMessageCodec.encode(AdbMessage.write(1, 2, byteArrayOf(1, 2, 3)))
        assertFailsWith<AdbFormatException> { AdbMessageCodec.decode(valid + 0) }
        assertFailsWith<EOFException> { AdbMessageCodec.decode(valid.copyOf(10)) }
        assertFailsWith<EOFException> { AdbMessageCodec.decode(valid.copyOf(valid.size - 1)) }
        assertFailsWith<AdbFormatException> {
            AdbMessageCodec.decode(valid.copyOf().also { it[20] = (it[20].toInt() xor 1).toByte() })
        }
        assertFailsWith<AdbFormatException> {
            AdbMessageCodec.decode(valid.copyOf().also { it[16] = (it[16].toInt() xor 1).toByte() })
        }
        val oversizedHeader = valid.copyOf(24).also {
            ByteBuffer.wrap(it).order(ByteOrder.LITTLE_ENDIAN).putInt(12, AdbMessageCodec.MAX_PAYLOAD_LENGTH + 1)
        }
        assertFailsWith<AdbFormatException> { AdbMessageCodec.decode(oversizedHeader) }
        assertFailsWith<AdbFormatException> {
            AdbMessageCodec.encode(
                AdbMessage(
                    AdbCommands.WRTE,
                    payload = BinaryData.of(ByteArray(AdbMessageCodec.MAX_PAYLOAD_LENGTH + 1)),
                ),
            )
        }
    }

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
