package com.rextechnologies.flint.mobile.net

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.castcore.capability.DiscoverySource
import com.rextechnologies.flint.castcore.discovery.DiscoveryRung
import com.rextechnologies.flint.castcore.discovery.DiscoveryStep
import com.rextechnologies.flint.protocol.discovery.DnsPacketCodec
import com.rextechnologies.flint.protocol.discovery.FlintDnsSd
import com.rextechnologies.flint.protocol.discovery.FlintService
import com.rextechnologies.flint.protocol.discovery.MulticastDnsSocket
import com.rextechnologies.flint.protocol.network.Ipv4
import com.rextechnologies.flint.protocol.network.Ipv4Subnet
import kotlinx.coroutines.runBlocking
import org.junit.Assume.assumeFalse
import org.junit.Test
import org.junit.runner.RunWith
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import kotlin.concurrent.thread
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

/**
 * The phone's multicast DNS rung, asking a real responder on the host's own interface.
 *
 * The responder side binds the multicast group, which Linux allows and Windows refuses, so the
 * exchanges skip on a Windows host only. What happens when multicast cannot be used at all is
 * checked everywhere.
 */
@RunWith(AndroidJUnit4::class)
class MulticastDnsRungTest {
    private val context: Context = ApplicationProvider.getApplicationContext()
    private val runner = DiscoveryRunner(context)

    @Test
    fun `an address no interface owns reports that multicast did not reach the phone`() = runBlocking<Unit> {
        val nowhere = Ipv4.parse("192.0.2.1")

        val result = runner.multicastDns(DiscoveryStep.MulticastDns(nowhere, Ipv4Subnet(nowhere, 24)))

        assertEquals(DiscoveryRung.MULTICAST_DNS, result.rung)
        assertTrue(result.attempted)
        assertTrue(result.receivers.isEmpty())
        assertEquals("Multicast did not reach this phone.", result.detail)
    }

    @Test
    fun `a receiver that answers directly is found, even after a packet that is not dns`() = runBlocking<Unit> {
        val (address, subnet) = linuxInterface()
        val responder = respondOnce(address, junkFirst = true)

        val result = runner.multicastDns(DiscoveryStep.MulticastDns(address, subnet))
        responder.join(5_000)

        val device = assertNotNull(result.receivers.singleOrNull(), "found ${result.receivers}")
        assertEquals(address.hostAddress, device.address)
        assertEquals(47_855, device.port)
        assertEquals("Living Room", device.friendlyName)
        assertEquals(DiscoverySource.MULTICAST_DNS, device.source)
        assertTrue(device.receiverAnswered)
    }

    @Test
    fun `an answer from off the subnet is ignored and the real one still found`() = runBlocking<Unit> {
        val (address, subnet) = linuxInterface()
        val responder = respondOnce(address, strangerFirst = true)

        val result = runner.multicastDns(DiscoveryStep.MulticastDns(address, subnet))
        responder.join(5_000)

        val device = assertNotNull(result.receivers.singleOrNull(), "found ${result.receivers}")
        assertEquals(address.hostAddress, device.address)
    }

    @Test
    fun `an answer only from off the subnet finds nothing`() = runBlocking<Unit> {
        // The stranger's announcement is well formed and names a TV on this subnet; only where it
        // came from gives it away, which is exactly what the rung has to check.
        val (address, subnet) = linuxInterface()
        val responder = respondOnce(address, strangerFirst = true, answer = false)

        val result = runner.multicastDns(DiscoveryStep.MulticastDns(address, subnet))
        responder.join(5_000)

        assertTrue(result.attempted)
        assertTrue(result.receivers.none { it.friendlyName == "Living Room" }, "found ${result.receivers}")
    }

    @Test
    fun `no answer within the window finds nothing without failing`() = runBlocking<Unit> {
        val (address, subnet) = linuxInterface()

        val result = runner.multicastDns(DiscoveryStep.MulticastDns(address, subnet))

        assertTrue(result.attempted)
        assertTrue(result.receivers.none { it.friendlyName == "Living Room" })
        assertEquals("", result.detail)
    }

    private companion object {
        val isWindows: Boolean = System.getProperty("os.name").orEmpty().startsWith("Windows")

        fun linuxInterface(): Pair<Inet4Address, Ipv4Subnet> {
            assumeFalse("Windows refuses to bind a socket to a multicast group address", isWindows)
            val binding = assertNotNull(
                NetworkInterface.getNetworkInterfaces()
                    .asSequence()
                    .filter { runCatching { it.isUp && !it.isLoopback && it.supportsMulticast() }.getOrDefault(false) }
                    .flatMap { it.interfaceAddresses.asSequence() }
                    .firstOrNull { it.address is Inet4Address },
                "this host has no multicast-capable IPv4 interface",
            )
            val address = binding.address as Inet4Address
            return address to Ipv4Subnet(address, binding.networkPrefixLength.toInt())
        }

        /**
         * A television on [address] that answers the first Flint query it hears, then stops.
         *
         * [junkFirst] sends a datagram that is not DNS before the answer. [strangerFirst] has a
         * socket on the loopback, off the subnet, send the same announcement first. [answer] false
         * leaves the television itself silent.
         */
        fun respondOnce(
            address: Inet4Address,
            junkFirst: Boolean = false,
            strangerFirst: Boolean = false,
            answer: Boolean = true,
        ): Thread {
            val socket = MulticastDnsSocket.open(address, 3_000)
            return thread(isDaemon = true) {
                socket.use { active ->
                    repeat(50) {
                        val received = active.receive() ?: return@thread
                        val query = runCatching { DnsPacketCodec.decode(received.payload) }.getOrNull() ?: return@repeat
                        if (query.questions.none { it.name.trimEnd('.') == FlintDnsSd.SERVICE_TYPE }) return@repeat
                        val announcement = DnsPacketCodec.encode(
                            FlintDnsSd.announcement(
                                FlintService(
                                    instanceName = "Living Room",
                                    hostName = "rexcast-test",
                                    port = 47_855,
                                    address = address,
                                ),
                            ),
                        )
                        if (junkFirst) active.sendTo(byteArrayOf(1, 2, 3), received.source)
                        if (strangerFirst) {
                            DatagramSocket(InetSocketAddress(InetAddress.getLoopbackAddress(), 0)).use { stranger ->
                                stranger.send(DatagramPacket(announcement, announcement.size, received.source))
                            }
                        }
                        if (answer) active.sendTo(announcement, received.source)
                        return@thread
                    }
                }
            }
        }
    }
}
