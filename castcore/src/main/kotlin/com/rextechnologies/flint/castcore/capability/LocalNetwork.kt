package com.rextechnologies.flint.castcore.capability

import com.rextechnologies.flint.protocol.network.HotspotInterfaceSelector
import com.rextechnologies.flint.protocol.network.InterfaceAddressSnapshot
import com.rextechnologies.flint.protocol.network.Ipv4Subnet
import com.rextechnologies.flint.protocol.network.NetworkInterfaceSnapshot
import com.rextechnologies.flint.protocol.network.SelectedHotspotInterface
import java.net.Inet4Address

/**
 * Which end of the local network this phone is on.
 *
 * Android has no public way to ask "am I tethering": `TetheringManager` is a system API. What a
 * normal app can see is its own interface list, and a phone running a hotspot has a tether interface
 * with an address on it. That evidence supports exactly three conclusions, and this type is all
 * three of them — there is deliberately no fourth case meaning "probably".
 */
sealed interface LocalNetwork {
    /** The address every socket must be bound to, or `null` when there is no local network. */
    val boundAddress: Inet4Address?

    /**
     * A tether interface is up and carries a site-local address, so this phone is the access point.
     *
     * This is the good case, and not only because it needs no setup. The access point can always
     * reach its own clients: client isolation, the setting that silently breaks casting on a shared
     * Wi-Fi network, is a rule the AP applies between clients and never to itself.
     */
    data class PhoneIsHost(val selected: SelectedHotspotInterface) : LocalNetwork {
        override val boundAddress: Inet4Address get() = selected.address
        val subnet: Ipv4Subnet get() = selected.subnet
    }

    /**
     * No tether interface, but the phone holds a site-local address on some other interface, so it
     * has joined a network somebody else runs.
     *
     * Casting can work here and often does. It is reported separately because the failure mode is
     * different and is not the phone's to fix: an access point with client isolation switched on
     * will carry the discovery probe nowhere, and no amount of retrying changes that.
     */
    data class PhoneIsClient(
        val interfaceName: String,
        val interfaceIndex: Int,
        override val boundAddress: Inet4Address,
        val prefixLength: Int,
    ) : LocalNetwork {
        val subnet: Ipv4Subnet get() = Ipv4Subnet(boundAddress, prefixLength)
    }

    /**
     * Neither. Mobile data may well be up — this says nothing about internet access, only that there
     * is no local network with a television on it.
     */
    data object NoLocalNetwork : LocalNetwork {
        override val boundAddress: Inet4Address? get() = null
    }
}

/**
 * Reads an interface list and says which end of the local network this phone is on.
 *
 * Pure: hand it a snapshot list and it returns a verdict. Sampling the real interfaces is the
 * Android adapter's job, and re-sampling on every connectivity change is the app's, because a
 * hotspot that comes up after the app started is the ordinary case rather than the exception.
 */
class LocalNetworkAssessor(
    private val hotspotSelector: HotspotInterfaceSelector = HotspotInterfaceSelector(),
) {
    fun assess(interfaces: List<NetworkInterfaceSnapshot>): LocalNetwork {
        hotspotSelector.select(interfaces)?.let { return LocalNetwork.PhoneIsHost(it) }
        return clientCandidate(interfaces) ?: LocalNetwork.NoLocalNetwork
    }

    /**
     * Picks the interface the phone is most likely casting over when it is not the access point.
     *
     * Chosen by shape rather than by name. Naming the Wi-Fi client interface would be a guess about
     * one vendor's kernel, and the addresses tell the truth anyway: a phone's Wi-Fi link is a small
     * site-local subnet, while a carrier's is either a public address or a /10 that is not
     * site-local at all, so neither reaches this branch. Where more than one candidate survives, the
     * narrowest subnet wins — that is the one with a television on it rather than a corporate /8 —
     * and enumeration order settles the rest so the answer is stable between samples.
     */
    private fun clientCandidate(interfaces: List<NetworkInterfaceSnapshot>): LocalNetwork.PhoneIsClient? {
        var bestSnapshot: NetworkInterfaceSnapshot? = null
        var bestBinding: InterfaceAddressSnapshot? = null
        for (snapshot in interfaces) {
            if (!snapshot.isUp || snapshot.isLoopback) continue
            val binding = snapshot.addresses.firstOrNull {
                it.address.isSiteLocalAddress && !it.address.isLoopbackAddress
            } ?: continue
            val incumbent = bestBinding
            // Strictly greater, so a tie keeps the earlier interface and repeated samples of an
            // unchanged machine keep returning the same answer.
            if (incumbent == null || binding.prefixLength > incumbent.prefixLength) {
                bestSnapshot = snapshot
                bestBinding = binding
            }
        }

        val snapshot = bestSnapshot ?: return null
        val binding = bestBinding ?: return null
        return LocalNetwork.PhoneIsClient(
            interfaceName = snapshot.name,
            interfaceIndex = snapshot.index,
            boundAddress = binding.address,
            prefixLength = binding.prefixLength,
        )
    }
}
