package com.rextechnologies.flint.receiver.net

import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import java.net.Inet4Address
import java.net.InetSocketAddress
import java.net.Socket
import kotlin.test.Test
import kotlin.test.assertContains
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35])
class ReceiverDiscoveryServerTest {
    private val loopback = java.net.InetAddress.getByName("127.0.0.1") as Inet4Address

    @Test
    fun `valid probe returns version device name and actual listening port`() {
        ReceiverDiscoveryServer(loopback, 0).use { server ->
            assertTrue(server.start().isSuccess)
            val response = request(server.port, ReceiverDiscoveryServer.DISCOVERY_REQUEST)
            assertContains(response, ReceiverDiscoveryServer.DISCOVERY_RESPONSE)
            assertTrue(response.trimEnd().endsWith("\t${server.port}"))
        }
    }

    @Test
    fun `unknown probe receives no capability data and double start fails`() {
        ReceiverDiscoveryServer(loopback, 0).use { server ->
            assertTrue(server.start().isSuccess)
            assertTrue(server.start().isFailure)
            assertEquals("", request(server.port, "GET / HTTP/1.1"))
        }
    }

    @Test
    fun `address lookup is safe and never returns public or loopback endpoints`() {
        val address = ReceiverDiscoveryServer.findLocalAddress()
        if (address != null) {
            assertTrue(address.isSiteLocalAddress)
            assertTrue(!address.isLoopbackAddress)
        } else {
            assertNull(address)
        }
    }

    private fun request(port: Int, line: String): String = Socket().use { socket ->
        socket.connect(InetSocketAddress(loopback, port), 2_000)
        socket.soTimeout = 2_000
        socket.getOutputStream().bufferedWriter().apply {
            write(line)
            write("\n")
            flush()
        }
        socket.shutdownOutput()
        socket.getInputStream().readBytes().toString(Charsets.UTF_8)
    }
}
