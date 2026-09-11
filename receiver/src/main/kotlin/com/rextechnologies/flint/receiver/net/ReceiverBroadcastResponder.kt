package com.rextechnologies.flint.receiver.net

import com.rextechnologies.flint.protocol.discovery.ReceiverProbe
import java.io.Closeable
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetSocketAddress
import java.nio.charset.StandardCharsets
import java.util.concurrent.atomic.AtomicBoolean
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/**
 * Answers a UDP broadcast probe on the hotspot LAN.
 *
 * The third rung of the phone's discovery ladder, and the one that did not exist until now. The first
 * two rungs cover most cases -- a bounded TCP sweep, which always works but takes as long as the
 * subnet is wide, and multicast DNS, which is instant when multicast survives the link and silent
 * when it does not. A broadcast is the middle ground: one datagram, one answer, and it crosses some
 * access points that drop multicast.
 *
 * It speaks exactly the same two lines as the TCP probe, through [ReceiverProbe], so there is one
 * wire format for the phone to parse rather than two that could drift apart. The port number is the
 * same as well: UDP and TCP are separate namespaces, so nothing collides.
 *
 * Like the TCP probe, answering one produces no state change and no listener callback. A phone
 * looking for a television must not be able to make that television flicker between screens.
 */
class ReceiverBroadcastResponder(
    private val address: Inet4Address,
    private val servicePort: Int,
    private val modelName: () -> String,
    /**
     * The UDP port to listen on.
     *
     * A parameter only so a test can take an ephemeral one. In the app it is always the default: a
     * phone broadcasts to a fixed port because there is nothing to ask first.
     */
    private val listenPort: Int = ReceiverProbe.PORT,
) : Closeable {
    /** The port actually bound, which is the requested one unless a test asked for an ephemeral. */
    var boundPort: Int = 0
        private set

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val closed = AtomicBoolean()
    private var socket: DatagramSocket? = null

    fun start(): Result<Unit> = runCatching {
        check(socket == null) { "Broadcast responder is already running" }
        // Bound to the receiver's own site-local address rather than the wildcard, matching every
        // other listener in this app. A broadcast datagram is delivered to a socket bound to the
        // interface that received it.
        val active = DatagramSocket(null).apply {
            reuseAddress = true
            bind(InetSocketAddress(address, listenPort))
            soTimeout = RECEIVE_TIMEOUT_MILLIS
        }
        socket = active
        boundPort = active.localPort

        scope.launch {
            val buffer = ByteArray(MAX_REQUEST_BYTES)
            while (isActive && !closed.get()) {
                val packet = DatagramPacket(buffer, buffer.size)
                try {
                    active.receive(packet)
                } catch (_: Throwable) {
                    // A receive timeout is the ordinary case: it is how the loop checks whether it
                    // has been asked to stop. Anything else is a socket that is going away anyway.
                    continue
                }
                answerIfProbe(active, packet)
            }
        }
    }

    private fun answerIfProbe(socket: DatagramSocket, packet: DatagramPacket) {
        val request = String(packet.data, packet.offset, packet.length, StandardCharsets.US_ASCII)
            .trimEnd('\r', '\n')
        if (!ReceiverProbe.isRequest(request)) return

        val response = ReceiverProbe.responseBytes(modelName(), servicePort)
        runCatching {
            socket.send(DatagramPacket(response, response.size, packet.socketAddress))
        }
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) return
        scope.cancel()
        runCatching { socket?.close() }
        socket = null
    }

    private companion object {
        /**
         * Short enough that stopping the receiver does not wait on it, long enough that the loop is
         * not spinning through a wakeup every few milliseconds for the hours nobody is casting.
         */
        const val RECEIVE_TIMEOUT_MILLIS = 500

        /** A probe is nineteen bytes. Anything appreciably larger is not one. */
        const val MAX_REQUEST_BYTES = 256
    }
}
