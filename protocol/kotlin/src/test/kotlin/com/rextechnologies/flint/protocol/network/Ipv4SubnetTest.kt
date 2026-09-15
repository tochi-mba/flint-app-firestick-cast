package com.rextechnologies.flint.protocol.network

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class Ipv4SubnetTest {
    @Test
    fun `strict parser accepts all octets and rejects hostnames or ambiguous forms`() {
        assertEquals("0.0.0.0", Ipv4.parse("0.0.0.0").hostAddress)
        assertEquals("255.255.255.255", Ipv4.parse("255.255.255.255").hostAddress)
        listOf(
            "127.0.0",
            "127.0.0.1.2",
            "localhost",
            "1..2.3",
            "1.2.3.-1",
            "1.2.3.256",
            "1.2.3.999999999999999999999",
            " 1.2.3.4",
            "1.2.3.4 ",
        ).forEach { assertFailsWith<IllegalArgumentException>(it) { Ipv4.parse(it) } }
        assertFailsWith<IllegalArgumentException> { Ipv4.fromUnsignedLong(-1) }
        assertFailsWith<IllegalArgumentException> { Ipv4.fromUnsignedLong(0x1_0000_0000L) }
    }

    @Test
    fun `subnet derives addresses containment and counts without fixed slash 24`() {
        val subnet = Ipv4Subnet(Ipv4.parse("192.168.77.42"), 26)
        assertEquals("192.168.77.0", subnet.networkAddress.hostAddress)
        assertEquals("192.168.77.63", subnet.broadcastAddress.hostAddress)
        assertEquals(64, subnet.addressCount)
        assertTrue(subnet.contains(Ipv4.parse("192.168.77.1")))
        assertFalse(subnet.contains(Ipv4.parse("192.168.77.64")))
        assertFailsWith<IllegalArgumentException> { Ipv4Subnet(Ipv4.parse("1.2.3.4"), -1) }
        assertFailsWith<IllegalArgumentException> { Ipv4Subnet(Ipv4.parse("1.2.3.4"), 33) }
    }

    @Test
    fun `host enumeration excludes network broadcast and optionally local`() {
        val subnet = Ipv4Subnet(Ipv4.parse("10.0.0.2"), 30)
        assertEquals(listOf("10.0.0.1"), subnet.hosts().map { it.hostAddress }.toList())
        assertEquals(
            listOf("10.0.0.1", "10.0.0.2"),
            subnet.hosts(excludeLocalAddress = false).map { it.hostAddress }.toList(),
        )
        assertFailsWith<IllegalArgumentException> { subnet.hosts(maximumHosts = 0).toList() }
    }

    @Test
    fun `slash 31 and slash 32 follow point-to-point usability`() {
        val slash31 = Ipv4Subnet(Ipv4.parse("10.0.0.0"), 31)
        assertEquals(listOf("10.0.0.1"), slash31.hosts().map { it.hostAddress }.toList())
        assertEquals(2, slash31.hosts(false).count())

        val slash32 = Ipv4Subnet(Ipv4.parse("10.0.0.9"), 32)
        assertTrue(slash32.hosts().none())
        assertEquals(listOf("10.0.0.9"), slash32.hosts(false).map { it.hostAddress }.toList())
    }

    @Test
    fun `large prefixes fail closed at explicit scan bound`() {
        val slash16 = Ipv4Subnet(Ipv4.parse("172.16.3.4"), 16)
        assertFailsWith<IllegalArgumentException> { slash16.hosts(maximumHosts = 1_024).toList() }
        val slash0 = Ipv4Subnet(Ipv4.parse("8.8.8.8"), 0)
        assertEquals(4_294_967_296L, slash0.addressCount)
        assertEquals("0.0.0.0", slash0.networkAddress.hostAddress)
        assertEquals("255.255.255.255", slash0.broadcastAddress.hostAddress)
    }
}
