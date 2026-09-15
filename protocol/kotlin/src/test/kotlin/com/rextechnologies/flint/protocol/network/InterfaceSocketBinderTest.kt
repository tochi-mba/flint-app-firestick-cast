package com.rextechnologies.flint.protocol.network

import java.net.BindException
import java.net.InetSocketAddress
import kotlin.concurrent.thread
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertTrue

class InterfaceSocketBinderTest {
    private val loopback = Ipv4.parse("127.0.0.1")

    @Test
    fun `server and outgoing socket are explicitly bound to selected address`() {
        val binder = InterfaceSocketBinder(loopback)
        binder.newServerSocket(0).use { server ->
            assertEquals(loopback, server.inetAddress)
            val accepted = thread {
                server.accept().use { socket ->
                    assertEquals(loopback, socket.localAddress)
                    socket.getOutputStream().write(byteArrayOf(1, 2, 3))
                }
            }
            binder.newOutgoingSocket().use { socket ->
                assertTrue(socket.isBound)
                socket.connect(InetSocketAddress(loopback, server.localPort), 2_000)
                assertEquals(loopback, socket.localAddress)
                assertContentEquals(byteArrayOf(1, 2, 3), socket.getInputStream().readBytes())
            }
            accepted.join(2_000)
            assertTrue(!accepted.isAlive)
        }
    }

    @Test
    fun `invalid bind arguments and duplicate port fail cleanly`() {
        val binder = InterfaceSocketBinder(loopback)
        assertFailsWith<IllegalArgumentException> { binder.newServerSocket(-1) }
        assertFailsWith<IllegalArgumentException> { binder.newServerSocket(65_536) }
        assertFailsWith<IllegalArgumentException> { binder.newServerSocket(0, 0) }
        binder.newServerSocket(0).use { first ->
            assertFailsWith<BindException> { binder.newServerSocket(first.localPort) }
        }
    }
}

