package com.rextechnologies.flint.receiver.net

import com.rextechnologies.flint.protocol.discovery.DnsPacket
import com.rextechnologies.flint.protocol.discovery.DnsPacketCodec
import com.rextechnologies.flint.protocol.discovery.DnsQuestion
import com.rextechnologies.flint.protocol.discovery.DnsType
import com.rextechnologies.flint.protocol.discovery.FlintDnsSd
import com.rextechnologies.flint.protocol.discovery.FlintService
import com.rextechnologies.flint.protocol.discovery.MulticastDnsSocket
import com.rextechnologies.flint.protocol.network.Ipv4
import org.junit.Assume.assumeFalse
import java.net.DatagramPacket
import java.net.Inet4Address
import java.net.InetSocketAddress
import java.net.MulticastSocket
import java.net.NetworkInterface
import java.net.SocketTimeoutException
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

/**
 * The television's advertisement, run for real on the host's own interface.
 *
 * The responder's socket binds the multicast group, which Linux and Android allow and Windows
 * refuses, so these skip on a Windows host only. On the Linux CI runner a responder that cannot
 * start is a failure.
 */
class ReceiverMdnsResponderTest {
    @Test
    fun `a query from an ordinary port receives the receiver announcement directly`() {
        val address = linuxInterface()
        val multicast = RecordingHold()
        val responder = ReceiverMdnsResponder(address, port = 47_855, browserPort = 44_321, holdMulticast = multicast)
        responder.use {
            assertTrue(responder.start().isSuccess)
            assertTrue(multicast.held, "multicast is held for as long as the responder listens")
            querier(address).use { query ->
                ask(query, FlintDnsSd.SERVICE_TYPE)

                val (service, port) = assertNotNull(answerFrom(query, address))
                assertEquals(47_855, service.port)
                assertEquals("44321", service.attributes["browser_port"])
                assertEquals("2", service.attributes["browser_protocol"])
                assertEquals(MulticastDnsSocket.PORT, port)
            }
        }
        assertFalse(multicast.held, "closing gives the hold back")
    }

    @Test
    fun `starting announces the receiver to the group and closing withdraws it`() {
        val address = linuxInterface()
        MulticastDnsSocket.open(address, 2_000).use { listener ->
            val responder = ReceiverMdnsResponder(address, port = 47_855, holdMulticast = RecordingHold())
            assertTrue(responder.start().isSuccess)

            val hello = assertNotNull(announcementFrom(listener, address))
            assertEquals(120L, hello.ttlSeconds)
            assertEquals(null, hello.attributes["browser_port"], "no browser listener, so no browser port")

            responder.close()

            val goodbye = assertNotNull(announcementFrom(listener, address))
            assertEquals(0L, goodbye.ttlSeconds, "a zero TTL tells every cache to forget the receiver")
        }
    }

    @Test
    fun `a query for another service and a packet that is not dns are both ignored`() {
        val address = linuxInterface()
        ReceiverMdnsResponder(address, port = 47_855, holdMulticast = RecordingHold()).use { responder ->
            assertTrue(responder.start().isSuccess)
            querier(address).use { query ->
                ask(query, "_googlecast._tcp.local")
                val garbage = byteArrayOf(1, 2, 3)
                query.send(DatagramPacket(garbage, garbage.size, GROUP))
                assertFailsWith<SocketTimeoutException> {
                    query.soTimeout = 700
                    query.receive(DatagramPacket(ByteArray(512), 512))
                }

                // Still answering: neither the stranger's question nor the junk stopped the loop.
                query.soTimeout = 2_000
                ask(query, FlintDnsSd.SERVICE_TYPE)
                assertNotNull(answerFrom(query, address))
            }
        }
    }

    @Test
    fun `a responder can only be started once`() {
        val address = linuxInterface()
        val multicast = RecordingHold()
        ReceiverMdnsResponder(address, port = 47_855, holdMulticast = multicast).use { responder ->
            assertTrue(responder.start().isSuccess)

            val again = responder.start()

            assertTrue(again.isFailure)
            assertTrue(again.exceptionOrNull()?.message.orEmpty().contains("already running"))
            assertEquals(1, multicast.taken, "the refused second start takes nothing")
        }
    }

    @Test
    fun `an address no interface owns does not start, and gives the hold back`() {
        val multicast = RecordingHold()
        ReceiverMdnsResponder(Ipv4.parse("192.0.2.1"), port = 47_855, holdMulticast = multicast).use { responder ->
            assertTrue(responder.start().isFailure)

            assertEquals(1, multicast.taken)
            assertFalse(multicast.held, "a start that fails must not leave the Wi-Fi chip awake")
        }
    }

    @Test
    fun `closing twice or without starting is harmless`() {
        val multicast = RecordingHold()
        val responder = ReceiverMdnsResponder(Ipv4.parse("192.0.2.1"), port = 47_855, holdMulticast = multicast)

        responder.close()
        responder.close()

        assertEquals(0, multicast.taken)
    }

    /** Counts how often the responder took the platform's multicast delivery and gave it back. */
    private class RecordingHold : () -> AutoCloseable {
        var taken = 0
        var released = 0
        val held: Boolean get() = taken > released

        override fun invoke(): AutoCloseable {
            taken++
            return AutoCloseable { released++ }
        }
    }

    private companion object {
        val GROUP = InetSocketAddress(MulticastDnsSocket.GROUP, MulticastDnsSocket.PORT)
        val isWindows: Boolean = System.getProperty("os.name").orEmpty().startsWith("Windows")

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

        /** An ordinary socket on [address], the way the phone asks. */
        fun querier(address: Inet4Address): MulticastSocket = MulticastSocket(null).apply {
            reuseAddress = true
            bind(InetSocketAddress(address, 0))
            networkInterface = NetworkInterface.getByInetAddress(address)
            soTimeout = 2_000
        }

        fun ask(query: MulticastSocket, serviceType: String) {
            val packet = DnsPacketCodec.encode(DnsPacket(questions = listOf(DnsQuestion(serviceType, DnsType.PTR))))
            query.send(DatagramPacket(packet, packet.size, GROUP))
        }

        /** The first Flint answer about [address], and the port it came from. */
        fun answerFrom(query: MulticastSocket, address: Inet4Address): Pair<FlintService, Int>? {
            repeat(20) {
                val packet = DatagramPacket(ByteArray(DnsPacketCodec.MAX_PACKET_BYTES), DnsPacketCodec.MAX_PACKET_BYTES)
                query.receive(packet)
                val decoded = runCatching {
                    DnsPacketCodec.decode(packet.data.copyOfRange(packet.offset, packet.offset + packet.length))
                }.getOrNull() ?: return@repeat
                val service = FlintDnsSd.extractServices(decoded).firstOrNull { it.address == address }
                if (service != null) return service to packet.port
            }
            return null
        }

        /** The next Flint announcement about [address] heard on the group. */
        fun announcementFrom(listener: MulticastDnsSocket, address: Inet4Address): FlintService? {
            repeat(50) {
                val received = listener.receive() ?: return null
                val decoded = runCatching { DnsPacketCodec.decode(received.payload) }.getOrNull() ?: return@repeat
                FlintDnsSd.extractServices(decoded).firstOrNull { it.address == address }?.let { return it }
            }
            return null
        }
    }
}
