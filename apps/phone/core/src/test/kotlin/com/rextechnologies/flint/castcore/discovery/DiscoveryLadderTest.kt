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
import kotlin.test.assertIs
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
        // A /30 is four addresses: network, two hosts, broadcast. One of the two hosts is this
        // phone, so one remains.
        val budget = SweepBudget.forSubnet(Ipv4Subnet(address("10.0.0.1"), 30))
        assertNotNull(budget)
        assertEquals(1, budget.hostCount)
    }

    @Test
    fun `an RFC 3021 point to point link has both ends usable`() {
        // A /31 has no network or broadcast address, so both of its two addresses are hosts and the
        // one that is not this phone is the peer.
        val budget = SweepBudget.forSubnet(Ipv4Subnet(address("10.0.0.1"), 31))
        assertNotNull(budget)
        assertEquals(1, budget.hostCount)
    }

    @Test
    fun `the budget is exactly what the sweep will visit, at every prefix`() {
        // These were two implementations of one rule and they disagreed. Comparing the budget with
        // the sequence it sizes is the only assertion that can catch them drifting apart again.
        for (prefix in 23..32) {
            val subnet = Ipv4Subnet(address("10.0.0.1"), prefix)
            val budget = SweepBudget.forSubnet(subnet)
            val actual = subnet.hosts().count()
            if (budget == null) {
                assertTrue(
                    actual < 1 || actual > SweepBudget.MAXIMUM_SWEEP_HOSTS,
                    "/$prefix has $actual hosts and should have had a budget",
                )
            } else {
                assertEquals(actual, budget.hostCount, "/$prefix")
            }
        }
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
        val broadcast = assertIs<DiscoveryStep.UdpBroadcast>(plan.getValue(DiscoveryRung.UDP_BROADCAST))
        assertEquals(address("192.168.43.255"), broadcast.broadcastAddress)
    }

    @Test
    fun `a subnet with no broadcast address has no broadcast rung`() {
        // A /31 has two addresses and both are hosts; a /32 has one. Neither has anything to
        // broadcast to, and a rung planned for one would be a rung that could only ever fail.
        listOf(31, 32).forEach { prefix ->
            val plan = DiscoveryLadder.plan(LocalNetwork.PhoneIsClient("wlan0", 3, address("10.0.0.1"), prefix))
            assertFalse(
                plan.any { it.rung == DiscoveryRung.UDP_BROADCAST },
                "/$prefix planned a broadcast with nowhere to send it",
            )
        }
    }

    @Test
    fun `only the sweep rung carries a budget`() {
        val plan = DiscoveryLadder.plan(host).associateBy { it.rung }
        val sweep = assertIs<DiscoveryStep.LineProbe>(plan.getValue(DiscoveryRung.LINE_PROBE))
        assertEquals(253, sweep.sweep.hostCount)
        assertIs<DiscoveryStep.MulticastDns>(plan.getValue(DiscoveryRung.MULTICAST_DNS))
    }

    @Test
    fun `every rung's own shape matches the rung it says it is`() {
        // The sealed hierarchy is only worth having if the rung name and the shape cannot disagree.
        DiscoveryLadder.plan(host).forEach { step ->
            val expected = when (step) {
                is DiscoveryStep.LineProbe -> DiscoveryRung.LINE_PROBE
                is DiscoveryStep.MulticastDns -> DiscoveryRung.MULTICAST_DNS
                is DiscoveryStep.UdpBroadcast -> DiscoveryRung.UDP_BROADCAST
                is DiscoveryStep.Ssdp -> DiscoveryRung.SSDP
                is DiscoveryStep.Manual -> DiscoveryRung.MANUAL
            }
            assertEquals(expected, step.rung)
            assertEquals(host.subnet, step.subnet)
        }
    }
}
