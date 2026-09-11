package com.rextechnologies.flint.castcore.discovery

import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.protocol.network.Ipv4Subnet
import com.rextechnologies.flint.protocol.network.SelectedHotspotInterface
import java.net.Inet4Address
import java.net.InetAddress
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

private fun address(value: String): Inet4Address = InetAddress.getByName(value) as Inet4Address

class SweepBudgetTest {
    @Test
    fun `an ordinary hotspot subnet is sweepable`() {
        // A /24 is 256 addresses, of which the network and broadcast are not hosts and one is this
        // phone, so 253 remain to probe.
        val budget = SweepBudget.forSubnet(Ipv4Subnet(address("192.168.43.1"), 24))
        assertNotNull(budget)
        assertEquals(253, budget.hostCount)
        assertEquals(SweepBudget.CONNECT_TIMEOUT_MILLIS, budget.connectTimeoutMillis)
        assertEquals(SweepBudget.CONCURRENCY, budget.concurrency)
    }

    @Test
    fun `a point to point link is still worth one probe`() {
        val budget = SweepBudget.forSubnet(Ipv4Subnet(address("10.0.0.1"), 30))
        assertNotNull(budget)
        assertEquals(1, budget.hostCount)
    }

    @Test
    fun `a subnet with nothing to probe produces no budget`() {
        assertNull(SweepBudget.forSubnet(Ipv4Subnet(address("10.0.0.1"), 32)))
    }

    @Test
    fun `a subnet too large to sweep responsibly produces no budget`() {
        // Sixty thousand sequential connects is not a sweep, it is a denial of the radio.
        assertNull(SweepBudget.forSubnet(Ipv4Subnet(address("10.0.0.1"), 16)))
        assertNull(SweepBudget.forSubnet(Ipv4Subnet(address("10.0.0.1"), 8)))
    }

    @Test
    fun `the bound is the one the constant names`() {
        assertEquals(512, SweepBudget.MAXIMUM_SWEEP_HOSTS)
        assertNotNull(SweepBudget.forSubnet(Ipv4Subnet(address("10.0.0.1"), 23)))
    }

    @Test
    fun `a budget outside its own bounds is refused`() {
        assertFailsWith<IllegalArgumentException> { SweepBudget(0, 400, 16) }
        assertFailsWith<IllegalArgumentException> { SweepBudget(1_000, 400, 16) }
        assertFailsWith<IllegalArgumentException> { SweepBudget(10, 0, 16) }
        assertFailsWith<IllegalArgumentException> { SweepBudget(10, 400, 0) }
        assertFailsWith<IllegalArgumentException> { SweepBudget(10, 400, 100) }
    }
}

class DiscoveryLadderTest {
    private val host = LocalNetwork.PhoneIsHost(
        SelectedHotspotInterface("ap0", 7, address("192.168.43.1"), 24),
    )
    private val client = LocalNetwork.PhoneIsClient("wlan0", 3, address("192.168.1.44"), 24)

    @Test
    fun `nothing is planned without a local network`() {
        assertTrue(DiscoveryLadder.plan(LocalNetwork.NoLocalNetwork).isEmpty())
    }

    @Test
    fun `the multicast-free rung goes first, because multicast is what fails on a hotspot`() {
        listOf(host, client).forEach { network ->
            val plan = DiscoveryLadder.plan(network)
            assertEquals(DiscoveryRung.LINE_PROBE, plan.first().rung)
            assertEquals(
                listOf(
                    DiscoveryRung.LINE_PROBE,
                    DiscoveryRung.MULTICAST_DNS,
                    DiscoveryRung.UDP_BROADCAST,
                    DiscoveryRung.SSDP,
                    DiscoveryRung.MANUAL,
                ),
                plan.map { it.rung },
            )
        }
    }

    @Test
    fun `typing an address in by hand is always the last resort and always offered`() {
        listOf(host, client).forEach {
            assertEquals(DiscoveryRung.MANUAL, DiscoveryLadder.plan(it).last().rung)
        }
    }

    @Test
    fun `a subnet too large to sweep simply has no sweep rung`() {
        val wide = LocalNetwork.PhoneIsClient("wlan0", 3, address("10.1.2.3"), 8)
        val plan = DiscoveryLadder.plan(wide)
        assertFalse(plan.any { it.rung == DiscoveryRung.LINE_PROBE })
        assertEquals(DiscoveryRung.MULTICAST_DNS, plan.first().rung)
        assertEquals(4, plan.size)
    }

    @Test
    fun `every rung is pinned to the same interface address`() {
        DiscoveryLadder.plan(host).forEach {
            assertEquals(address("192.168.43.1"), it.boundAddress)
        }
    }

    @Test
    fun `only the multicast rungs ask for a multicast lock`() {
        val plan = DiscoveryLadder.plan(host).associateBy { it.rung }
        assertTrue(plan.getValue(DiscoveryRung.MULTICAST_DNS).requiresMulticastLock)
        assertTrue(plan.getValue(DiscoveryRung.SSDP).requiresMulticastLock)
        assertFalse(plan.getValue(DiscoveryRung.LINE_PROBE).requiresMulticastLock)
        assertFalse(plan.getValue(DiscoveryRung.UDP_BROADCAST).requiresMulticastLock)
        assertFalse(plan.getValue(DiscoveryRung.MANUAL).requiresMulticastLock)
    }

    @Test
    fun `the broadcast rung carries the address derived from the subnet`() {
        val plan = DiscoveryLadder.plan(host).associateBy { it.rung }
        assertEquals(address("192.168.43.255"), plan.getValue(DiscoveryRung.UDP_BROADCAST).broadcastAddress)
        assertNull(plan.getValue(DiscoveryRung.LINE_PROBE).broadcastAddress)
    }

    @Test
    fun `only the sweep rung carries a budget`() {
        val plan = DiscoveryLadder.plan(host).associateBy { it.rung }
        assertNotNull(plan.getValue(DiscoveryRung.LINE_PROBE).sweep)
        assertNull(plan.getValue(DiscoveryRung.MULTICAST_DNS).sweep)
    }
}
