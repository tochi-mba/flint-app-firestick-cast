package com.rextechnologies.flint.receiver.net

import com.rextechnologies.flint.protocol.discovery.ReceiverAnnouncement
import com.rextechnologies.flint.protocol.discovery.ReceiverProbe
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.nio.ByteBuffer
import java.nio.charset.StandardCharsets
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** The broadcast rung, exercised with a real broadcast whenever the host exposes one. */
class ReceiverBroadcastResponderTest {
    /**
     * The first address on a real subnet, whose broadcast address is the top of its own prefix.
     *
     * Windows also lists VPN links and disconnected adapters, reporting broadcast addresses such as
     * 0.0.0.0 that belong to no subnet. Sending to one fails with "Cannot assign requested address",
     * and which adapter came first changed from run to run, so different tests failed each time.
     */
    private val broadcastCapable: Pair<Inet4Address, Inet4Address>? =
        NetworkInterface.getNetworkInterfaces()
            .asSequence()
            .filter { runCatching { it.isUp && !it.isLoopback && !it.isPointToPoint }.getOrDefault(false) }
            .flatMap { it.interfaceAddresses.asSequence() }
            .mapNotNull { candidate ->
                val local = (candidate.address as? Inet4Address)?.takeUnless { it.isLinkLocalAddress }
                    ?: return@mapNotNull null
                val broadcast = candidate.broadcast as? Inet4Address ?: return@mapNotNull null
                (local to broadcast).takeIf { subnetBroadcast(local, candidate.networkPrefixLength) == broadcast }
            }
            .firstOrNull()

    @Test
    fun `the host running these tests can broadcast`() {
        assertNotNull(
            broadcastCapable,
            "no interface on this host has a broadcast address, so the broadcast rung is untested",
        )
    }

    @Test
    fun `the selected interface supplies the broadcast target`() {
        val (local, broadcast) = broadcastCapable ?: return
        assertEquals(broadcast, ReceiverBroadcastResponder.broadcastAddressFor(local))
        assertFalse(broadcast == local)
    }

    @Test
    fun `an address with no local interface has no broadcast address invented for it`() {
        assertNull(ReceiverBroadcastResponder.broadcastAddressFor(unassignedAddress()))
    }

    @Test
    fun `a responder on an interface that cannot broadcast refuses to start`() {
        val responder = ReceiverBroadcastResponder(unassignedAddress(), 47_855, { "Fire TV" }, listenPort = 0)
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
        val local = broadcastCapable?.first ?: unassignedAddress()
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
        assertTrue(responder.start().isSuccess, "the responder did not bind for $broadcast")
        try {
            assertTrue(responder.boundAddress?.isAnyLocalAddress == true)
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

    private fun unassignedAddress(): Inet4Address =
        InetAddress.getByAddress(byteArrayOf(192.toByte(), 0, 2, 1)) as Inet4Address

    /** The broadcast address of [local]'s subnet; a /31 or /32 link has none. */
    private fun subnetBroadcast(local: Inet4Address, prefixLength: Short): Inet4Address? {
        if (prefixLength !in 1..30) return null
        val bits = ByteBuffer.wrap(local.address).int or (-1 ushr prefixLength.toInt())
        return InetAddress.getByAddress(ByteBuffer.allocate(4).putInt(bits).array()) as Inet4Address
    }

    private companion object {
        const val TIMEOUT_MILLIS = 2_000
    }
}
