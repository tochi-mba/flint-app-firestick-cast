package com.rextechnologies.flint.protocol.discovery

import com.rextechnologies.flint.protocol.network.Ipv4
import org.junit.Assume.assumeNoException
import java.net.DatagramPacket
import java.net.Inet4Address
import java.net.InetSocketAddress
import java.net.MulticastSocket
import java.net.NetworkInterface
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

class MulticastDnsSocketTest {
    private val local: Inet4Address? = NetworkInterface.getNetworkInterfaces()
        .asSequence()
        .filter { runCatching { it.isUp && !it.isLoopback && it.supportsMulticast() }.getOrDefault(false) }
        .flatMap { it.inetAddresses.asSequence() }
        .filterIsInstance<Inet4Address>()
        .firstOrNull()

    @Test
    fun `an ordinary query receives a direct reply from the selected interface`() {
        val address = local ?: return
        val network = assertNotNull(NetworkInterface.getByInetAddress(address))
        val opened = runCatching { MulticastDnsSocket.open(address, 2_000) }
        if (opened.isFailure) {
            assumeNoException("This host cannot bind an mDNS group address", opened.exceptionOrNull())
            return
        }
        opened.getOrThrow().use { responder ->
            MulticastSocket(null).use { query ->
                query.reuseAddress = true
                query.bind(InetSocketAddress(address, 0))
                query.networkInterface = network
                query.soTimeout = 2_000

                val request = byteArrayOf(1, 2, 3)
                val group = InetSocketAddress(MulticastDnsSocket.GROUP, MulticastDnsSocket.PORT)
                query.send(DatagramPacket(request, request.size, group))

                val received = assertNotNull(responder.receive())
                assertContentEquals(request, received.payload)
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
    fun `the timeout must be positive`() {
        val address = local ?: Ipv4.parse("192.0.2.1")
        assertFailsWith<IllegalArgumentException> { MulticastDnsSocket.open(address, 0) }
    }

    @Test
    fun `an address no interface owns is rejected`() {
        assertFailsWith<IllegalArgumentException> {
            MulticastDnsSocket.open(Ipv4.parse("192.0.2.1"), 50)
        }
    }

    @Test
    fun `received datagrams compare by payload and source`() {
        val source = InetSocketAddress(Ipv4.parse("192.0.2.2"), 12_345)
        val first = MulticastDnsSocket.Received(byteArrayOf(1, 2), source)
        val same = MulticastDnsSocket.Received(byteArrayOf(1, 2), source)

        assertEquals(first, same)
        assertEquals(first.hashCode(), same.hashCode())
    }
}
