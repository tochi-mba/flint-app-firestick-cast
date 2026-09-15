package com.rextechnologies.flint.protocol.adb

import com.rextechnologies.flint.protocol.BinaryData
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.EOFException
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream

open class AdbFormatException(message: String, cause: Throwable? = null) : IOException(message, cause)

/** The 24-byte little-endian ADB transport header and its payload. */
object AdbMessageCodec {
    const val HEADER_LENGTH: Int = 24
    const val MAX_PAYLOAD_LENGTH: Int = 16 * 1024 * 1024

    fun encode(message: AdbMessage): ByteArray {
        val output = ByteArrayOutputStream(HEADER_LENGTH + message.payload.size)
        writeTo(output, message)
        return output.toByteArray()
    }

    fun writeTo(output: OutputStream, message: AdbMessage) {
        if (message.payload.size > MAX_PAYLOAD_LENGTH) {
            throw AdbFormatException("ADB payload is too large: ${message.payload.size}")
        }
        writeIntLe(output, message.command.rawValue)
        writeIntLe(output, message.argument0.toInt())
        writeIntLe(output, message.argument1.toInt())
        writeIntLe(output, message.payload.size)
        writeIntLe(output, checksum(message.payload).toInt())
        writeIntLe(output, message.command.rawValue xor -1)
        message.payload.withBytes { output.write(it) }
    }

    fun decode(encoded: ByteArray): AdbMessage {
        val input = ByteArrayInputStream(encoded)
        val message = readFrom(input) ?: throw AdbFormatException("Missing ADB message")
        if (input.available() != 0) throw AdbFormatException("Trailing bytes after ADB message")
        return message
    }

    /** Returns null only on a clean EOF before a new ADB header begins. */
    fun readFrom(input: InputStream): AdbMessage? {
        val first = input.read()
        if (first < 0) return null
        val header = ByteArray(HEADER_LENGTH)
        header[0] = first.toByte()
        readFully(input, header, 1, HEADER_LENGTH - 1, "ADB header")

        val commandRaw = readIntLe(header, 0)
        val argument0 = readIntLe(header, 4).toLong() and 0xffff_ffffL
        val argument1 = readIntLe(header, 8).toLong() and 0xffff_ffffL
        val payloadLength = readIntLe(header, 12).toLong() and 0xffff_ffffL
        val expectedChecksum = readIntLe(header, 16).toLong() and 0xffff_ffffL
        val magic = readIntLe(header, 20)
        if (magic != commandRaw xor -1) throw AdbFormatException("Invalid ADB command magic")
        if (payloadLength > MAX_PAYLOAD_LENGTH) {
            throw AdbFormatException("ADB payload is too large: $payloadLength")
        }

        val bytes = ByteArray(payloadLength.toInt())
        readFully(input, bytes, 0, bytes.size, "ADB payload")
        val payload = BinaryData.of(bytes)
        val actualChecksum = checksum(payload)
        if (actualChecksum != expectedChecksum) {
            throw AdbFormatException("ADB payload checksum mismatch")
        }
        return AdbMessage(AdbCommand(commandRaw), argument0, argument1, payload)
    }

    fun checksum(payload: BinaryData): Long = payload.withBytes { bytes ->
        bytes.fold(0L) { sum, byte -> (sum + (byte.toInt() and 0xff)) and 0xffff_ffffL }
    }

    private fun writeIntLe(output: OutputStream, value: Int) {
        output.write(value)
        output.write(value ushr 8)
        output.write(value ushr 16)
        output.write(value ushr 24)
    }

    private fun readIntLe(bytes: ByteArray, offset: Int): Int =
        (bytes[offset].toInt() and 0xff) or
            ((bytes[offset + 1].toInt() and 0xff) shl 8) or
            ((bytes[offset + 2].toInt() and 0xff) shl 16) or
            ((bytes[offset + 3].toInt() and 0xff) shl 24)

    private fun readFully(
        input: InputStream,
        destination: ByteArray,
        offset: Int,
        length: Int,
        description: String,
    ) {
        var position = offset
        val end = offset + length
        while (position < end) {
            val read = input.read(destination, position, end - position)
            if (read < 0) throw EOFException("Truncated $description")
            if (read == 0) {
                val byte = input.read()
                if (byte < 0) throw EOFException("Truncated $description")
                destination[position++] = byte.toByte()
            } else {
                position += read
            }
        }
    }
}


