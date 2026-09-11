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
         * The worst case is every host silent: hosts divided by concurrency, times the connect
         * timeout. A /24 is 253 hosts, so about six seconds at this concurrency and timeout, and the
         * permitted /23 about thirteen -- a television that is there answers in the first round, and
         * the sweep stops at the first rung that finds one. A /16 is sixty thousand, which is not a
         * sweep, it is a denial of the radio, so a subnet that large gets no line-probe rung at all
         * and the copy says why rather than appearing to hang.
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
 * Sealed, with one shape per rung, because the rungs do not need the same things: a sweep needs a
 * budget and nothing else does, a broadcast needs a broadcast address and nothing else does, and two
 * of the five need a multicast lock held for their duration. As one data class with five nullable
 * fields, which combinations were valid lived in the heads of whoever wrote the runner -- so the
 * sweep read `step.sweep ?: return "too large to sweep"`, a sentence that is only true for one of
 * the five rungs, and the broadcast read `step.broadcastAddress ?: return notAttempted`, which could
 * silently skip the rung on a subnet that does have one.
 *
 * @property boundAddress the address every socket this rung opens must be bound to. There is no case
 *   where this is absent and the rung still runs, which is the point of carrying it here rather than
 *   letting each rung go and look for itself.
 * @property subnet the subnet the address sits in, carried rather than re-derived. A rung that had
 *   to work the prefix out for itself would have to guess at it, and a guessed prefix is a hardcoded
 *   subnet wearing arithmetic as a disguise. It is derived once, from the interface, and handed down.
 * @property requiresMulticastLock whether Android will drop the rung's packets without a
 *   `WifiManager.MulticastLock` held for its duration.
 */
sealed interface DiscoveryStep {
    val rung: DiscoveryRung
    val boundAddress: Inet4Address
    val subnet: Ipv4Subnet

    val requiresMulticastLock: Boolean
        get() = false

    /** The plaintext sweep, with the bounds the subnet earned. */
    data class LineProbe(
        override val boundAddress: Inet4Address,
        override val subnet: Ipv4Subnet,
        val sweep: SweepBudget,
    ) : DiscoveryStep {
        override val rung: DiscoveryRung get() = DiscoveryRung.LINE_PROBE
    }

    /** Multicast DNS on `_rexcast._tcp.local`. */
    data class MulticastDns(
        override val boundAddress: Inet4Address,
        override val subnet: Ipv4Subnet,
    ) : DiscoveryStep {
        override val rung: DiscoveryRung get() = DiscoveryRung.MULTICAST_DNS
        override val requiresMulticastLock: Boolean get() = true
    }

    /** One datagram to the subnet's broadcast address, which only exists for some prefixes. */
    data class UdpBroadcast(
        override val boundAddress: Inet4Address,
        override val subnet: Ipv4Subnet,
        val broadcastAddress: Inet4Address,
    ) : DiscoveryStep {
        override val rung: DiscoveryRung get() = DiscoveryRung.UDP_BROADCAST
    }

    /** SSDP `M-SEARCH`, which finds DLNA renderers rather than Flint receivers. */
    data class Ssdp(
        override val boundAddress: Inet4Address,
        override val subnet: Ipv4Subnet,
    ) : DiscoveryStep {
        override val rung: DiscoveryRung get() = DiscoveryRung.SSDP
        override val requiresMulticastLock: Boolean get() = true
    }

    /** Typed in by hand, so it is always in the plan and never runs as part of a sweep. */
    data class Manual(
        override val boundAddress: Inet4Address,
        override val subnet: Ipv4Subnet,
    ) : DiscoveryStep {
        override val rung: DiscoveryRung get() = DiscoveryRung.MANUAL
    }
}

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
            // A rung that cannot run is left out of the plan rather than added and skipped. A plan
            // that lists a rung is a plan to attempt it, which is what lets the diagnostics say what
            // was tried without the runner also having to decide.
            SweepBudget.forSubnet(subnet)?.let { budget ->
                add(DiscoveryStep.LineProbe(address, subnet, budget))
            }
            add(DiscoveryStep.MulticastDns(address, subnet))
            // A /31 and a /32 have no broadcast address at all, so there is nothing to broadcast to.
            if (subnet.prefixLength <= 30) {
                add(DiscoveryStep.UdpBroadcast(address, subnet, subnet.broadcastAddress))
            }
            add(DiscoveryStep.Ssdp(address, subnet))
            add(DiscoveryStep.Manual(address, subnet))
        }
    }
}
