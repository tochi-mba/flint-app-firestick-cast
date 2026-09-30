package com.rextechnologies.flint.protocol.network

import java.net.Inet4Address
import java.net.NetworkInterface
import java.net.SocketException

data class InterfaceAddressSnapshot(
    val address: Inet4Address,
    val prefixLength: Int,
) {
    init {
        require(prefixLength in 0..32)
    }
}

/**
 * What the platform's connectivity service says an interface is, where there is one to ask.
 *
 * The interface list alone cannot tell a Wi-Fi link from a mobile-data one: both are up, both are
 * ordinary broadcast interfaces, and a carrier that hands out private addresses makes them look
 * the same down to the address range. The platform knows which is which, so an adapter that can
 * ask it records the answer here and the selection below stops having to guess.
 */
enum class PlatformLink {
    /** Nobody asked. Every rule falls back to what the interface list alone supports. */
    UNKNOWN,

    /**
     * The platform was asked and does not list this interface as a network the device has joined.
     *
     * For an interface holding a private address that leaves one explanation: the device is running
     * that network itself. A hotspot, USB tethering and Bluetooth tethering all look like this.
     */
    NOT_LISTED,

    /** A Wi-Fi or Ethernet network the device has joined as a client. */
    LOCAL_CLIENT,

    /** Mobile data. Never the path to a television, whatever its address looks like. */
    CELLULAR,

    /** A VPN tunnel. */
    VPN,
}

data class NetworkInterfaceSnapshot(
    val name: String,
    val index: Int,
    val isUp: Boolean,
    val isLoopback: Boolean,
    val addresses: List<InterfaceAddressSnapshot>,
    val isPointToPoint: Boolean = false,
    val platformLink: PlatformLink = PlatformLink.UNKNOWN,
)

/**
 * Whether this interface can carry cast traffic between two devices on one local network.
 *
 * Point-to-point is the shape a VPN tunnel takes, and a tunnel is never the path to a television
 * in the same room. Excluding it here matters more than it looks: a WireGuard tunnel's address is
 * site-local and usually a /32, so it wins both the hotspot priority list and the narrowest-subnet
 * comparison that picks a client interface. Without this filter, a phone with any VPN switched on
 * sweeps the tunnel instead of the Wi-Fi it is actually on, and finds nothing.
 *
 * Mobile data is excluded for the same reason and fails the same way. Many carriers hand out a
 * private address on a link of two or four hosts, which is narrower than any home network, so it
 * won that comparison too and the sweep searched the carrier's link for a television.
 */
fun NetworkInterfaceSnapshot.carriesLocalTraffic(): Boolean =
    isUp && !isLoopback && !isPointToPoint &&
        platformLink != PlatformLink.CELLULAR && platformLink != PlatformLink.VPN

fun interface NetworkInterfaceSource {
    fun snapshots(): List<NetworkInterfaceSnapshot>
}

/** JVM adapter kept separate from selection so tests never need real interfaces. */
class JvmNetworkInterfaceSource : NetworkInterfaceSource {
    override fun snapshots(): List<NetworkInterfaceSnapshot> {
        val result = mutableListOf<NetworkInterfaceSnapshot>()
        val interfaces = try {
            NetworkInterface.getNetworkInterfaces()
        } catch (_: SocketException) {
            null
        } ?: return emptyList()

        while (interfaces.hasMoreElements()) {
            val networkInterface = interfaces.nextElement()
            val addresses = networkInterface.interfaceAddresses.mapNotNull { binding ->
                val ipv4 = binding.address as? Inet4Address ?: return@mapNotNull null
                val prefixLength = binding.networkPrefixLength.toInt()
                if (prefixLength !in 0..32) return@mapNotNull null
                InterfaceAddressSnapshot(ipv4, prefixLength)
            }
            result += NetworkInterfaceSnapshot(
                name = networkInterface.name,
                index = networkInterface.index,
                isUp = safely(false) { networkInterface.isUp },
                isLoopback = safely(false) { networkInterface.isLoopback },
                addresses = addresses,
                isPointToPoint = safely(false) { networkInterface.isPointToPoint },
            )
        }
        return result
    }

    private inline fun <T> safely(fallback: T, block: () -> T): T = try {
        block()
    } catch (_: SocketException) {
        fallback
    }
}

data class SelectedHotspotInterface(
    val interfaceName: String,
    val interfaceIndex: Int,
    val address: Inet4Address,
    val prefixLength: Int,
) {
    val subnet: Ipv4Subnet
        get() = Ipv4Subnet(address, prefixLength)
}

/**
 * Selects tethering candidates without assuming any particular hotspot subnet.
 *
 * The name list is what a plain interface list supports, and it is incomplete by nature: every
 * vendor names its access-point interface differently and new names keep appearing. Where the
 * platform has been asked, its answer settles it both ways. An interface it lists as a joined
 * network is never a hotspot, whatever it is called, and one it does not list is one however
 * unfamiliar the name.
 */
class HotspotInterfaceSelector {
    fun select(interfaces: Iterable<NetworkInterfaceSnapshot>): SelectedHotspotInterface? {
        return interfaces.withIndex()
            .asSequence()
            .filter { it.value.carriesLocalTraffic() }
            .filter { it.value.platformLink != PlatformLink.LOCAL_CLIENT }
            .mapNotNull { indexed ->
                val priority = candidatePriority(indexed.value.name)
                    ?: UNNAMED_PRIORITY.takeIf { indexed.value.platformLink == PlatformLink.NOT_LISTED }
                    ?: return@mapNotNull null
                val address = indexed.value.addresses.firstOrNull {
                    it.address.isSiteLocalAddress && !it.address.isLoopbackAddress
                } ?: return@mapNotNull null
                Candidate(indexed.index, priority, indexed.value, address)
            }
            .sortedWith(compareBy<Candidate> { it.priority }.thenBy { it.enumerationOrder })
            .firstOrNull()
            ?.let {
                SelectedHotspotInterface(
                    interfaceName = it.snapshot.name,
                    interfaceIndex = it.snapshot.index,
                    address = it.address.address,
                    prefixLength = it.address.prefixLength,
                )
            }
    }

    private fun candidatePriority(name: String): Int? {
        val normalized = name.lowercase()
        return when {
            normalized.matches(Regex("ap[0-9]+")) -> 0
            normalized.matches(Regex("swlan[0-9]+")) -> 1
            normalized.matches(Regex("softap[0-9]+")) -> 2
            normalized.matches(Regex("wlan(?:[1-9][0-9]*)")) -> 3
            normalized.matches(Regex("rndis[0-9]+")) -> 4
            else -> null
        }
    }

    private companion object {
        /** After every recognised name, so a known access-point interface still wins a tie. */
        const val UNNAMED_PRIORITY = 5
    }

    private data class Candidate(
        val enumerationOrder: Int,
        val priority: Int,
        val snapshot: NetworkInterfaceSnapshot,
        val address: InterfaceAddressSnapshot,
    )
}
