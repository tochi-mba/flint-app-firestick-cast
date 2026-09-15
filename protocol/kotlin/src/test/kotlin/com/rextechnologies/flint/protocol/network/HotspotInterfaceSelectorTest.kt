package com.rextechnologies.flint.protocol.network

import java.net.NetworkInterface
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class HotspotInterfaceSelectorTest {
    private val selector = HotspotInterfaceSelector()

    @Test
    fun `selection prefers soft ap family then enumeration order`() {
        val selected = assertNotNull(selector.select(listOf(
            snapshot("rndis0", 8, address = "192.168.42.1"),
            snapshot("wlan1", 7, address = "192.168.50.1"),
            snapshot("softap0", 6, address = "192.168.60.1"),
            snapshot("swlan0", 5, address = "192.168.70.1"),
            snapshot("ap2", 4, address = "192.168.80.1", prefix = 26),
            snapshot("ap1", 3, address = "192.168.90.1"),
        )))
        assertEquals("ap2", selected.interfaceName)
        assertEquals(4, selected.interfaceIndex)
        assertEquals("192.168.80.1", selected.address.hostAddress)
        assertEquals(26, selected.prefixLength)
        assertEquals("192.168.80.0", selected.subnet.networkAddress.hostAddress)
    }

    @Test
    fun `selection rejects down loopback client and public interfaces`() {
        val candidates = listOf(
            snapshot("ap0", 1, up = false),
            snapshot("swlan0", 2, loopback = true),
            snapshot("wlan0", 3),
            snapshot("eth0", 4),
            snapshot("ap0", 5, address = "8.8.8.8"),
        )
        assertNull(selector.select(candidates))
        assertNull(selector.select(emptyList()))
    }

    @Test
    fun `selection skips unusable first address and malformed names`() {
        val valid = snapshot("rndis12", 9, address = "10.1.2.1")
        val invalidNames = listOf("ap", "swlan", "softap", "wlan01", "rndis", "AP-x")
        invalidNames.forEach { name -> assertNull(selector.select(listOf(snapshot(name, 1))), name) }
        assertEquals(valid.index, assertNotNull(selector.select(listOf(valid))).interfaceIndex)
    }

    @Test
    fun `snapshot models validate prefixes and JVM adapter is safe to enumerate`() {
        assertFailsWith<IllegalArgumentException> {
            InterfaceAddressSnapshot(Ipv4.parse("10.0.0.1"), 33)
        }
        val snapshots = JvmNetworkInterfaceSource().snapshots()
        assertTrue(snapshots.all { item -> item.addresses.all { it.prefixLength in 0..32 } })
        val source = NetworkInterfaceSource { listOf(snapshot("ap0", 1)) }
        assertEquals("ap0", source.snapshots().single().name)
    }

    @Test
    fun `a tunnel wearing a hotspot name is not a hotspot`() {
        // WireGuard and OpenVPN both let the user name the interface, so a tunnel can arrive
        // called ap0. The point-to-point flag is the part the user cannot rename.
        val tunnel = snapshot("ap0", 1, address = "192.168.49.1").copy(isPointToPoint = true)
        val real = snapshot("wlan1", 2, address = "192.168.43.1")

        assertNull(selector.select(listOf(tunnel)))
        assertEquals("wlan1", selector.select(listOf(tunnel, real))?.interfaceName)
    }

    @Test
    fun `the jvm source reports the point-to-point flag`() {
        // Nothing here asserts a tunnel exists on the build machine — only that the field is
        // populated from the interface rather than left at its permissive default.
        val snapshots = JvmNetworkInterfaceSource().snapshots()
        val expected = NetworkInterface.getNetworkInterfaces().toList()
            .filter { it.isPointToPoint }
            .map { it.name }
            .toSet()
        assertEquals(expected, snapshots.filter { it.isPointToPoint }.map { it.name }.toSet())
    }

    private fun snapshot(
        name: String,
        index: Int,
        up: Boolean = true,
        loopback: Boolean = false,
        address: String = "192.168.1.1",
        prefix: Int = 24,
    ) = NetworkInterfaceSnapshot(
        name = name,
        index = index,
        isUp = up,
        isLoopback = loopback,
        addresses = listOf(InterfaceAddressSnapshot(Ipv4.parse(address), prefix)),
    )
}


