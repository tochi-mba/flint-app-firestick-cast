package com.rextechnologies.flint.receiver.net

import android.os.Build
import com.rextechnologies.flint.protocol.discovery.ReceiverProbe
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.BufferedInputStream
import java.io.Closeable
import java.net.Inet4Address
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.ServerSocket
import java.net.Socket
import java.net.SocketException
import java.util.Collections

/** Small, explicitly bound probe endpoint used before authenticated pairing begins. */
class ReceiverDiscoveryServer(
    private val address: Inet4Address,
    requestedPort: Int = DEFAULT_PORT,
) : Closeable {
    var port: Int = requestedPort
        private set
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private var server: ServerSocket? = null

    fun start(): Result<Unit> = runCatching {
        check(server == null) { "Discovery server is already running" }
        val bound = ServerSocket().apply {
            reuseAddress = true
            bind(InetSocketAddress(address, port), 8)
        }
        port = bound.localPort
        server = bound
        scope.launch {
            while (isActive) {
                val client = try {
                    bound.accept()
                } catch (_: SocketException) {
                    break
                }
                // A client that connects and then says nothing times out, and one that vanishes
                // throws. Neither is this server's failure, but this launch is a child of the
                // accept loop, so either used to cancel it -- and one stalled connection took
                // discovery down for the rest of the session.
                launch { runCatching { answer(client) } }
            }
        }
    }

    private fun answer(client: Socket) {
        client.use { socket ->
            socket.soTimeout = CLIENT_TIMEOUT_MILLIS
            // Bounded: this answers anyone who connects, so the allocation is theirs to trigger
            // and ours to refuse.
            val request = ReceiverProbe.readLine(
                BufferedInputStream(socket.getInputStream()),
                ReceiverProbe.MAX_REQUEST_BYTES,
            ).orEmpty()
            if (!ReceiverProbe.isRequest(request)) return
            // Through ReceiverProbe, which is the only place that knows the byte budget and that
            // a carriage return splits the record too -- this replaced tabs and line feeds and
            // let carriage returns through, and capped nothing at all.
            socket.getOutputStream().apply {
                write(ReceiverProbe.responseBytes(Build.MODEL.orEmpty(), port))
                flush()
            }
        }
    }

    override fun close() {
        server?.close()
        server = null
        scope.cancel()
    }

    companion object {
        // One definition of the probe's wire format, in the module both ends share.
        const val DEFAULT_PORT = ReceiverProbe.PORT
        const val DISCOVERY_REQUEST = ReceiverProbe.REQUEST_LINE
        const val DISCOVERY_RESPONSE = ReceiverProbe.RESPONSE_PREFIX
        private const val CLIENT_TIMEOUT_MILLIS = 2_000

        fun findLocalAddress(): Inet4Address? {
            val interfaces = try {
                Collections.list(NetworkInterface.getNetworkInterfaces())
            } catch (_: SocketException) {
                return null
            }
            return interfaces.asSequence()
                .filter {
                    try {
                        // Point-to-point is a VPN tunnel, which no phone in the room is on.
                        it.isUp && !it.isLoopback && !it.isVirtual && !it.isPointToPoint
                    } catch (_: SocketException) {
                        false
                    }
                }
                .flatMap { Collections.list(it.inetAddresses).asSequence() }
                .filterIsInstance<Inet4Address>()
                .firstOrNull { it.isSiteLocalAddress && !it.isLoopbackAddress }
        }
    }
}
