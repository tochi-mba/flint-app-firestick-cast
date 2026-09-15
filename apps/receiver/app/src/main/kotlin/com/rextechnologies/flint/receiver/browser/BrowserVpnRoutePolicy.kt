package com.rextechnologies.flint.receiver.browser

import com.wireguard.config.Config
import com.wireguard.config.InetNetwork
import com.wireguard.config.Peer
import java.net.InetAddress
import java.net.NetworkInterface

/**
 * WireGuard admission and LAN carve-out for Fire TV.
 *
 * Full-tunnel configs (`0.0.0.0/0`, `::/0`) are allowed. Before the tunnel goes UP, every AllowedIP
 * that covers a derived local interface network is rewritten so cast/browser control stays on Wi‑Fi.
 * A peer whose routes collapse to nothing after carve-out is rejected (LAN-only tunnel).
 */
object BrowserVpnRoutePolicy {
    fun validate(
        config: Config,
        localAddresses: List<InetAddress>,
        packageName: String,
        localNetworks: List<InetNetwork> = discoverLocalNetworks(),
    ): String? {
        if (localAddresses.isEmpty() || localNetworks.isEmpty()) {
            return "Could not verify the TV's local network routes"
        }
        val intf = config.`interface`
        if (intf.excludedApplications.isNotEmpty() ||
            (intf.includedApplications.isNotEmpty() && intf.includedApplications != setOf(packageName))
        ) {
            return "Use a WireGuard configuration without application exclusions"
        }
        if (config.peers.isEmpty() || config.peers.any { it.allowedIps.isEmpty() || !it.endpoint.isPresent }) {
            return "Each VPN peer needs an endpoint and at least one route"
        }
        for (peer in config.peers) {
            if (carveAllowedIps(peer.allowedIps, localNetworks).isEmpty()) {
                return "This VPN route only covers the TV control network. Use a full-tunnel or internet routes; " +
                    "Flint keeps the local network outside the tunnel"
            }
        }
        return null
    }

    /**
     * Returns a config whose peer AllowedIPs have local interface networks carved out.
     * Call only after [validate] succeeds.
     */
    fun withLocalRoutesCarvedOut(
        config: Config,
        localNetworks: List<InetNetwork> = discoverLocalNetworks(),
    ): Config {
        if (localNetworks.isEmpty()) return config
        val builder = Config.Builder().setInterface(config.`interface`)
        for (peer in config.peers) {
            builder.addPeer(rebuildPeer(peer, carveAllowedIps(peer.allowedIps, localNetworks)))
        }
        return builder.build()
    }

    /** No DNS lookups, hard-coded interface names, address ranges or subnet assumptions. */
    fun localAddresses(): List<InetAddress> = localInterfaceAddresses()
        .map { it.address }
        .distinct()

    fun discoverLocalNetworks(): List<InetNetwork> = localInterfaceAddresses()
        .mapNotNull { entry ->
            runCatching {
                InetNetwork.parse("${formatAddress(entry.address)}/${entry.prefixLength}")
            }.getOrNull()
        }
        .distinct()

    internal fun carveAllowedIps(
        allowed: Collection<InetNetwork>,
        excluded: Collection<InetNetwork>,
    ): List<InetNetwork> {
        if (excluded.isEmpty()) return allowed.toList()
        var remaining = allowed.toList()
        for (block in excluded) {
            remaining = remaining.flatMap { route -> excludeOne(route, block) }
        }
        return remaining
    }

    /**
     * Removes [block] from [route]. Returns zero or more non-overlapping prefixes that cover
     * [route] minus [block].
     */
    internal fun excludeOne(route: InetNetwork, block: InetNetwork): List<InetNetwork> {
        if (route.address.address.size != block.address.address.size) return listOf(route)
        if (!overlaps(route, block)) return listOf(route)
        if (containsNetwork(block, route)) return emptyList()
        if (containsNetwork(route, block)) return subtractPrefix(route, block)
        // Partial overlap: split the route and recurse.
        if (route.mask >= route.address.address.size * 8) return emptyList()
        val (left, right) = splitInHalf(route)
        return excludeOne(left, block) + excludeOne(right, block)
    }

    internal fun contains(network: InetAddress, mask: Int, address: InetAddress): Boolean {
        val left = network.address
        val right = address.address
        if (left.size != right.size || mask !in 0..left.size * 8) return false
        val bytes = mask / 8
        for (index in 0 until bytes) if (left[index] != right[index]) return false
        val bits = mask % 8
        if (bits == 0) return true
        val partialMask = (0xff shl (8 - bits)) and 0xff
        return (left[bytes].toInt() and partialMask) == (right[bytes].toInt() and partialMask)
    }

    private fun localInterfaceAddresses(): List<LocalAddress> =
        NetworkInterface.getNetworkInterfaces()
            ?.toList().orEmpty()
            .filter { it.isUp && !it.isLoopback && !it.isPointToPoint }
            .flatMap { nif ->
                nif.interfaceAddresses.mapNotNull { ia ->
                    val address = ia.address ?: return@mapNotNull null
                    if (address.isAnyLocalAddress || address.isLoopbackAddress || address.isMulticastAddress) {
                        return@mapNotNull null
                    }
                    val prefix = ia.networkPrefixLength.toInt()
                    if (prefix !in 0..(address.address.size * 8)) return@mapNotNull null
                    LocalAddress(address, prefix)
                }
            }

    private fun rebuildPeer(peer: Peer, allowedIps: Collection<InetNetwork>): Peer {
        val builder = Peer.Builder()
            .setPublicKey(peer.publicKey)
            .addAllowedIps(allowedIps)
        peer.endpoint.ifPresent { builder.setEndpoint(it) }
        peer.preSharedKey.ifPresent { builder.setPreSharedKey(it) }
        peer.persistentKeepalive.ifPresent { builder.setPersistentKeepalive(it) }
        return builder.build()
    }

    private fun overlaps(a: InetNetwork, b: InetNetwork): Boolean {
        val shorter = minOf(a.mask, b.mask)
        return contains(a.address, shorter, b.address)
    }

    private fun containsNetwork(outer: InetNetwork, inner: InetNetwork): Boolean =
        outer.mask <= inner.mask && contains(outer.address, outer.mask, inner.address)

    /** Split [supernet] into the minimal set of prefixes that cover it except [hole]. */
    private fun subtractPrefix(supernet: InetNetwork, hole: InetNetwork): List<InetNetwork> {
        require(containsNetwork(supernet, hole))
        val results = ArrayList<InetNetwork>()
        var mask = hole.mask
        var addressBytes = hole.address.address.copyOf()
        while (mask > supernet.mask) {
            val bitIndex = mask - 1
            val siblingBytes = addressBytes.copyOf()
            flipBitInPlace(siblingBytes, bitIndex)
            clearHostBits(siblingBytes, mask)
            results += InetNetwork.parse("${formatAddress(InetAddress.getByAddress(siblingBytes))}/$mask")
            mask -= 1
            clearHostBits(addressBytes, mask)
        }
        return results
    }

    private fun splitInHalf(route: InetNetwork): Pair<InetNetwork, InetNetwork> {
        val nextMask = route.mask + 1
        val leftBytes = route.address.address.copyOf()
        clearHostBits(leftBytes, nextMask)
        val rightBytes = leftBytes.copyOf()
        flipBitInPlace(rightBytes, route.mask)
        clearHostBits(rightBytes, nextMask)
        val left = InetNetwork.parse("${formatAddress(InetAddress.getByAddress(leftBytes))}/$nextMask")
        val right = InetNetwork.parse("${formatAddress(InetAddress.getByAddress(rightBytes))}/$nextMask")
        return left to right
    }

    private fun flipBitInPlace(bytes: ByteArray, bitIndex: Int) {
        val byteIndex = bitIndex / 8
        val bitInByte = 7 - (bitIndex % 8)
        bytes[byteIndex] = (bytes[byteIndex].toInt() xor (1 shl bitInByte)).toByte()
    }

    private fun clearHostBits(bytes: ByteArray, prefixLen: Int) {
        val totalBits = bytes.size * 8
        for (bit in prefixLen until totalBits) {
            val byteIndex = bit / 8
            val bitInByte = 7 - (bit % 8)
            bytes[byteIndex] = (bytes[byteIndex].toInt() and (1 shl bitInByte).inv()).toByte()
        }
    }

    private fun formatAddress(address: InetAddress): String {
        val host = address.hostAddress ?: return address.toString()
        val zone = host.indexOf('%')
        return if (zone >= 0) host.substring(0, zone) else host
    }

    private data class LocalAddress(val address: InetAddress, val prefixLength: Int)
}
