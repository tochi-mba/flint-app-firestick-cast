package com.rextechnologies.flint.receiver.net

import android.os.Build
import com.rextechnologies.flint.protocol.discovery.DnsPacketCodec
import com.rextechnologies.flint.protocol.discovery.FlintDnsSd
import com.rextechnologies.flint.protocol.discovery.FlintService
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.Closeable
import java.net.DatagramPacket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.MulticastSocket
import java.net.NetworkInterface
import java.net.SocketException
import java.util.concurrent.atomic.AtomicBoolean

/** Interface-pinned DNS-SD responder; TCP discovery remains available if multicast is filtered. */
class ReceiverMdnsResponder(
    private val address: Inet4Address,
    private val port: Int,
    private val browserPort: Int = 0,
) : Closeable {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val closed = AtomicBoolean()
    private var socket: MulticastSocket? = null

    fun start(): Result<Unit> = runCatching {
        check(socket == null) { "mDNS responder is already running" }
        val network = NetworkInterface.getByInetAddress(address)
            ?: error("No interface owns the receiver address")
        val group = InetAddress.getByName(MDNS_GROUP)
        val active = MulticastSocket(null).apply {
            reuseAddress = true
            bind(InetSocketAddress(address, MDNS_PORT))
            networkInterface = network
            soTimeout = RECEIVE_TIMEOUT_MILLIS
            joinGroup(InetSocketAddress(group, MDNS_PORT), network)
        }
        socket = active
        announce(active, ttl = DEFAULT_TTL_SECONDS)
        scope.launch {
            while (isActive && !closed.get()) {
                val buffer = ByteArray(DnsPacketCodec.MAX_PACKET_BYTES)
                val packet = DatagramPacket(buffer, buffer.size)
                try {
                    active.receive(packet)
                    val query = DnsPacketCodec.decode(
                        packet.data.copyOfRange(packet.offset, packet.offset + packet.length),
                    )
                    if (query.questions.any {
                            it.name.trimEnd('.').equals(FlintDnsSd.SERVICE_TYPE, ignoreCase = true)
                        }
                    ) {
                        announce(active, ttl = DEFAULT_TTL_SECONDS)
                    }
                } catch (_: java.net.SocketTimeoutException) {
                    // Periodically observe cancellation.
                } catch (_: IllegalArgumentException) {
                    // Unrelated multicast traffic is not a receiver error.
                } catch (_: SocketException) {
                    break
                }
            }
        }
    }

    private fun announce(active: MulticastSocket, ttl: Long) {
        val model = Build.MODEL.orEmpty().replace(Regex("[^A-Za-z0-9 _-]"), " ").trim()
            .ifBlank { "Fire TV" }
        val host = "rexcast-" + address.hostAddress.orEmpty().replace(".", "-")
        val attributes = buildMap {
            put("version", "1")
            put("pairing", "code")
            if (browserPort in 1..65_535) {
                put("browser_port", browserPort.toString())
                put("browser_protocol", "2")
            }
        }
        val payload = DnsPacketCodec.encode(
            FlintDnsSd.announcement(
                FlintService(
                    instanceName = model.take(48),
                    hostName = host.take(63),
                    port = port,
                    address = address,
                    attributes = attributes,
                    ttlSeconds = ttl,
                ),
            ),
        )
        val group = InetAddress.getByName(MDNS_GROUP)
        active.send(DatagramPacket(payload, payload.size, group, MDNS_PORT))
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) return
        socket?.let { active ->
            runCatching { announce(active, ttl = 0) }
            runCatching {
                active.leaveGroup(
                    InetSocketAddress(InetAddress.getByName(MDNS_GROUP), MDNS_PORT),
                    active.networkInterface,
                )
            }
            active.close()
        }
        socket = null
        scope.cancel()
    }

    private companion object {
        const val MDNS_GROUP = "224.0.0.251"
        const val MDNS_PORT = 5_353
        const val DEFAULT_TTL_SECONDS = 120L
        const val RECEIVE_TIMEOUT_MILLIS = 500
    }
}
