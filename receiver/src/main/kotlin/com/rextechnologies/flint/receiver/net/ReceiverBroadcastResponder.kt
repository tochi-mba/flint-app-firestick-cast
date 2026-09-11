package com.rextechnologies.flint.receiver.net

import com.rextechnologies.flint.protocol.discovery.ReceiverProbe
import java.io.Closeable
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
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
 * ## What it binds, and why it is not the address every other listener here binds
 *
 * Every other socket in this application binds the television's own address. This one binds the
 * interface's **broadcast** address, because that is the address the datagram is sent to, and a
 * socket bound to a unicast address never sees it: the kernel matches the destination address of an
 * arriving datagram against the socket's bound address, and 192.0.2.255 is not 192.0.2.2. Bound to
 * the unicast address this class received every unicast datagram anybody sent it and not one of the
 * broadcasts it exists to answer -- and the loopback interface, which its tests used, has no
 * broadcast address at all, so nothing noticed.
 *
 * This is not the wildcard the project's rule forbids. The address is derived from the same
 * interface the rest of the receiver is bound to, it is one address rather than all of them, and an
 * interface with no broadcast address of its own gets no responder rather than a wildcard one.
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

    /** The address actually bound, for the log line that says where answers will come from. */
    var boundAddress: Inet4Address? = null
        private set

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val closed = AtomicBoolean()
    private var socket: DatagramSocket? = null

    fun start(): Result<Unit> = runCatching {
        check(socket == null) { "Broadcast responder is already running" }
        val listenAddress = requireNotNull(broadcastAddressFor(address)) {
            "The interface holding ${address.hostAddress} has no broadcast address, so nothing can " +
                "be broadcast to it"
        }
        val active = DatagramSocket(null).apply {
            reuseAddress = true
            bind(InetSocketAddress(listenAddress, listenPort))
            soTimeout = RECEIVE_TIMEOUT_MILLIS
        }
        socket = active
        boundPort = active.localPort
        boundAddress = listenAddress

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

        /**
         * Short enough that stopping the receiver does not wait on it, long enough that the loop is
         * not spinning through a wakeup every few milliseconds for the hours nobody is casting.
         */
        private const val RECEIVE_TIMEOUT_MILLIS = 500

        /** A probe is nineteen bytes. Anything appreciably larger is not one. */
        private const val MAX_REQUEST_BYTES = 256
    }
}
