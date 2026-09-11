package com.rextechnologies.flint.receiver.net

import com.rextechnologies.flint.protocol.discovery.ReceiverProbe
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.nio.charset.StandardCharsets
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * The broadcast rung, exercised over the loopback interface.
 *
 * A real socket rather than a fake one, because the thing worth testing is that the responder binds,
 * receives and answers on the same datagram socket the phone will be talking to. A fake would prove
 * only that the string formatting works, and that is already covered in :protocol.
 */
class ReceiverBroadcastResponderTest {
    private val loopback = InetAddress.getLoopbackAddress() as Inet4Address

    @Test
    fun `a probe is answered with this receiver's name and service port`() {
        val responder = ReceiverBroadcastResponder(
            address = loopback,
            servicePort = 47_855,
            modelName = { "Fire TV Stick 4K" },
            listenPort = 0,
        )
        assertTrue(responder.start().isSuccess)
        try {
            val announcement = probe(responder.boundPort)
            assertNotNull(announcement)
            assertEquals("Fire TV Stick 4K", announcement.modelName)
            assertEquals(47_855, announcement.port)
        } finally {
            responder.close()
        }
    }

    @Test
    fun `anything that is not the probe is ignored rather than answered`() {
        val responder = ReceiverBroadcastResponder(
            address = loopback,
            servicePort = 47_855,
            modelName = { "Fire TV" },
            listenPort = 0,
        )
        assertTrue(responder.start().isSuccess)
        try {
            assertNull(probe(responder.boundPort, request = "GET / HTTP/1.1"))
        } finally {
            responder.close()
        }
    }

    @Test
    fun `a name carrying a field separator cannot split the answer`() {
        val responder = ReceiverBroadcastResponder(
            address = loopback,
            servicePort = 47_855,
            modelName = { "Fire\tTV\nStick" },
            listenPort = 0,
        )
        assertTrue(responder.start().isSuccess)
        try {
            assertEquals("Fire TV Stick", probe(responder.boundPort)?.modelName)
        } finally {
            responder.close()
        }
    }

    @Test
    fun `starting twice is refused rather than leaking the first socket`() {
        val responder = ReceiverBroadcastResponder(loopback, 47_855, { "Fire TV" }, listenPort = 0)
        assertTrue(responder.start().isSuccess)
        try {
            assertTrue(responder.start().isFailure)
        } finally {
            responder.close()
        }
    }

    @Test
    fun `closing twice is harmless`() {
        val responder = ReceiverBroadcastResponder(loopback, 47_855, { "Fire TV" }, listenPort = 0)
        responder.start()
        responder.close()
        responder.close()
    }

    private fun probe(
        port: Int,
        request: String = ReceiverProbe.REQUEST_LINE,
    ): com.rextechnologies.flint.protocol.discovery.ReceiverAnnouncement? {
        DatagramSocket(InetSocketAddress(loopback, 0)).use { client ->
            client.soTimeout = TIMEOUT_MILLIS
            val payload = "$request\n".toByteArray(StandardCharsets.US_ASCII)
            client.send(DatagramPacket(payload, payload.size, loopback, port))

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
