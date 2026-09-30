package com.rextechnologies.flint.protocol.discovery

import com.rextechnologies.flint.protocol.network.Ipv4Subnet
import java.net.DatagramPacket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.MulticastSocket
import java.net.NetworkInterface

/**
 * A multicast DNS socket on one interface, opened the one way that actually receives on Linux.
 *
 * The receiver and the phone each used to bind their mDNS socket to the interface's own address,
 * following the rule that every socket is pinned to the chosen interface. On Linux, and so on
 * Android, a UDP socket bound to a unicast address is handed only datagrams sent to that address.
 * A query sent to the group is not one of those, so the receiver never heard a query and the phone
 * never heard an answer. Neither logged anything; a socket that receives nothing looks exactly like
 * a network with nothing on it, which is what the television being switched off looks like too.
 *
 * So the receive side binds the multicast group rather than a unicast interface address. This is
 * narrower than a wildcard bind: the kernel can hand it only traffic addressed to Flint's one mDNS
 * group. The group is joined on one validated interface and sends leave through that interface.
 * Some kernels can still over-deliver group traffic when another socket joined the same group on a
 * different interface, so every received datagram is checked against this interface's subnet too.
 */
class MulticastDnsSocket private constructor(
    private val receiver: MulticastSocket,
    private val sender: MulticastSocket,
    private val networkInterface: NetworkInterface,
    private val subnet: Ipv4Subnet,
    private val receiveTimeoutMillis: Int,
) : AutoCloseable {
    /** The interface's own address, which is what a unicast reply to a querier leaves from. */
    val localAddress: Inet4Address get() = subnet.localAddress

    /** Sends [payload] to the mDNS group, through this socket's interface. */
    fun sendToGroup(payload: ByteArray) {
        sender.send(DatagramPacket(payload, payload.size, GROUP_ENDPOINT))
    }

    /** Sends [payload] to one host, which is how a querier on an ordinary port gets its answer. */
    fun sendTo(payload: ByteArray, destination: InetSocketAddress) {
        sender.send(DatagramPacket(payload, payload.size, destination))
    }

    /**
     * Waits for one datagram from a host on this interface's subnet.
     *
     * Returns `null` when the socket's timeout passes with nothing to report. Datagrams from other
     * subnets are the kernel's over-delivery described above and are skipped without ending the
     * wait, so a busy neighbour interface cannot starve this one of its timeout.
     */
    fun receive(): Received? {
        val buffer = ByteArray(DnsPacketCodec.MAX_PACKET_BYTES)
        val deadline = System.nanoTime() + receiveTimeoutMillis * NANOS_PER_MILLI
        while (true) {
            val remaining = ((deadline - System.nanoTime()) / NANOS_PER_MILLI).toInt()
            if (remaining <= 0) return null
            receiver.soTimeout = remaining
            val packet = DatagramPacket(buffer, buffer.size)
            try {
                receiver.receive(packet)
            } catch (_: java.net.SocketTimeoutException) {
                return null
            }
            accept(subnet, packet)?.let { return it }
        }
    }

    override fun close() {
        runCatching { receiver.leaveGroup(GROUP_ENDPOINT, networkInterface) }
        receiver.close()
        sender.close()
    }

    /** One datagram and who sent it. */
    data class Received(val payload: ByteArray, val source: InetSocketAddress) {
        /**
         * Whether the sender is a full mDNS participant, which answers arrive at through the group,
         * or an ordinary socket that can only be answered directly (RFC 6762 §6.7).
         */
        val wantsDirectReply: Boolean get() = source.port != PORT

        override fun equals(other: Any?): Boolean =
            other is Received && payload.contentEquals(other.payload) && source == other.source

        override fun hashCode(): Int = 31 * payload.contentHashCode() + source.hashCode()
    }

    companion object {
        const val GROUP: String = "224.0.0.251"
        const val PORT: Int = 5_353

        private val GROUP_ENDPOINT = InetSocketAddress(InetAddress.getByName(GROUP), PORT)
        private const val NANOS_PER_MILLI = 1_000_000L

        /**
         * Whether a datagram from [source] belongs to this socket's interface.
         *
         * Only an IPv4 sender on [subnet] qualifies. Anything else is the kernel handing over group
         * traffic that arrived on another interface, which this socket must not answer.
         */
        internal fun isOnSubnet(subnet: Ipv4Subnet, source: InetAddress?): Boolean =
            source is Inet4Address && subnet.contains(source)

        /**
         * What [receive] makes of one datagram: its payload and sender, or `null` for a sender off
         * [subnet], which [receive] skips without ending its wait.
         */
        internal fun accept(subnet: Ipv4Subnet, packet: DatagramPacket): Received? =
            if (!isOnSubnet(subnet, packet.address)) {
                null
            } else {
                Received(
                    payload = packet.data.copyOfRange(packet.offset, packet.offset + packet.length),
                    source = InetSocketAddress(packet.address, packet.port),
                )
            }

        /**
         * Opens the socket for the interface that owns [address].
         *
         * The receive-side bind is the group, for the reason the class comment gives. Everything
         * else is pinned too: the group membership, the outgoing interface, and the subnet every
         * received datagram is checked against.
         */
        fun open(address: Inet4Address, receiveTimeoutMillis: Int): MulticastDnsSocket {
            require(receiveTimeoutMillis > 0)
            val networkInterface = NetworkInterface.getByInetAddress(address)
                ?: throw IllegalArgumentException("No interface owns ${address.hostAddress}")
            val prefixLength = networkInterface.interfaceAddresses
                .firstOrNull { it.address == address }
                ?.networkPrefixLength?.toInt()
                ?: throw IllegalArgumentException("No prefix is known for ${address.hostAddress}")
            val receiver = MulticastSocket(null)
            val sender = MulticastSocket(null)
            try {
                receiver.reuseAddress = true
                receiver.bind(GROUP_ENDPOINT)
                receiver.networkInterface = networkInterface
                receiver.joinGroup(GROUP_ENDPOINT, networkInterface)

                // A group address is suitable only for receiving. Replies leave a second socket
                // whose source address and port are explicitly the selected interface and 5353.
                sender.reuseAddress = true
                sender.bind(InetSocketAddress(address, PORT))
                sender.networkInterface = networkInterface
                sender.timeToLive = MDNS_TIME_TO_LIVE
            } catch (failure: Throwable) {
                receiver.close()
                sender.close()
                throw failure
            }
            return MulticastDnsSocket(
                receiver,
                sender,
                networkInterface,
                Ipv4Subnet(address, prefixLength),
                receiveTimeoutMillis,
            )
        }

        /** The value RFC 6762 specifies. The group is link-local, so no router forwards it either way. */
        private const val MDNS_TIME_TO_LIVE = 255
    }
}
