package com.rextechnologies.flint.receiver.net

import com.rextechnologies.flint.protocol.discovery.ReceiverAnnouncement
import com.rextechnologies.flint.protocol.discovery.ReceiverProbe
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.nio.charset.StandardCharsets
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * The broadcast rung, exercised with a real broadcast.
 *
 * These used to run over loopback, sending a unicast datagram to `127.0.0.1`. That passed against a
 * responder that could not receive a broadcast at all, because loopback has no broadcast address --
 * so the one thing the class is for was the one thing the tests did not do. They now find a real
 * broadcast-capable interface and send to its broadcast address, which is what the phone sends to.
 */
class ReceiverBroadcastResponderTest {
    /**
     * A real interface with a broadcast address, or `null` on a host that has none.
     *
     * Every ordinary machine has one -- an Ethernet or Wi-Fi interface with a netmask. A host that
     * genuinely has none cannot exercise a broadcast at all, and the tests that need one say so
     * rather than passing quietly.
     */
    private val broadcastCapable: Pair<Inet4Address, Inet4Address>? =
        NetworkInterface.getNetworkInterfaces()
            .asSequence()
            .filter { runCatching { it.isUp && !it.isLoopback }.getOrDefault(false) }
            .flatMap { it.interfaceAddresses.asSequence() }
            .mapNotNull { candidate ->
                val local = candidate.address as? Inet4Address ?: return@mapNotNull null
                val broadcast = candidate.broadcast as? Inet4Address ?: return@mapNotNull null
                local to broadcast
            }
            .firstOrNull()

    @Test
    fun `the host running these tests can broadcast`() {
        // The one test that fails rather than skipping. Every test below needs a broadcast-capable
        // interface and returns early without one, so without this the suite could go green on a
        // host where none of it ran -- which is how the loopback version of these tests managed to
        // pass against a responder that could not receive a broadcast at all.
        assertNotNull(
            broadcastCapable,
            "no interface on this host has a broadcast address, so the broadcast rung is untested",
        )
    }

    @Test
    fun `the bind address is the interface's broadcast address, not the receiver's own`() {
        // The whole bug in one assertion: a socket bound to 192.0.2.2 never sees a datagram sent to
        // 192.0.2.255, because the kernel matches the arriving datagram's destination against the
        // socket's bound address.
        val (local, broadcast) = broadcastCapable ?: return
        assertEquals(broadcast, ReceiverBroadcastResponder.broadcastAddressFor(local))
        assertFalse(broadcast == local)
    }

    @Test
    fun `an interface with no broadcast address has none invented for it`() {
        val loopback = InetAddress.getLoopbackAddress() as Inet4Address
        assertNull(ReceiverBroadcastResponder.broadcastAddressFor(loopback))
    }

    @Test
    fun `a responder on an interface that cannot broadcast refuses to start`() {
        // It loses the ladder one rung and says why, rather than binding something that can never
        // hear the thing it is listening for.
        val loopback = InetAddress.getLoopbackAddress() as Inet4Address
        val responder = ReceiverBroadcastResponder(loopback, 47_855, { "Fire TV" }, listenPort = 0)
        try {
            assertTrue(responder.start().isFailure)
        } finally {
            responder.close()
        }
    }

    @Test
    fun `a broadcast probe is answered with this receiver's name and service port`() {
        withResponder({ "Fire TV Stick 4K" }) { local, broadcast, port ->
            val announcement = probe(local, broadcast, port)
            assertNotNull(announcement)
            assertEquals("Fire TV Stick 4K", announcement.modelName)
            assertEquals(47_855, announcement.port)
        }
    }

    @Test
    fun `anything that is not the probe is ignored rather than answered`() {
        withResponder({ "Fire TV" }) { local, broadcast, port ->
            assertNull(probe(local, broadcast, port, request = "GET / HTTP/1.1"))
        }
    }

    @Test
    fun `a name carrying a field separator cannot split the answer`() {
        withResponder({ "Fire\tTV\nStick" }) { local, broadcast, port ->
            assertEquals("Fire TV Stick", probe(local, broadcast, port)?.modelName)
        }
    }

    @Test
    fun `starting twice is refused rather than leaking the first socket`() {
        val (local, _) = broadcastCapable ?: return
        val responder = ReceiverBroadcastResponder(local, 47_855, { "Fire TV" }, listenPort = 0)
        assertTrue(responder.start().isSuccess)
        try {
            assertTrue(responder.start().isFailure)
        } finally {
            responder.close()
        }
    }

    @Test
    fun `closing twice is harmless`() {
        val local = broadcastCapable?.first ?: InetAddress.getLoopbackAddress() as Inet4Address
        val responder = ReceiverBroadcastResponder(local, 47_855, { "Fire TV" }, listenPort = 0)
        responder.start()
        responder.close()
        responder.close()
    }

    private fun withResponder(
        modelName: () -> String,
        body: (local: Inet4Address, broadcast: Inet4Address, port: Int) -> Unit,
    ) {
        val (local, broadcast) = broadcastCapable ?: return
        val responder = ReceiverBroadcastResponder(local, 47_855, modelName, listenPort = 0)
        assertTrue(responder.start().isSuccess, "the responder did not bind $broadcast")
        try {
            assertEquals(broadcast, responder.boundAddress)
            body(local, broadcast, responder.boundPort)
        } finally {
            responder.close()
        }
    }

    private fun probe(
        local: Inet4Address,
        broadcast: Inet4Address,
        port: Int,
        request: String = ReceiverProbe.REQUEST_LINE,
    ): ReceiverAnnouncement? {
        DatagramSocket(InetSocketAddress(local, 0)).use { client ->
            client.broadcast = true
            client.soTimeout = TIMEOUT_MILLIS
            val payload = "$request\n".toByteArray(StandardCharsets.US_ASCII)
            client.send(DatagramPacket(payload, payload.size, broadcast, port))

            val buffer = ByteArray(ReceiverProbe.MAX_RESPONSE_BYTES)
            val packet = DatagramPacket(buffer, buffer.size)
            return try {
                client.receive(packet)
                ReceiverProbe.parseResponse(
                    String(packet.data, packet.offset, packet.length, StandardCharsets.UTF_8),
                )
            } catch (_: Throwable) {
                null
            }
        }
    }

    private companion object {
        const val TIMEOUT_MILLIS = 2_000
    }
}
