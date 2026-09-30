package com.rextechnologies.flint.mobile.platform

import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import com.rextechnologies.flint.protocol.network.JvmNetworkInterfaceSource
import com.rextechnologies.flint.protocol.network.NetworkInterfaceSnapshot
import com.rextechnologies.flint.protocol.network.NetworkInterfaceSource
import com.rextechnologies.flint.protocol.network.PlatformLink

/**
 * What the platform says about each interface: mobile data, a tunnel, or a network the phone joined.
 *
 * @property byInterfaceName the interfaces the platform lists, by name, with what each one is.
 * @property complete whether the platform answered at all. When it did not, nothing can be said
 *   about any interface, and saying "not listed" about all of them would turn every interface into
 *   a hotspot.
 */
data class PlatformLinkTable(
    val byInterfaceName: Map<String, PlatformLink>,
    val complete: Boolean,
) {
    /** The interface list with the platform's word added to each entry. */
    fun classify(snapshots: List<NetworkInterfaceSnapshot>): List<NetworkInterfaceSnapshot> =
        snapshots.map { snapshot ->
            snapshot.copy(
                platformLink = byInterfaceName[snapshot.name]
                    ?: if (complete) PlatformLink.NOT_LISTED else PlatformLink.UNKNOWN,
            )
        }

    companion object {
        /** The table for a platform that could not be asked. Every rule falls back to the interface list. */
        val UNAVAILABLE = PlatformLinkTable(emptyMap(), complete = false)
    }
}

/**
 * The interface list, annotated with what the connectivity service knows about each entry.
 *
 * The interface list on its own cannot tell Wi-Fi from mobile data, and it does not need to
 * guess: [ConnectivityManager] lists every network the phone has joined and which transport each
 * one uses. What it does not list is just as telling. A tether interface belongs to a network the
 * phone is running rather than one it joined, so the framework never lists it, whatever the vendor
 * called it.
 */
class PlatformNetworkInterfaceSource(
    private val connectivity: ConnectivityManager?,
    private val interfaces: NetworkInterfaceSource = JvmNetworkInterfaceSource(),
) : NetworkInterfaceSource {
    override fun snapshots(): List<NetworkInterfaceSnapshot> = table().classify(interfaces.snapshots())

    /**
     * Asks the platform once, for every network at once, so one walk of the interface list is
     * judged against one view of the networks rather than against answers gathered over time.
     */
    private fun table(): PlatformLinkTable {
        val manager = connectivity ?: return PlatformLinkTable.UNAVAILABLE
        return runCatching {
            val listed = mutableMapOf<String, PlatformLink>()
            @Suppress("DEPRECATION")
            for (network in manager.allNetworks) {
                val capabilities = manager.getNetworkCapabilities(network) ?: continue
                val properties = manager.getLinkProperties(network) ?: continue
                val name = properties.interfaceName ?: continue
                listed[name] = linkOf(capabilities)
            }
            PlatformLinkTable(listed, complete = true)
        }.getOrDefault(PlatformLinkTable.UNAVAILABLE)
    }

    private fun linkOf(capabilities: NetworkCapabilities): PlatformLink = when {
        capabilities.hasTransport(NetworkCapabilities.TRANSPORT_VPN) -> PlatformLink.VPN
        capabilities.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR) -> PlatformLink.CELLULAR
        // Wi-Fi, Ethernet, Bluetooth and anything newer: a network this phone joined as a client.
        else -> PlatformLink.LOCAL_CLIENT
    }
}
