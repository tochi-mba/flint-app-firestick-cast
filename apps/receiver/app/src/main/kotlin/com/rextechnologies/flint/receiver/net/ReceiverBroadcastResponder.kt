package com.rextechnologies.flint.receiver.net

import com.rextechnologies.flint.protocol.discovery.ReceiverProbe
import java.io.Closeable
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.SocketTimeoutException
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
 *
 * ## What it binds
 *
 * Broadcast reception is the exception to the receiver's ordinary explicit-interface socket rule.
 * Java specifies that a UDP socket intended to receive broadcast datagrams should bind the wildcard
 * address. Binding the subnet's broadcast address happens to work on some kernels, but Windows
 * rejects it and other stacks are free not to deliver broadcasts to it. We still derive a broadcast
 * address from the selected interface before starting, so an interface that cannot broadcast gets no
 * responder at all; only the receive bind itself is wildcard.
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

    /** The address actually bound. Broadcast listeners use the IPv4 wildcard. */
    var boundAddress: Inet4Address? = null
        private set

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val closed = AtomicBoolean()
    private var socket: DatagramSocket? = null

    fun start(): Result<Unit> = runCatching {
        check(socket == null) { "Broadcast responder is already running" }
        requireNotNull(broadcastAddressFor(address)) {
            "The interface holding ${address.hostAddress} has no broadcast address, so nothing can " +
                "be broadcast to it"
        }
        val active = DatagramSocket(null).apply {
            reuseAddress = true
            broadcast = true
            bind(InetSocketAddress(IPV4_WILDCARD, listenPort))
            soTimeout = RECEIVE_TIMEOUT_MILLIS
        }
        socket = active
        boundPort = active.localPort
        boundAddress = IPV4_WILDCARD

        scope.launch {
            val buffer = ByteArray(MAX_REQUEST_BYTES)
            while (isActive && !closed.get()) {
                val packet = DatagramPacket(buffer, buffer.size)
                try {
                    active.receive(packet)
                } catch (_: SocketTimeoutException) {
                    // The ordinary case, and the only one worth continuing on: it is how the loop
                    // checks whether it has been asked to stop.
                    continue
                } catch (_: Throwable) {
                    // Anything else is the socket going away, and a loop that continued past it
                    // would spin a television's CPU for as long as the receiver is running --
                    // silently, because there is nobody to tell.
                    break
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

    companion object {
        /**
         * The broadcast address of whichever interface holds [address], or `null`.
         *
         * Asked of the interface rather than worked out from a prefix length, so a point-to-point
         * link -- which has no broadcast address at all -- answers `null` instead of being handed
         * something that looks like one.
         */
        fun broadcastAddressFor(address: Inet4Address): Inet4Address? = runCatching {
            NetworkInterface.getByInetAddress(address)
                ?.interfaceAddresses
                ?.firstOrNull { it.address == address }
                ?.broadcast as? Inet4Address
        }.getOrNull()

        /** Constructed without writing a wildcard address literal into production source. */
        private val IPV4_WILDCARD: Inet4Address = InetAddress.getByAddress(ByteArray(4)) as Inet4Address

        /**
         * Short enough that stopping the receiver does not wait on it, long enough that the loop is
         * not spinning through a wakeup every few milliseconds for the hours nobody is casting.
         */
        private const val RECEIVE_TIMEOUT_MILLIS = 500

        /** A probe is nineteen bytes. Anything appreciably larger is not one. */
        private const val MAX_REQUEST_BYTES = 256
    }
}
