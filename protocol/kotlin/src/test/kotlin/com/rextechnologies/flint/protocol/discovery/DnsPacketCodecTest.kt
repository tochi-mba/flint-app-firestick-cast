package com.rextechnologies.flint.protocol.discovery

import com.rextechnologies.flint.protocol.BinaryData
import com.rextechnologies.flint.protocol.network.Ipv4
import java.io.ByteArrayOutputStream
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertIs
import kotlin.test.assertTrue

class DnsPacketCodecTest {
    @Test
    fun `questions and every record form round trip`() {
        val packet = DnsPacket(
            id = 0x1234,
            flags = 0x8400,
            questions = listOf(DnsQuestion("_rexcast._tcp.local", DnsType.PTR)),
            answers = listOf(PtrRecord("_rexcast._tcp.local", "Living._rexcast._tcp.local")),
            authorities = listOf(
                RawDnsRecord(".", 65000, DnsClass.IN, 1, BinaryData.of(byteArrayOf(1, 2))),
            ),
            additionals = listOf(
                SrvRecord("Living._rexcast._tcp.local", 0, 2, 47855, "firetv.local"),
                TxtRecord("Living._rexcast._tcp.local", listOf(bytes("v=1"), BinaryData.EMPTY)),
                ARecord("firetv.local", Ipv4.parse("192.168.77.2")),
            ),
        )
        val decoded = DnsPacketCodec.decode(DnsPacketCodec.encode(packet))
        assertEquals(packet, decoded)
        assertEquals(packet.answers + packet.authorities + packet.additionals, decoded.records)
        assertIs<RawDnsRecord>(decoded.authorities.single()).also {
            assertContentEquals(byteArrayOf(1, 2), it.data.toByteArray())
        }
    }

    @Test
    fun `decoder supports compressed names`() {
        val output = ByteArrayOutputStream()
        repeat(6) { u16(output, if (it == 2) 2 else 0) }
        name(output, "service.local")
        u16(output, DnsType.PTR)
        u16(output, DnsClass.IN)
        output.write(0xc0)
        output.write(12)
        u16(output, DnsType.ANY)
        u16(output, DnsClass.IN)
        val decoded = DnsPacketCodec.decode(output.toByteArray())
        assertEquals(listOf("service.local", "service.local"), decoded.questions.map { it.name })
    }

    @Test
    fun `decoder rejects malformed headers names records and trailing bytes`() {
        assertFailsWith<DnsFormatException> { DnsPacketCodec.decode(ByteArray(11)) }
        assertFailsWith<DnsFormatException> { DnsPacketCodec.decode(ByteArray(DnsPacketCodec.MAX_PACKET_BYTES + 1)) }

        val tooMany = ByteArray(12).also {
            it[4] = 2
            it[5] = 1
        }
        assertFailsWith<DnsFormatException> { DnsPacketCodec.decode(tooMany) }

        val valid = DnsPacketCodec.encode(DnsPacket(questions = listOf(DnsQuestion("a.local", DnsType.A))))
        assertFailsWith<DnsFormatException> { DnsPacketCodec.decode(valid + 0) }
        assertFailsWith<DnsFormatException> { DnsPacketCodec.decode(valid.copyOf(valid.size - 1)) }

        assertFailsWith<DnsFormatException> { DnsPacketCodec.decode(questionWithName(byteArrayOf(0xc0.toByte()))) }
        assertFailsWith<DnsFormatException> { DnsPacketCodec.decode(questionWithName(byteArrayOf(0xc0.toByte(), 12))) }
        assertFailsWith<DnsFormatException> { DnsPacketCodec.decode(questionWithName(byteArrayOf(0x40))) }
        assertFailsWith<DnsFormatException> {
            DnsPacketCodec.decode(questionWithName(byteArrayOf(2, 0xc3.toByte(), 0x28, 0)))
        }
        assertFailsWith<DnsFormatException> { DnsPacketCodec.decode(questionWithName(byteArrayOf(63) + ByteArray(2))) }

        val badA = encodedRawRecord(DnsType.A, byteArrayOf(1, 2, 3))
        assertFailsWith<DnsFormatException> { DnsPacketCodec.decode(badA) }
        val badSrv = encodedRawRecord(DnsType.SRV, ByteArray(6))
        assertFailsWith<DnsFormatException> { DnsPacketCodec.decode(badSrv) }
        val badTxt = encodedRawRecord(DnsType.TXT, byteArrayOf(3, 1, 2))
        assertFailsWith<DnsFormatException> { DnsPacketCodec.decode(badTxt) }
    }

    @Test
    fun `encoder validates model bounds names ttl and packet size`() {
        assertFailsWith<IllegalArgumentException> { DnsQuestion("x", -1) }
        assertFailsWith<IllegalArgumentException> { DnsQuestion("x", 1, 65_536) }
        assertFailsWith<IllegalArgumentException> { SrvRecord("x", -1, 0, 1, "h") }
        assertFailsWith<IllegalArgumentException> { SrvRecord("x", 0, 65_536, 1, "h") }
        assertFailsWith<IllegalArgumentException> { SrvRecord("x", 0, 0, 65_536, "h") }
        assertFailsWith<IllegalArgumentException> {
            TxtRecord("x", listOf(BinaryData.of(ByteArray(256))))
        }
        assertFailsWith<IllegalArgumentException> { RawDnsRecord("x", -1, 1, 1, BinaryData.EMPTY) }
        assertFailsWith<IllegalArgumentException> { DnsPacket(id = -1) }
        assertFailsWith<IllegalArgumentException> { DnsPacket(flags = 65_536) }

        listOf("", "a..local", "a".repeat(64) + ".local").forEach { name ->
            assertFailsWith<DnsFormatException>(name) {
                DnsPacketCodec.encode(DnsPacket(questions = listOf(DnsQuestion(name, 1))))
            }
        }
        val longName = List(5) { "a".repeat(63) }.joinToString(".")
        assertFailsWith<DnsFormatException> {
            DnsPacketCodec.encode(DnsPacket(questions = listOf(DnsQuestion(longName, 1))))
        }
        assertFailsWith<DnsFormatException> {
            DnsPacketCodec.encode(DnsPacket(answers = listOf(PtrRecord("x", "y", ttlSeconds = -1))))
        }
        val entries = List(180) { index ->
            TxtRecord("n$index.local", listOf(BinaryData.of(ByteArray(50))))
        }
        assertFailsWith<DnsFormatException> { DnsPacketCodec.encode(DnsPacket(answers = entries)) }
    }

    @Test
    fun `root label is supported`() {
        val packet = DnsPacket(questions = listOf(DnsQuestion(".", DnsType.ANY)))
        assertEquals(".", DnsPacketCodec.decode(DnsPacketCodec.encode(packet)).questions.single().name)
    }

    private fun bytes(value: String) = BinaryData.of(value.toByteArray())

    private fun questionWithName(encodedName: ByteArray): ByteArray = ByteArrayOutputStream().apply {
        repeat(6) { u16(this, if (it == 2) 1 else 0) }
        write(encodedName)
        u16(this, DnsType.A)
        u16(this, DnsClass.IN)
    }.toByteArray()

    private fun encodedRawRecord(type: Int, data: ByteArray): ByteArray = DnsPacketCodec.encode(
        DnsPacket(answers = listOf(RawDnsRecord("x.local", type, DnsClass.IN, 1, BinaryData.of(data)))),
    )

    private fun name(output: ByteArrayOutputStream, value: String) {
        value.split('.').forEach {
            output.write(it.length)
            output.write(it.toByteArray())
        }
        output.write(0)
    }

    private fun u16(output: ByteArrayOutputStream, value: Int) {
        output.write(value ushr 8)
        output.write(value)
    }
}
