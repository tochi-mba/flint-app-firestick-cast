package com.rextechnologies.flint.castcore.capability

import com.rextechnologies.flint.protocol.network.HotspotInterfaceSelector
import com.rextechnologies.flint.protocol.network.InterfaceAddressSnapshot
import com.rextechnologies.flint.protocol.network.Ipv4
import com.rextechnologies.flint.protocol.network.NetworkInterfaceSnapshot
import com.rextechnologies.flint.protocol.network.SelectedHotspotInterface
import kotlin.test.Test
import kotlin.test.assertContains
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertSame
import kotlin.test.assertTrue

class LocalNetworkTest {
    private val assessor = LocalNetworkAssessor()

    @Test
    fun `a tether interface makes the phone the host of the subnet it serves`() {
        val verdict = host(assessor.assess(listOf(snapshot("ap0", 11, address = "192.168.43.1"))))
        assertEquals("ap0", verdict.selected.interfaceName)
        assertEquals(11, verdict.selected.interfaceIndex)
        assertEquals("192.168.43.1", verdict.boundAddress.hostAddress)
        assertSame(verdict.selected.address, verdict.boundAddress)
        assertEquals("192.168.43.0", verdict.subnet.networkAddress.hostAddress)
        assertEquals("192.168.43.255", verdict.subnet.broadcastAddress.hostAddress)
        assertEquals(24, verdict.subnet.prefixLength)
        assertEquals(256L, verdict.subnet.addressCount)
        assertTrue(verdict.subnet.contains(Ipv4.parse("192.168.43.9")))
        assertFalse(verdict.subnet.contains(Ipv4.parse("192.168.44.9")))
    }

    @Test
    fun `every tether interface family is recognised as the phone hosting`() {
        val tethers = listOf(
            "ap0" to "192.168.43.1",
            "swlan0" to "192.168.44.1",
            "softap0" to "192.168.45.1",
            "wlan1" to "192.168.46.1",
            "rndis0" to "192.168.42.1",
        )
        tethers.forEachIndexed { position, (name, address) ->
            val verdict = host(assessor.assess(listOf(snapshot(name, position + 1, address = address))))
            assertEquals(name, verdict.selected.interfaceName, name)
            assertEquals(position + 1, verdict.selected.interfaceIndex, name)
            assertEquals(address, verdict.boundAddress.hostAddress, name)
            assertEquals(address.substringBeforeLast('.') + ".0", verdict.subnet.networkAddress.hostAddress, name)
        }
    }

    @Test
    fun `a hotspot wins over a site local address on another interface`() {
        val verdict = host(
            assessor.assess(
                listOf(
                    snapshot("wlan0", 12, address = "192.168.1.37"),
                    snapshot("ap0", 13, address = "10.5.0.1"),
                ),
            ),
        )
        assertEquals("ap0", verdict.selected.interfaceName)
        assertEquals("10.5.0.1", verdict.boundAddress.hostAddress)
        assertEquals("10.5.0.0", verdict.subnet.networkAddress.hostAddress)
    }

    @Test
    fun `a site local address without a tether makes the phone a client`() {
        val verdict = client(assessor.assess(listOf(snapshot("wlan0", 12, address = "192.168.1.37"))))
        assertEquals("wlan0", verdict.interfaceName)
        assertEquals(12, verdict.interfaceIndex)
        assertEquals("192.168.1.37", verdict.boundAddress.hostAddress)
        assertEquals(24, verdict.prefixLength)
        assertEquals("192.168.1.0", verdict.subnet.networkAddress.hostAddress)
        assertEquals("192.168.1.255", verdict.subnet.broadcastAddress.hostAddress)
        assertEquals(24, verdict.subnet.prefixLength)
        assertTrue(verdict.subnet.contains(Ipv4.parse("192.168.1.60")))
    }

    @Test
    fun `an empty interface list is no local network and binds nothing`() {
        val verdict = assessor.assess(emptyList())
        assertSame(LocalNetwork.NoLocalNetwork, verdict)
        assertNull(verdict.boundAddress)
        assertNull(LocalNetwork.NoLocalNetwork.boundAddress)
    }

    @Test
    fun `a down interface carrying a site local address is ignored`() {
        val verdict = assessor.assess(listOf(snapshot("wlan0", 12, up = false, address = "192.168.1.37")))
        assertSame(LocalNetwork.NoLocalNetwork, verdict)
        assertNull(verdict.boundAddress)
    }

    @Test
    fun `a loopback interface carrying a site local address is ignored`() {
        // The interface flag is what rejects this, not the address: the snapshot deliberately carries
        // a routable site-local address so only `isLoopback` can be doing the work. The companion
        // `!address.isLoopbackAddress` guard inside the assessor has no reachable false branch,
        // because no IPv4 address is both site-local (10/8, 172.16/12, 192.168/16) and loopback (127/8).
        val verdict = assessor.assess(
            listOf(
                snapshot("lo", 1, loopback = true, address = "10.0.0.1", prefix = 8),
                snapshot("lo0", 2, loopback = true, address = "127.0.0.1", prefix = 8),
            ),
        )
        assertSame(LocalNetwork.NoLocalNetwork, verdict)
    }

    @Test
    fun `a carrier grade nat address never becomes a client verdict`() {
        val verdict = assessor.assess(
            listOf(
                snapshot("rmnet0", 21, address = "100.64.12.9", prefix = 10),
                snapshot("rmnet1", 22, address = "8.8.8.8", prefix = 32),
            ),
        )
        assertSame(LocalNetwork.NoLocalNetwork, verdict)
        assertNull(verdict.boundAddress)
    }

    @Test
    fun `an interface with no addresses at all is skipped`() {
        val verdict = assessor.assess(
            listOf(
                NetworkInterfaceSnapshot("dummy0", 31, isUp = true, isLoopback = false, addresses = emptyList()),
                snapshot("wlan0", 32, address = "172.16.4.9", prefix = 20),
            ),
        )
        val surviving = client(verdict)
        assertEquals("wlan0", surviving.interfaceName)
        assertEquals("172.16.4.9", surviving.boundAddress.hostAddress)
        assertEquals("172.16.0.0", surviving.subnet.networkAddress.hostAddress)
    }

    @Test
    fun `the first site local address on an interface is the one bound`() {
        val verdict = client(
            assessor.assess(
                listOf(
                    NetworkInterfaceSnapshot(
                        name = "wlan0",
                        index = 12,
                        isUp = true,
                        isLoopback = false,
                        addresses = listOf(
                            binding("203.0.113.7", 32),
                            binding("192.168.9.4", 24),
                            binding("10.9.9.9", 8),
                        ),
                    ),
                ),
            ),
        )
        assertEquals("192.168.9.4", verdict.boundAddress.hostAddress)
        assertEquals(24, verdict.prefixLength)
    }

    @Test
    fun `the narrower subnet wins between two client candidates`() {
        val wide = snapshot("eth0", 41, address = "10.0.0.5", prefix = 8)
        val narrow = snapshot("wlan0", 42, address = "192.168.1.37", prefix = 24)
        val wideFirst = client(assessor.assess(listOf(wide, narrow)))
        assertEquals("wlan0", wideFirst.interfaceName)
        assertEquals(42, wideFirst.interfaceIndex)
        assertEquals(24, wideFirst.prefixLength)

        val narrowFirst = client(assessor.assess(listOf(narrow, wide)))
        assertEquals("wlan0", narrowFirst.interfaceName)
        assertEquals(24, narrowFirst.prefixLength)
    }

    @Test
    fun `a tie on prefix length keeps the earlier interface across repeated samples`() {
        val interfaces = listOf(
            snapshot("wlan0", 51, address = "192.168.1.37"),
            snapshot("eth0", 52, address = "192.168.5.9"),
        )
        val first = client(assessor.assess(interfaces))
        assertEquals("wlan0", first.interfaceName)
        assertEquals("192.168.1.37", first.boundAddress.hostAddress)

        val second = assessor.assess(interfaces)
        val third = assessor.assess(interfaces)
        assertEquals(first, second)
        assertEquals(second, third)
    }

    @Test
    fun `an injected selector is used in place of the default`() {
        val injected = LocalNetworkAssessor(HotspotInterfaceSelector())
        val verdict = host(injected.assess(listOf(snapshot("softap0", 61, address = "192.168.45.1"))))
        assertEquals("softap0", verdict.selected.interfaceName)
        assertEquals("192.168.45.1", verdict.boundAddress.hostAddress)
        assertSame(LocalNetwork.NoLocalNetwork, injected.assess(emptyList()))
    }

    @Test
    fun `verdicts compare by content and name their interface`() {
        val selected = SelectedHotspotInterface("ap0", 11, Ipv4.parse("192.168.43.1"), 24)
        val hostVerdict = LocalNetwork.PhoneIsHost(selected)
        assertEquals(LocalNetwork.PhoneIsHost(selected), hostVerdict)
        assertEquals(LocalNetwork.PhoneIsHost(selected).hashCode(), hostVerdict.hashCode())
        assertContains(hostVerdict.toString(), "ap0")

        val clientVerdict = LocalNetwork.PhoneIsClient("wlan0", 12, Ipv4.parse("192.168.1.37"), 24)
        assertEquals(LocalNetwork.PhoneIsClient("wlan0", 12, Ipv4.parse("192.168.1.37"), 24), clientVerdict)
        assertEquals(clientVerdict, clientVerdict.copy())
        assertEquals("eth0", clientVerdict.copy(interfaceName = "eth0").interfaceName)
        assertFalse(clientVerdict == clientVerdict.copy(prefixLength = 16))
        assertFalse(clientVerdict == clientVerdict.copy(interfaceIndex = 13))
        // Widened to `LocalNetwork` so the three verdicts can be compared across their own types,
        // which the compiler refuses for two unrelated final classes written out directly.
        val everyShape: List<LocalNetwork> = listOf(hostVerdict, clientVerdict, LocalNetwork.NoLocalNetwork)
        assertEquals(3, everyShape.distinct().size)
        assertContains(clientVerdict.toString(), "wlan0")
        assertContains(LocalNetwork.NoLocalNetwork.toString(), "NoLocalNetwork")
        assertEquals(clientVerdict.interfaceName, clientVerdict.component1())
    }

    @Test
    fun `a client verdict with an impossible prefix rejects its subnet`() {
        val impossible = LocalNetwork.PhoneIsClient(
            interfaceName = "wlan0",
            interfaceIndex = 12,
            boundAddress = Ipv4.parse("192.168.1.37"),
            prefixLength = 33,
        )
        assertEquals("192.168.1.37", impossible.boundAddress.hostAddress)
        val failure = assertFailsWith<IllegalArgumentException> { impossible.subnet }
        assertEquals("IPv4 prefix must be between 0 and 32", failure.message)
        assertFailsWith<IllegalArgumentException> { InterfaceAddressSnapshot(Ipv4.parse("10.0.0.1"), 33) }
    }

    private fun host(verdict: LocalNetwork): LocalNetwork.PhoneIsHost =
        assertNotNull(verdict as? LocalNetwork.PhoneIsHost, "expected a host verdict but got $verdict")

    private fun client(verdict: LocalNetwork): LocalNetwork.PhoneIsClient =
        assertNotNull(verdict as? LocalNetwork.PhoneIsClient, "expected a client verdict but got $verdict")

    private fun binding(address: String, prefix: Int) = InterfaceAddressSnapshot(Ipv4.parse(address), prefix)

    @Test
    fun `a vpn tunnel never wins the client interface`() {
        // The bug this pins: a WireGuard tunnel is site-local and usually a /32, so the
        // narrowest-subnet rule below picked it over the Wi-Fi the television is actually on,
        // and the sweep then searched a tunnel with one host in it.
        val tunnel = snapshot("tun0", 9, address = "10.8.0.2", prefix = 32).copy(isPointToPoint = true)
        val wifi = snapshot("wlan0", 2, address = "192.168.1.34", prefix = 24)

        val assessed = assessor.assess(listOf(tunnel, wifi))

        val client = assertIs<LocalNetwork.PhoneIsClient>(assessed)
        assertEquals("wlan0", client.interfaceName)
        assertEquals(24, client.prefixLength)
    }

    @Test
    fun `a tunnel alone is not a local network`() {
        val tunnel = snapshot("tun0", 9, address = "10.8.0.2", prefix = 32).copy(isPointToPoint = true)

        assertIs<LocalNetwork.NoLocalNetwork>(assessor.assess(listOf(tunnel)))
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
        addresses = listOf(binding(address, prefix)),
    )
}
