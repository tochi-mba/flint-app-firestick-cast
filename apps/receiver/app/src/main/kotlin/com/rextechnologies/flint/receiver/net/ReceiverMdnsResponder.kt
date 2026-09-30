package com.rextechnologies.flint.receiver.net

import android.content.Context
import android.os.Build
import com.rextechnologies.flint.protocol.discovery.DnsPacketCodec
import com.rextechnologies.flint.protocol.discovery.FlintDnsSd
import com.rextechnologies.flint.protocol.discovery.FlintService
import com.rextechnologies.flint.protocol.discovery.MulticastDnsSocket
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.Closeable
import java.net.Inet4Address
import java.net.SocketException
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Interface-pinned DNS-SD responder; TCP discovery remains available if multicast is filtered.
 *
 * @property holdMulticast takes whatever the platform needs to deliver multicast to this app, and
 *   returns the handle that gives it back. On Android that is the Wi-Fi multicast lock
 *   ([WifiMulticast]): without it a Wi-Fi TV hears no query at all. Held from start to close.
 */
class ReceiverMdnsResponder(
    private val address: Inet4Address,
    private val port: Int,
    private val browserPort: Int = 0,
    private val holdMulticast: () -> AutoCloseable,
) : Closeable {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val closed = AtomicBoolean()
    private var socket: MulticastDnsSocket? = null
    private var multicast: AutoCloseable? = null

    fun start(): Result<Unit> = runCatching {
        check(socket == null) { "mDNS responder is already running" }
        val held = holdMulticast()
        val active = try {
            MulticastDnsSocket.open(address, RECEIVE_TIMEOUT_MILLIS)
        } catch (failure: Exception) {
            held.close()
            throw failure
        }
        multicast = held
        socket = active
        announce(active, ttl = DEFAULT_TTL_SECONDS, destination = null)
        scope.launch {
            while (isActive && !closed.get()) {
                try {
                    val received = active.receive() ?: continue
                    val query = DnsPacketCodec.decode(received.payload)
                    if (query.questions.any {
                            it.name.trimEnd('.').equals(FlintDnsSd.SERVICE_TYPE, ignoreCase = true)
                        }
                    ) {
                        announce(
                            active,
                            ttl = DEFAULT_TTL_SECONDS,
                            destination = received.source.takeIf { received.wantsDirectReply },
                        )
                    }
                } catch (_: IllegalArgumentException) {
                    // Unrelated multicast traffic is not a receiver error.
                } catch (_: SocketException) {
                    break
                }
            }
        }
    }

    private fun announce(
        active: MulticastDnsSocket,
        ttl: Long,
        destination: java.net.InetSocketAddress?,
    ) {
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
        if (destination == null) {
            active.sendToGroup(payload)
        } else {
            active.sendTo(payload, destination)
        }
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) return
        socket?.let { active ->
            runCatching { announce(active, ttl = 0, destination = null) }
            active.close()
        }
        socket = null
        scope.cancel()
        multicast?.let { runCatching { it.close() } }
        multicast = null
    }

    companion object {
        /**
         * The responder a receiver runs. It holds the Wi-Fi multicast lock from start to close, so a
         * Wi-Fi TV hears the queries it is there to answer.
         */
        fun onWifi(context: Context, address: Inet4Address, port: Int, browserPort: Int): ReceiverMdnsResponder =
            ReceiverMdnsResponder(address, port, browserPort) { WifiMulticast.hold(context, MULTICAST_LOCK_TAG) }

        private const val DEFAULT_TTL_SECONDS = 120L
        private const val RECEIVE_TIMEOUT_MILLIS = 500

        /** Names the lock the way `dumpsys wifi` lists it. */
        private const val MULTICAST_LOCK_TAG = "flint-receiver-mdns"
    }
}
