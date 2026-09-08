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

data class NetworkInterfaceSnapshot(
    val name: String,
    val index: Int,
    val isUp: Boolean,
    val isLoopback: Boolean,
    val addresses: List<InterfaceAddressSnapshot>,
)

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

/** Selects tethering candidates without assuming any particular hotspot subnet. */
class HotspotInterfaceSelector {
    fun select(interfaces: Iterable<NetworkInterfaceSnapshot>): SelectedHotspotInterface? {
        return interfaces.withIndex()
            .asSequence()
            .filter { it.value.isUp && !it.value.isLoopback }
            .mapNotNull { indexed ->
                val priority = candidatePriority(indexed.value.name) ?: return@mapNotNull null
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

    private data class Candidate(
        val enumerationOrder: Int,
        val priority: Int,
        val snapshot: NetworkInterfaceSnapshot,
        val address: InterfaceAddressSnapshot,
    )
}

