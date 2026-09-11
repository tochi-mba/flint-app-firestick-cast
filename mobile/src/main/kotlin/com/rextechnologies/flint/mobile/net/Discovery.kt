package com.rextechnologies.flint.mobile.net

import android.content.Context
import android.net.wifi.WifiManager
import com.rextechnologies.flint.castcore.capability.DiscoverySource
import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.ReceiverDevice
import com.rextechnologies.flint.castcore.discovery.DiscoveryLadder
import com.rextechnologies.flint.castcore.discovery.DiscoveryRung
import com.rextechnologies.flint.castcore.discovery.DiscoveryStep
import com.rextechnologies.flint.protocol.discovery.DnsPacketCodec
import com.rextechnologies.flint.protocol.discovery.FlintDnsSd
import com.rextechnologies.flint.protocol.discovery.ReceiverProbe
import com.rextechnologies.flint.protocol.network.InterfaceSocketBinder
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.withContext
import java.io.BufferedReader
import java.io.InputStreamReader
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.MulticastSocket
import java.net.NetworkInterface
import java.nio.charset.StandardCharsets

/** What one rung of the ladder produced, kept separately so diagnostics can say which one worked. */
data class RungResult(
    val rung: DiscoveryRung,
    val receivers: List<ReceiverDevice>,
    val attempted: Boolean,
    val detail: String = "",
)

/**
 * Runs the discovery ladder against a real network.
 *
 * Every socket opened here is bound to the address the ladder handed down, and none of them is ever
 * left to the process default. While the phone is tethering from mobile data that default is the
 * cellular network, so an unbound socket leaves over the wide-area network and reaches neither the
 * television nor anything else useful. `ConnectivityManager.bindProcessToNetwork` does not fix it,
 * because a tether interface is not a `Network` the framework exposes.
 */
class DiscoveryRunner(context: Context) {
    private val wifi = context.applicationContext.getSystemService(WifiManager::class.java)

    /**
     * Walks the ladder and stops at the first rung that finds a receiver.
     *
     * Stopping early is not an optimisation. Every probe competes with the cast stream for the same
     * radio, and running four more rungs after the first has already answered is four more rungs of
     * contention for no new information.
     */
    suspend fun discover(network: LocalNetwork): List<RungResult> {
        val plan = DiscoveryLadder.plan(network)
        val results = mutableListOf<RungResult>()
        for (step in plan) {
            if (step.rung == DiscoveryRung.MANUAL) break
            val result = runStep(step)
            results += result
            if (result.receivers.isNotEmpty()) break
        }
        return results
    }

    private suspend fun runStep(step: DiscoveryStep): RungResult = when (step) {
        is DiscoveryStep.LineProbe -> sweep(step)
        is DiscoveryStep.MulticastDns -> multicastDns(step)
        is DiscoveryStep.UdpBroadcast -> broadcast(step)
        is DiscoveryStep.Ssdp -> RungResult(
            step.rung,
            emptyList(),
            attempted = false,
            detail = "Only searched when no Flint receiver answered, and it finds a DLNA renderer " +
                "rather than a Flint receiver.",
        )

        is DiscoveryStep.Manual -> RungResult(step.rung, emptyList(), attempted = false)
    }

    /**
     * The plaintext sweep, and the rung that actually works on a hotspot.
     *
     * The receiver answers this on its ordinary port and treats a probe as a non-event, so sweeping a
     * subnet cannot make the television flicker between screens. Concurrency and the connect timeout
     * both come from the budget the ladder derived rather than from numbers chosen here.
     */
    private suspend fun sweep(step: DiscoveryStep.LineProbe): RungResult {
        val budget = step.sweep
        val binder = InterfaceSocketBinder(step.boundAddress)
        val gate = Semaphore(budget.concurrency)

        val found = coroutineScope {
            subnetHosts(step, budget.hostCount).map { host ->
                async(Dispatchers.IO) {
                    gate.withPermit { probeOne(binder, host, budget.connectTimeoutMillis) }
                }
            }.awaitAll()
        }.filterNotNull()

        return RungResult(step.rung, found, attempted = true, detail = "Swept ${budget.hostCount} addresses.")
    }

    private fun subnetHosts(step: DiscoveryStep, limit: Int): List<Inet4Address> =
        step.subnet.hosts(maximumHosts = limit).toList()

    private fun probeOne(
        binder: InterfaceSocketBinder,
        host: Inet4Address,
        timeoutMillis: Int,
    ): ReceiverDevice? = try {
        binder.newOutgoingSocket().use { socket ->
            socket.tcpNoDelay = true
            socket.soTimeout = timeoutMillis
            socket.connect(InetSocketAddress(host, ReceiverProbe.PORT), timeoutMillis)
            socket.getOutputStream().apply {
                write(ReceiverProbe.requestBytes())
                flush()
            }
            val line = BufferedReader(
                InputStreamReader(socket.getInputStream(), StandardCharsets.UTF_8),
                ReceiverProbe.MAX_RESPONSE_BYTES,
            ).readLine()
            line?.let(ReceiverProbe::parseResponse)?.let { announcement ->
                ReceiverDevice(
                    address = host.hostAddress.orEmpty(),
                    friendlyName = announcement.modelName,
                    source = DiscoverySource.LINE_PROBE,
                    port = announcement.port,
                    receiverAnswered = true,
                )
            }
        }
    } catch (_: Throwable) {
        // A sweep talks to whatever happens to have a port open. A refusal, a timeout and a stranger
        // answering with something else are all the ordinary case, not errors worth unwinding for.
        null
    }

    /**
     * Multicast DNS, held up by a lock Android will not give back on its own.
     *
     * Without a `MulticastLock` the Wi-Fi chip filters multicast out before it reaches the app, and
     * the symptom is not an error — it is a rung that silently finds nothing.
     */
    private suspend fun multicastDns(step: DiscoveryStep.MulticastDns): RungResult = withContext(Dispatchers.IO) {
        val lock = wifi?.createMulticastLock(MULTICAST_LOCK_TAG)?.apply {
            setReferenceCounted(false)
            runCatching { acquire() }
        }
        try {
            RungResult(step.rung, queryMulticastDns(step.boundAddress), attempted = true)
        } catch (_: Throwable) {
            RungResult(step.rung, emptyList(), attempted = true, detail = "Multicast did not reach this phone.")
        } finally {
            runCatching { lock?.release() }
        }
    }

    private fun queryMulticastDns(boundAddress: Inet4Address): List<ReceiverDevice> {
        val group = InetAddress.getByName(MDNS_GROUP)
        val networkInterface = NetworkInterface.getByInetAddress(boundAddress)
        // Bound to the chosen address rather than the wildcard, matching what the receiver does on
        // its side. The group membership below is what pins which interface the answers arrive on.
        val socket = MulticastSocket(null)
        return socket.use {
            it.reuseAddress = true
            it.bind(InetSocketAddress(boundAddress, MDNS_PORT))
            networkInterface?.let { chosen -> it.networkInterface = chosen }
            it.joinGroup(InetSocketAddress(group, MDNS_PORT), networkInterface)
            it.soTimeout = MDNS_TIMEOUT_MILLIS

            val query = DnsPacketCodec.encode(FlintDnsSd.query())
            it.send(DatagramPacket(query, query.size, group, MDNS_PORT))

            val found = linkedMapOf<String, ReceiverDevice>()
            val buffer = ByteArray(DnsPacketCodec.MAX_PACKET_BYTES)
            val deadline = System.nanoTime() + MDNS_TIMEOUT_MILLIS * 1_000_000L
            while (System.nanoTime() < deadline) {
                val packet = DatagramPacket(buffer, buffer.size)
                try {
                    it.receive(packet)
                } catch (_: Throwable) {
                    break
                }
                val decoded = runCatching {
                    DnsPacketCodec.decode(packet.data.copyOf(packet.length))
                }.getOrNull() ?: continue
                for (service in FlintDnsSd.extractServices(decoded)) {
                    val address = service.address.hostAddress ?: continue
                    found[address] = ReceiverDevice(
                        address = address,
                        friendlyName = service.instanceName,
                        source = DiscoverySource.MULTICAST_DNS,
                        port = service.port,
                        receiverAnswered = true,
                    )
                }
                if (found.isNotEmpty()) break
            }
            runCatching { it.leaveGroup(InetSocketAddress(group, MDNS_PORT), networkInterface) }
            found.values.toList()
        }
    }

    /**
     * The broadcast rung.
     *
     * It needs a responder on the receiver that did not exist before this change, so on an older
     * television it finds nothing and says so rather than appearing to have looked.
     */
    private suspend fun broadcast(step: DiscoveryStep.UdpBroadcast): RungResult = withContext(Dispatchers.IO) {
        val target = step.broadcastAddress
        try {
            val socket = DatagramSocket(InetSocketAddress(step.boundAddress, 0))
            val found = socket.use {
                it.broadcast = true
                it.soTimeout = BROADCAST_TIMEOUT_MILLIS
                val request = ReceiverProbe.requestBytes()
                it.send(DatagramPacket(request, request.size, target, ReceiverProbe.PORT))

                val buffer = ByteArray(ReceiverProbe.MAX_RESPONSE_BYTES)
                val answers = linkedMapOf<String, ReceiverDevice>()
                val deadline = System.nanoTime() + BROADCAST_TIMEOUT_MILLIS * 1_000_000L
                while (System.nanoTime() < deadline) {
                    val packet = DatagramPacket(buffer, buffer.size)
                    try {
                        it.receive(packet)
                    } catch (_: Throwable) {
                        break
                    }
                    val text = String(packet.data, 0, packet.length, StandardCharsets.UTF_8)
                    val announcement = ReceiverProbe.parseResponse(text) ?: continue
                    val address = packet.address?.hostAddress ?: continue
                    answers[address] = ReceiverDevice(
                        address = address,
                        friendlyName = announcement.modelName,
                        source = DiscoverySource.BROADCAST,
                        port = announcement.port,
                        receiverAnswered = true,
                    )
                }
                answers.values.toList()
            }
            RungResult(step.rung, found, attempted = true)
        } catch (_: Throwable) {
            RungResult(step.rung, emptyList(), attempted = true, detail = "The broadcast was not answered.")
        }
    }

    /** Confirms one typed-in address, which is the rung that is always available. */
    suspend fun probeManual(
        network: LocalNetwork,
        address: String,
        port: Int = ReceiverProbe.PORT,
    ): ReceiverDevice? = withContext(Dispatchers.IO) {
        val bound = network.boundAddress ?: return@withContext null
        val target = runCatching { InetAddress.getByName(address) as? Inet4Address }.getOrNull()
            ?: return@withContext null
        val binder = InterfaceSocketBinder(bound)
        probeOne(binder, target, MANUAL_TIMEOUT_MILLIS)?.copy(
            source = DiscoverySource.MANUAL,
            port = port,
        )
    }

    /**
     * The round trip, measured by this phone.
     *
     * The receiver reports zero in every STATS frame because it never measures one, and passing that
     * through would put a zero on a diagnostics row, which reads as an extraordinarily good link
     * rather than as no measurement at all. So the phone times its own connect-and-answer instead,
     * and the row says where the number came from.
     */
    suspend fun measureRoundTripMillis(
        network: LocalNetwork,
        device: ReceiverDevice,
        samples: Int = ROUND_TRIP_ATTEMPTS,
    ): List<Double> = withContext(Dispatchers.IO) {
        val bound = network.boundAddress ?: return@withContext emptyList()
        val target = runCatching { InetAddress.getByName(device.address) as? Inet4Address }.getOrNull()
            ?: return@withContext emptyList()
        val binder = InterfaceSocketBinder(bound)
        buildList {
            repeat(samples) {
                val started = System.nanoTime()
                val answered = probeOne(binder, target, MANUAL_TIMEOUT_MILLIS) != null
                if (answered) add((System.nanoTime() - started) / 1_000_000.0)
            }
        }
    }

    companion object {
        /**
         * How many times the round trip is sampled.
         *
         * Public because the loss figure is the delivered fraction of these, and a caller working
         * that out from a number of its own would be reporting loss against a denominator this
         * class did not use.
         */
        const val ROUND_TRIP_ATTEMPTS: Int = 5

        private const val MULTICAST_LOCK_TAG = "flint-mobile-discovery"
        private const val MDNS_GROUP = "224.0.0.251"
        private const val MDNS_PORT = 5353
        private const val MDNS_TIMEOUT_MILLIS = 1_500
        private const val BROADCAST_TIMEOUT_MILLIS = 1_000
        private const val MANUAL_TIMEOUT_MILLIS = 1_500
    }
}
