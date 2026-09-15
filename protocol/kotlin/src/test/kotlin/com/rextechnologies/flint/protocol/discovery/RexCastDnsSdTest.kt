package com.rextechnologies.flint.protocol.discovery

import com.rextechnologies.flint.protocol.BinaryData
import com.rextechnologies.flint.protocol.network.Ipv4
import java.security.SecureRandom
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class FlintDnsSdTest {
    @Test
    fun `query targets the rex cast service`() {
        val query = FlintDnsSd.query()
        assertEquals(0, query.flags)
        assertEquals(DnsQuestion(FlintDnsSd.SERVICE_TYPE, DnsType.PTR), query.questions.single())
    }

    @Test
    fun `announcement round trip extracts a complete case insensitive service`() {
        val service = FlintService(
            instanceName = "Living Room",
            hostName = "FireTV",
            port = 47_855,
            address = Ipv4.parse("192.168.88.12"),
            attributes = linkedMapOf("version" to "1", "codec" to "h265"),
            ttlSeconds = 90,
        )
        val decoded = DnsPacketCodec.decode(DnsPacketCodec.encode(FlintDnsSd.announcement(service)))
        assertEquals(listOf(service), FlintDnsSd.extractServices(decoded))
        assertEquals(0x8400, decoded.flags)
        assertTrue(decoded.additionals.all { it.recordClass and DnsClass.CACHE_FLUSH != 0 })
    }

    @Test
    fun `extractor skips incomplete invalid and duplicate advertisements`() {
        val service = FlintService("Room", "tv", 1234, Ipv4.parse("10.0.0.2"))
        val announcement = FlintDnsSd.announcement(service)
        assertTrue(FlintDnsSd.extractServices(DnsPacket(answers = announcement.answers)).isEmpty())

        val duplicate = announcement.copy(
            answers = announcement.answers + announcement.answers,
            additionals = announcement.additionals + announcement.additionals,
        )
        assertEquals(1, FlintDnsSd.extractServices(duplicate).size)

        val wrongSuffix = announcement.copy(
            answers = listOf(PtrRecord(FlintDnsSd.SERVICE_TYPE, "Room._other._tcp.local")),
        )
        assertTrue(FlintDnsSd.extractServices(wrongSuffix).isEmpty())

        val badHost = announcement.copy(
            additionals = announcement.additionals.map {
                if (it is SrvRecord) it.copy(target = "not-local.example") else it
            },
        )
        assertTrue(FlintDnsSd.extractServices(badHost).isEmpty())
    }

    @Test
    fun `TXT parsing accepts flags and ignores empty keys`() {
        val instance = "Room.${FlintDnsSd.SERVICE_TYPE}"
        val host = "tv.local"
        val packet = DnsPacket(
            answers = listOf(PtrRecord(FlintDnsSd.SERVICE_TYPE.uppercase(), instance + ".")),
            additionals = listOf(
                SrvRecord(instance, 0, 0, 1234, host),
                ARecord(host, Ipv4.parse("10.0.0.2")),
                TxtRecord(instance, listOf(bytes("secure"), bytes("codec=h264"), bytes("=bad"))),
            ),
        )
        assertEquals(mapOf("secure" to "", "codec" to "h264"), FlintDnsSd.extractServices(packet).single().attributes)
    }

    @Test
    fun `service and TXT input validation fail early`() {
        val address = Ipv4.parse("10.0.0.2")
        assertFailsWith<IllegalArgumentException> { FlintService("", "tv", 1, address) }
        assertFailsWith<IllegalArgumentException> { FlintService("a.b", "tv", 1, address) }
        assertFailsWith<IllegalArgumentException> { FlintService("a", "tv.local", 1, address) }
        assertFailsWith<IllegalArgumentException> { FlintService("a", "tv", 0, address) }
        assertFailsWith<IllegalArgumentException> { FlintService("a", "tv", 1, address, ttlSeconds = -1) }
        assertFailsWith<IllegalArgumentException> {
            FlintService("a", "tv", 1, address, attributes = mapOf("bad=key" to "x"))
        }
        assertFailsWith<IllegalArgumentException> {
            FlintService("a", "tv", 1, address, attributes = (0..64).associate { "k$it" to "v" })
        }
        val tooLong = FlintService("a", "tv", 1, address, attributes = mapOf("k" to "x".repeat(256)))
        assertFailsWith<DnsFormatException> { FlintDnsSd.announcement(tooLong) }
    }

    @Test
    fun `pairing code has fixed ASCII shape and constant time comparison`() {
        val code = PairingCode.generate(FixedRandom())
        assertEquals("000000", code.toString())
        assertEquals(code, PairingCode.parse("000000"))
        assertEquals(code.hashCode(), PairingCode.parse("000000").hashCode())
        assertTrue(code.constantTimeMatches("000000"))
        assertFalse(code.constantTimeMatches("000001"))
        assertNull(PairingCode.parseOrNull("12345"))
        listOf("12345", "1234567", "ï¼‘ï¼’ï¼“ï¼”ï¼•ï¼–", "12a456").forEach {
            assertFailsWith<IllegalArgumentException> { PairingCode.parse(it) }
        }
    }

    private fun bytes(value: String) = BinaryData.of(value.toByteArray())

    private class FixedRandom : SecureRandom() {
        override fun nextInt(bound: Int): Int = 0
    }
}
