package com.rextechnologies.flint.receiver.net

import android.os.Build
import com.rextechnologies.flint.protocol.discovery.ReceiverProbe
import java.io.BufferedInputStream
import java.io.Closeable
import java.net.Inet4Address
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.ServerSocket
import java.net.SocketException
import java.util.Collections
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

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
                launch {
                    client.use { socket ->
                        socket.soTimeout = CLIENT_TIMEOUT_MILLIS
                        // Bounded: this answers anyone who connects, so the allocation is theirs
                        // to trigger and ours to refuse.
                        val request = ReceiverProbe.readLine(
                            BufferedInputStream(socket.getInputStream()),
                            ReceiverProbe.MAX_REQUEST_BYTES,
                        ).orEmpty()
                        if (request == DISCOVERY_REQUEST) {
                            val safeName = Build.MODEL.replace('\t', ' ').replace('\n', ' ')
                            socket.getOutputStream().bufferedWriter().apply {
                                write("$DISCOVERY_RESPONSE\t$safeName\t$port\n")
                                flush()
                            }
                        }
                    }
                }
            }
        }
    }

    override fun close() {
        server?.close()
        server = null
        scope.cancel()
    }

    companion object {
        const val DEFAULT_PORT = 47_855
        const val DISCOVERY_REQUEST = "REXCAST DISCOVER/1"
        const val DISCOVERY_RESPONSE = "REXCAST RECEIVER/1"
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

