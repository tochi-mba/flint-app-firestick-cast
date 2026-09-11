package com.rextechnologies.flint.castcore.discovery

import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.protocol.network.Ipv4Subnet
import java.net.Inet4Address

/** One way of looking for a receiver. Tried in order; the first that finds one wins. */
enum class DiscoveryRung {
    /**
     * A plaintext line probe swept across the derived subnet.
     *
     * First on purpose. Multicast is the thing that fails on a SoftAP link, and this is the
     * multicast-free fallback the receiver was built to answer — it treats a probe as a non-event, so
     * a sweep cannot make the television flicker between screens.
     */
    LINE_PROBE,

    /** Multicast DNS on `_rexcast._tcp.local`, which the receiver answers when multicast survives. */
    MULTICAST_DNS,

    /** A UDP broadcast to the subnet's broadcast address. */
    UDP_BROADCAST,

    /** SSDP `M-SEARCH`, which finds DLNA renderers rather than Flint receivers. */
    SSDP,

    /** Typed in by hand. Always available, so discovery is never the only route to a television. */
    MANUAL,
}

/**
 * How hard a sweep is allowed to try.
 *
 * Every probe competes with the cast stream for the same radio, so none of these numbers is "as many
 * as possible". They are derived from the subnet rather than written down, because a hotspot's subnet
 * is the access point's choice and not Flint's.
 */
data class SweepBudget(
    val hostCount: Int,
    val connectTimeoutMillis: Int,
    val concurrency: Int,
) {
    init {
        require(hostCount in 1..MAXIMUM_SWEEP_HOSTS)
        require(connectTimeoutMillis in 1..10_000)
        require(concurrency in 1..64)
    }

    companion object {
        /**
         * The largest subnet worth sweeping one host at a time.
         *
         * A /24 is 254 hosts and finishes in well under a second at this concurrency. A /16 is sixty
         * thousand, which is not a sweep, it is a denial of the radio — so a subnet that large gets no
         * line-probe rung at all and the copy says why rather than appearing to hang.
         */
        const val MAXIMUM_SWEEP_HOSTS: Int = 512

        /**
         * Short, because a host that is going to answer answers immediately on a local link, and every
         * millisecond spent waiting on one that will not is a millisecond the radio is not carrying
         * video.
         */
        const val CONNECT_TIMEOUT_MILLIS: Int = 400

        const val CONCURRENCY: Int = 16

        /**
         * `null` when the derived subnet is too large to sweep responsibly.
         *
         * The count comes from the subnet rather than being worked out again here. Working it out
         * again is what produced a budget for one more host than the sweep would ever visit.
         */
        fun forSubnet(subnet: Ipv4Subnet): SweepBudget? {
            val sweepable = subnet.usableHostCount()
            if (sweepable < 1 || sweepable > MAXIMUM_SWEEP_HOSTS) return null
            return SweepBudget(
                hostCount = sweepable.toInt(),
                connectTimeoutMillis = CONNECT_TIMEOUT_MILLIS,
                concurrency = CONCURRENCY,
            )
        }
    }
}

/**
 * One rung, ready to run.
 *
 * @property boundAddress the address every socket this rung opens must be bound to. There is no case
 *   where this is absent and the rung still runs, which is the point of carrying it here rather than
 *   letting each rung go and look for itself.
 * @property requiresMulticastLock whether Android will drop the rung's packets without a
 *   `WifiManager.MulticastLock` held for its duration.
 */
data class DiscoveryStep(
    val rung: DiscoveryRung,
    val boundAddress: Inet4Address,
    /**
     * The subnet the address sits in, carried rather than re-derived.
     *
     * A rung that had to work the prefix out for itself would have to guess at it, and a guessed
     * prefix is a hardcoded subnet wearing arithmetic as a disguise. It is derived once, from the
     * interface, and handed down.
     */
    val subnet: Ipv4Subnet,
    val sweep: SweepBudget? = null,
    val broadcastAddress: Inet4Address? = null,
    val requiresMulticastLock: Boolean = false,
)

/**
 * Decides which rungs to run, in which order, with what bounds.
 *
 * Pure. Running them is the Android adapter's job; deciding is testable without a radio.
 */
object DiscoveryLadder {
    fun plan(network: LocalNetwork): List<DiscoveryStep> {
        val address = network.boundAddress ?: return emptyList()
        val subnet = when (network) {
            is LocalNetwork.PhoneIsHost -> network.subnet
            is LocalNetwork.PhoneIsClient -> network.subnet
            LocalNetwork.NoLocalNetwork -> return emptyList()
        }

        return buildList {
            SweepBudget.forSubnet(subnet)?.let { budget ->
                add(DiscoveryStep(DiscoveryRung.LINE_PROBE, address, subnet, sweep = budget))
            }
            add(
                DiscoveryStep(
                    DiscoveryRung.MULTICAST_DNS,
                    address,
                    subnet,
                    requiresMulticastLock = true,
                ),
            )
            add(
                DiscoveryStep(
                    DiscoveryRung.UDP_BROADCAST,
                    address,
                    subnet,
                    broadcastAddress = subnet.broadcastAddress,
                ),
            )
            add(DiscoveryStep(DiscoveryRung.SSDP, address, subnet, requiresMulticastLock = true))
            add(DiscoveryStep(DiscoveryRung.MANUAL, address, subnet))
        }
    }
}
