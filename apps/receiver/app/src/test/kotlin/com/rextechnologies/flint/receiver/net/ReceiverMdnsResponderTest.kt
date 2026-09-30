package com.rextechnologies.flint.receiver.net

import com.rextechnologies.flint.protocol.discovery.DnsPacketCodec
import com.rextechnologies.flint.protocol.discovery.FlintDnsSd
import com.rextechnologies.flint.protocol.discovery.MulticastDnsSocket
import org.junit.Assume.assumeTrue
import java.net.DatagramPacket
import java.net.Inet4Address
import java.net.InetSocketAddress
import java.net.MulticastSocket
import java.net.NetworkInterface
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull

class ReceiverMdnsResponderTest {
    private val local: Inet4Address? = NetworkInterface.getNetworkInterfaces()
        .asSequence()
        .filter { runCatching { it.isUp && !it.isLoopback && it.supportsMulticast() }.getOrDefault(false) }
        .flatMap { it.inetAddresses.asSequence() }
        .filterIsInstance<Inet4Address>()
        .firstOrNull()

    @Test
    fun `a query from an ordinary port receives the receiver announcement directly`() {
        val address = local ?: return
        val network = assertNotNull(NetworkInterface.getByInetAddress(address))
        ReceiverMdnsResponder(address, port = 47_855, browserPort = 44_321).use { responder ->
            val started = responder.start()
            assumeTrue("This host cannot bind an mDNS group address", started.isSuccess)
            MulticastSocket(null).use { query ->
                query.reuseAddress = true
                query.bind(InetSocketAddress(address, 0))
                query.networkInterface = network
                query.soTimeout = 2_000

                val payload = DnsPacketCodec.encode(FlintDnsSd.query())
                val group = InetSocketAddress(MulticastDnsSocket.GROUP, MulticastDnsSocket.PORT)
                query.send(DatagramPacket(payload, payload.size, group))

                val answer = DatagramPacket(ByteArray(DnsPacketCodec.MAX_PACKET_BYTES), DnsPacketCodec.MAX_PACKET_BYTES)
                query.receive(answer)
                val services = FlintDnsSd.extractServices(
                    DnsPacketCodec.decode(answer.data.copyOfRange(answer.offset, answer.offset + answer.length)),
                )
                val service = assertNotNull(services.singleOrNull())
                assertEquals(address, service.address)
                assertEquals(47_855, service.port)
                assertEquals("44321", service.attributes["browser_port"])
                assertEquals(MulticastDnsSocket.PORT, answer.port)
            }
        }
    }
}
