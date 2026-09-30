package com.rextechnologies.flint.protocol.discovery

import com.rextechnologies.flint.protocol.network.Ipv4
import com.rextechnologies.flint.protocol.network.Ipv4Subnet
import org.junit.Assume.assumeFalse
import java.net.DatagramPacket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.MulticastSocket
import java.net.NetworkInterface
import java.net.SocketException
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * The mDNS socket, run for real.
 *
 * The receive side binds the multicast group itself, which Linux — and so Android — allows and
 * Windows refuses. These tests therefore skip on a Windows host and nowhere else: on the Linux CI
 * runner a socket that cannot bind is a failure, not a reason to pass quietly.
 */
class MulticastDnsSocketTest {
    @Test
    fun `an ordinary query receives a direct reply from the selected interface`() {
        val address = linuxInterface()
        val network = assertNotNull(NetworkInterface.getByInetAddress(address))
        MulticastDnsSocket.open(address, 2_000).use { responder ->
            assertEquals(address, responder.localAddress)
            MulticastSocket(null).use { query ->
                query.reuseAddress = true
                query.bind(InetSocketAddress(address, 0))
                query.networkInterface = network
                query.soTimeout = 2_000

                val request = byteArrayOf(1, 2, 3)
                val group = InetSocketAddress(MulticastDnsSocket.GROUP, MulticastDnsSocket.PORT)
                query.send(DatagramPacket(request, request.size, group))

                val received = assertNotNull(receiveMatching(responder) { it.payload.contentEquals(request) })
                assertEquals(query.localPort, received.source.port)
                assertTrue(received.wantsDirectReply)

                val response = byteArrayOf(4, 5, 6)
                responder.sendTo(response, received.source)
                val packet = DatagramPacket(ByteArray(16), 16)
                query.receive(packet)
                assertContentEquals(response, packet.data.copyOf(packet.length))
            }
        }
    }

    @Test
    fun `a group send reaches another member on the same interface`() {
        val address = linuxInterface()
        MulticastDnsSocket.open(address, 2_000).use { listener ->
            MulticastDnsSocket.open(address, 2_000).use { speaker ->
                val payload = byteArrayOf(7, 8, 9)

                speaker.sendToGroup(payload)

                val received = assertNotNull(receiveMatching(listener) { it.payload.contentEquals(payload) })
                assertEquals(MulticastDnsSocket.PORT, received.source.port)
                assertFalse(received.wantsDirectReply, "a full mDNS participant is answered through the group")
            }
        }
    }

    @Test
    fun `a quiet group ends the wait with nothing rather than blocking`() {
        val address = linuxInterface()
        MulticastDnsSocket.open(address, 150).use { socket ->
            // Other hosts' mDNS chatter may arrive first; the wait must still end on its own.
            val quiet = (1..20).firstNotNullOfOrNull { attempt -> if (socket.receive() == null) attempt else null }
            assertNotNull(quiet, "receive never timed out")
        }
    }

    @Test
    fun `a closed socket can no longer send`() {
        val address = linuxInterface()
        val socket = MulticastDnsSocket.open(address, 150)

        socket.close()

        assertFailsWith<SocketException> { socket.sendToGroup(byteArrayOf(1)) }
    }

    @Test
    fun `the timeout must be positive`() {
        assertFailsWith<IllegalArgumentException> { MulticastDnsSocket.open(Ipv4.parse("192.0.2.1"), 0) }
    }

    @Test
    fun `an address no interface owns is rejected`() {
        val failure = assertFailsWith<IllegalArgumentException> {
            MulticastDnsSocket.open(Ipv4.parse("192.0.2.1"), 50)
        }
        assertTrue(failure.message.orEmpty().contains("192.0.2.1"))
    }

    @Test
    fun `only an ipv4 sender on the interface's own subnet is accepted`() {
        val subnet = Ipv4Subnet(Ipv4.parse("192.168.1.10"), 24)

        assertTrue(MulticastDnsSocket.isOnSubnet(subnet, Ipv4.parse("192.168.1.42")))
        assertFalse(MulticastDnsSocket.isOnSubnet(subnet, Ipv4.parse("192.168.2.42")), "another interface's traffic")
        assertFalse(MulticastDnsSocket.isOnSubnet(subnet, InetAddress.getByName("fe80::1")), "not IPv4")
        assertFalse(MulticastDnsSocket.isOnSubnet(subnet, null), "no sender")
    }

    @Test
    fun `a datagram is kept only from this interface's subnet, and only its own bytes`() {
        val subnet = Ipv4Subnet(Ipv4.parse("192.168.1.10"), 24)
        val buffer = byteArrayOf(9, 1, 2, 3, 9)

        val kept = MulticastDnsSocket.accept(
            subnet,
            DatagramPacket(buffer, 1, 3, InetSocketAddress(Ipv4.parse("192.168.1.42"), 5_353)),
        )
        val skipped = MulticastDnsSocket.accept(
            subnet,
            DatagramPacket(buffer, 1, 3, InetSocketAddress(Ipv4.parse("10.0.0.7"), 5_353)),
        )

        val received = assertNotNull(kept)
        assertContentEquals(byteArrayOf(1, 2, 3), received.payload)
        assertEquals(InetSocketAddress(Ipv4.parse("192.168.1.42"), 5_353), received.source)
        assertNull(skipped, "another interface's group traffic, which the wait skips")
    }

    @Test
    fun `received datagrams compare by payload and source`() {
        val source = InetSocketAddress(Ipv4.parse("192.0.2.2"), 12_345)
        val first = MulticastDnsSocket.Received(byteArrayOf(1, 2), source)
        val same = MulticastDnsSocket.Received(byteArrayOf(1, 2), source)
        val otherPayload = MulticastDnsSocket.Received(byteArrayOf(3), source)
        val otherSource = MulticastDnsSocket.Received(byteArrayOf(1, 2), InetSocketAddress(Ipv4.parse("192.0.2.3"), 1))

        assertEquals(first, same)
        assertEquals(first.hashCode(), same.hashCode())
        assertFalse(first == otherPayload)
        assertFalse(first == otherSource)
        assertFalse(first.equals("not a datagram"))
    }

    private companion object {
        val isWindows: Boolean = System.getProperty("os.name").orEmpty().startsWith("Windows")

        /**
         * The first up, multicast-capable IPv4 interface. Skips on Windows, where the group bind is
         * refused; anywhere else a missing interface fails the test rather than excusing it.
         */
        fun linuxInterface(): Inet4Address {
            assumeFalse("Windows refuses to bind a socket to a multicast group address", isWindows)
            return assertNotNull(
                NetworkInterface.getNetworkInterfaces()
                    .asSequence()
                    .filter { runCatching { it.isUp && !it.isLoopback && it.supportsMulticast() }.getOrDefault(false) }
                    .flatMap { it.inetAddresses.asSequence() }
                    .filterIsInstance<Inet4Address>()
                    .firstOrNull(),
                "this host has no multicast-capable IPv4 interface",
            )
        }

        /** Receives until [match] accepts a datagram, skipping any unrelated group traffic. */
        fun receiveMatching(
            socket: MulticastDnsSocket,
            match: (MulticastDnsSocket.Received) -> Boolean,
        ): MulticastDnsSocket.Received? {
            repeat(50) {
                val received = socket.receive() ?: return null
                if (match(received)) return received
            }
            return null
        }
    }
}
