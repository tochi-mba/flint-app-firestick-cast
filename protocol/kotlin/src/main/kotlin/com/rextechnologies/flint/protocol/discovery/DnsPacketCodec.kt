package com.rextechnologies.flint.protocol.discovery

import com.rextechnologies.flint.protocol.BinaryData
import java.io.ByteArrayOutputStream
import java.net.InetAddress
import java.nio.ByteBuffer
import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets

class DnsFormatException(message: String, cause: Throwable? = null) : IllegalArgumentException(message, cause)

object DnsPacketCodec {
    const val MAX_PACKET_BYTES: Int = 9_000
    private const val HEADER_BYTES = 12
    private const val MAX_TOTAL_ENTRIES = 512

    fun encode(packet: DnsPacket): ByteArray {
        val writer = DnsWriter()
        writer.u16(packet.id)
        writer.u16(packet.flags)
        writer.u16(packet.questions.size)
        writer.u16(packet.answers.size)
        writer.u16(packet.authorities.size)
        writer.u16(packet.additionals.size)
        packet.questions.forEach {
            writer.name(it.name)
            writer.u16(it.type)
            writer.u16(it.questionClass)
        }
        packet.answers.forEach { writer.record(it) }
        packet.authorities.forEach { writer.record(it) }
        packet.additionals.forEach { writer.record(it) }
        val result = writer.toByteArray()
        if (result.size > MAX_PACKET_BYTES) {
            throw DnsFormatException("DNS packet is too large: ${result.size} bytes")
        }
        return result
    }

    fun decode(packet: ByteArray): DnsPacket {
        if (packet.size < HEADER_BYTES) throw DnsFormatException("Truncated DNS header")
        if (packet.size > MAX_PACKET_BYTES) throw DnsFormatException("DNS packet is too large")
        val cursor = Cursor(0)
        val id = readU16(packet, cursor, "id")
        val flags = readU16(packet, cursor, "flags")
        val questionCount = readU16(packet, cursor, "question count")
        val answerCount = readU16(packet, cursor, "answer count")
        val authorityCount = readU16(packet, cursor, "authority count")
        val additionalCount = readU16(packet, cursor, "additional count")
        val total = questionCount + answerCount + authorityCount + additionalCount
        if (total > MAX_TOTAL_ENTRIES) throw DnsFormatException("Too many DNS entries: $total")

        val questions = List(questionCount) {
            DnsQuestion(
                name = readName(packet, cursor),
                type = readU16(packet, cursor, "question type"),
                questionClass = readU16(packet, cursor, "question class"),
            )
        }
        val answers = List(answerCount) { readRecord(packet, cursor) }
        val authorities = List(authorityCount) { readRecord(packet, cursor) }
        val additionals = List(additionalCount) { readRecord(packet, cursor) }
        if (cursor.position != packet.size) {
            throw DnsFormatException("Trailing DNS packet bytes: ${packet.size - cursor.position}")
        }
        return DnsPacket(id, flags, questions, answers, authorities, additionals)
    }

    private fun readRecord(packet: ByteArray, cursor: Cursor): DnsRecord {
        val name = readName(packet, cursor)
        val type = readU16(packet, cursor, "record type")
        val recordClass = readU16(packet, cursor, "record class")
        val ttl = readU32(packet, cursor, "record ttl")
        val dataLength = readU16(packet, cursor, "record data length")
        val dataStart = cursor.position
        val dataEnd = dataStart + dataLength
        if (dataEnd < dataStart || dataEnd > packet.size) throw DnsFormatException("Truncated record data")

        val record = when (type) {
            DnsType.A -> {
                if (dataLength != 4) throw DnsFormatException("A record must contain four bytes")
                val address = InetAddress.getByAddress(packet.copyOfRange(dataStart, dataEnd))
                cursor.position = dataEnd
                ARecord(name, address as java.net.Inet4Address, recordClass, ttl)
            }

            DnsType.PTR -> {
                val target = readName(packet, cursor)
                requireRecordEnd(cursor, dataEnd, "PTR")
                PtrRecord(name, target, recordClass, ttl)
            }

            DnsType.SRV -> {
                if (dataLength < 7) throw DnsFormatException("Truncated SRV record")
                val priority = readU16(packet, cursor, "SRV priority")
                val weight = readU16(packet, cursor, "SRV weight")
                val port = readU16(packet, cursor, "SRV port")
                val target = readName(packet, cursor)
                requireRecordEnd(cursor, dataEnd, "SRV")
                SrvRecord(name, priority, weight, port, target, recordClass, ttl)
            }

            DnsType.TXT -> {
                val entries = mutableListOf<BinaryData>()
                while (cursor.position < dataEnd) {
                    val length = readU8(packet, cursor, "TXT entry length")
                    if (cursor.position + length > dataEnd) throw DnsFormatException("Truncated TXT entry")
                    entries += BinaryData.of(packet.copyOfRange(cursor.position, cursor.position + length))
                    cursor.position += length
                    if (entries.size > 256) throw DnsFormatException("Too many TXT entries")
                }
                TxtRecord(name, entries, recordClass, ttl)
            }

            else -> {
                cursor.position = dataEnd
                RawDnsRecord(
                    name = name,
                    type = type,
                    recordClass = recordClass,
                    ttlSeconds = ttl,
                    data = BinaryData.of(packet.copyOfRange(dataStart, dataEnd)),
                )
            }
        }
        if (cursor.position != dataEnd) throw DnsFormatException("Record data length mismatch")
        return record
    }

    private fun requireRecordEnd(cursor: Cursor, expected: Int, type: String) {
        if (cursor.position != expected) throw DnsFormatException("$type record data length mismatch")
    }

    private fun readName(packet: ByteArray, cursor: Cursor): String {
        var position = cursor.position
        var returnPosition = -1
        val labels = mutableListOf<String>()
        val visitedPointers = mutableSetOf<Int>()
        var expandedLength = 1

        while (true) {
            if (position !in packet.indices) throw DnsFormatException("Truncated DNS name")
            val length = packet[position].toInt() and 0xff
            when {
                length == 0 -> {
                    if (returnPosition < 0) returnPosition = position + 1
                    cursor.position = returnPosition
                    return if (labels.isEmpty()) "." else labels.joinToString(".")
                }

                length and 0xc0 == 0xc0 -> {
                    if (position + 1 >= packet.size) throw DnsFormatException("Truncated name pointer")
                    val pointer = ((length and 0x3f) shl 8) or (packet[position + 1].toInt() and 0xff)
                    if (returnPosition < 0) returnPosition = position + 2
                    if (!visitedPointers.add(pointer)) throw DnsFormatException("DNS name pointer loop")
                    if (visitedPointers.size > 32) throw DnsFormatException("Too many DNS name pointers")
                    position = pointer
                }

                length and 0xc0 != 0 -> throw DnsFormatException("Unsupported DNS label encoding")
                else -> {
                    if (length > 63 || position + 1 + length > packet.size) {
                        throw DnsFormatException("Invalid DNS label length")
                    }
                    expandedLength += length + 1
                    if (expandedLength > 255 || labels.size >= 127) {
                        throw DnsFormatException("DNS name is too long")
                    }
                    labels += decodeUtf8(packet, position + 1, length, "DNS label")
                    position += length + 1
                }
            }
        }
    }

    private fun readU8(packet: ByteArray, cursor: Cursor, description: String): Int {
        if (cursor.position >= packet.size) throw DnsFormatException("Truncated $description")
        return packet[cursor.position++].toInt() and 0xff
    }

    private fun readU16(packet: ByteArray, cursor: Cursor, description: String): Int {
        if (cursor.position + 2 > packet.size) throw DnsFormatException("Truncated $description")
        val result = ((packet[cursor.position].toInt() and 0xff) shl 8) or
            (packet[cursor.position + 1].toInt() and 0xff)
        cursor.position += 2
        return result
    }

    private fun readU32(packet: ByteArray, cursor: Cursor, description: String): Long {
        if (cursor.position + 4 > packet.size) throw DnsFormatException("Truncated $description")
        var result = 0L
        repeat(4) { result = (result shl 8) or (packet[cursor.position++].toLong() and 0xff) }
        return result
    }

    private fun decodeUtf8(bytes: ByteArray, offset: Int, length: Int, description: String): String = try {
        StandardCharsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .decode(ByteBuffer.wrap(bytes, offset, length))
            .toString()
    } catch (exception: CharacterCodingException) {
        throw DnsFormatException("Invalid UTF-8 in $description", exception)
    }

    private data class Cursor(var position: Int)
}

private class DnsWriter {
    private val output = ByteArrayOutputStream()

    fun u8(value: Int) {
        if (value !in 0..0xff) throw DnsFormatException("Value is not u8: $value")
        output.write(value)
    }

    fun u16(value: Int) {
        if (value !in 0..0xffff) throw DnsFormatException("Value is not u16: $value")
        output.write(value ushr 8)
        output.write(value)
    }

    fun u32(value: Long) {
        if (value !in 0..0xffff_ffffL) throw DnsFormatException("Value is not u32: $value")
        output.write((value ushr 24).toInt())
        output.write((value ushr 16).toInt())
        output.write((value ushr 8).toInt())
        output.write(value.toInt())
    }

    fun name(name: String) {
        if (name == ".") {
            u8(0)
            return
        }
        val canonical = name.removeSuffix(".")
        if (canonical.isEmpty()) throw DnsFormatException("DNS name is empty")
        var encodedLength = 1
        canonical.split('.').forEach { label ->
            if (label.isEmpty()) throw DnsFormatException("DNS name contains an empty label")
            val bytes = label.toByteArray(StandardCharsets.UTF_8)
            if (bytes.size !in 1..63) throw DnsFormatException("DNS label is too long")
            encodedLength += bytes.size + 1
            if (encodedLength > 255) throw DnsFormatException("DNS name is too long")
            u8(bytes.size)
            output.write(bytes)
        }
        u8(0)
    }

    fun record(record: DnsRecord) {
        name(record.name)
        u16(record.type)
        u16(record.recordClass)
        u32(record.ttlSeconds)
        val data = DnsWriter()
        when (record) {
            is ARecord -> data.output.write(record.address.address)
            is PtrRecord -> data.name(record.target)
            is SrvRecord -> {
                data.u16(record.priority)
                data.u16(record.weight)
                data.u16(record.port)
                data.name(record.target)
            }

            is TxtRecord -> record.entries.forEach { entry ->
                data.u8(entry.size)
                entry.withBytes { data.output.write(it) }
            }

            is RawDnsRecord -> record.data.withBytes { data.output.write(it) }
        }
        val bytes = data.toByteArray()
        if (bytes.size > 0xffff) throw DnsFormatException("DNS record data is too long")
        u16(bytes.size)
        output.write(bytes)
    }

    fun toByteArray(): ByteArray = output.toByteArray()
}
