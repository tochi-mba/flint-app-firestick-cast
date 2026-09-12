package com.rextechnologies.flint.mobile.state

import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.NetworkPath
import com.rextechnologies.flint.castcore.capability.ReceiverDevice
import com.rextechnologies.flint.castcore.discovery.DiscoveryRung
import com.rextechnologies.flint.mobile.net.DiscoveryRunner
import com.rextechnologies.flint.mobile.net.ReceiverFinder
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.withContext

/** How a request to look for a television is going. */
sealed interface LookupState {
    data object Idle : LookupState
    data object Running : LookupState

    /** Finished and found nothing, carrying what it tried so the copy can say so. */
    data class FoundNothing(val rungsAttempted: List<DiscoveryRung>) : LookupState

    data class Found(val count: Int) : LookupState
}

/** What discovery knows: the televisions, the chosen one, and the measured path to it. */
data class Discovered(
    val lookup: LookupState = LookupState.Idle,
    val manualLookup: LookupState = LookupState.Idle,
    val receivers: List<ReceiverDevice> = emptyList(),
    val selected: ReceiverDevice? = null,
    val rungsAttempted: List<DiscoveryRung> = emptyList(),
    val path: NetworkPath? = null,
) {
    val isProbing: Boolean
        get() = lookup is LookupState.Running || manualLookup is LookupState.Running
}

/**
 * Runs the ladder and keeps what it found.
 *
 * Re-entrancy is refused rather than queued: two sweeps of the same subnet at once is twice the
 * radio contention for the same answer, and the second one's results would land on top of the
 * first's in an order nobody chose.
 */
class DiscoveryCoordinator(private val runner: ReceiverFinder) {
    private val mutable = MutableStateFlow(Discovered())
    val state: StateFlow<Discovered> = mutable

    /** Walks the ladder. Returns the verdict so a caller can say something about it. */
    suspend fun probe(network: LocalNetwork): LookupState {
        if (mutable.value.lookup is LookupState.Running) return LookupState.Running
        mutable.update { it.copy(lookup = LookupState.Running) }

        val results = withContext(Dispatchers.IO) { runner.discover(network) }
        val found = results.flatMap { it.receivers }.distinctBy { it.address }
        val attempted = results.filter { it.attempted }.map { it.rung }

        // The previously selected television survives a sweep that did not find it. It may simply
        // have been asleep for those four seconds, and dropping the selection would also drop the
        // pairing the Screen tab is built on.
        val selected = found.firstOrNull { it.address == mutable.value.selected?.address }
            ?: found.firstOrNull()
            ?: mutable.value.selected

        val path = selected?.let { measure(network, it) }
        val lookup = if (found.isEmpty()) LookupState.FoundNothing(attempted) else LookupState.Found(found.size)

        mutable.update {
            it.copy(
                lookup = lookup,
                receivers = merge(it.receivers, found),
                selected = selected,
                rungsAttempted = attempted,
                path = path ?: it.path.takeIf { _ -> selected?.address == it.selected?.address },
            )
        }
        return lookup
    }

    /** Confirms one typed-in address. Discovery is never the only route to a television. */
    suspend fun probeManual(network: LocalNetwork, address: String): ReceiverDevice? {
        if (mutable.value.manualLookup is LookupState.Running) return null
        mutable.update { it.copy(manualLookup = LookupState.Running) }

        val trimmed = address.trim()
        val found = withContext(Dispatchers.IO) { runner.probeManual(network, trimmed) }
        if (found == null) {
            mutable.update {
                it.copy(manualLookup = LookupState.FoundNothing(listOf(DiscoveryRung.MANUAL)))
            }
            return null
        }

        val path = measure(network, found)
        mutable.update {
            it.copy(
                manualLookup = LookupState.Found(1),
                receivers = merge(it.receivers, listOf(found)),
                selected = found,
                path = path,
            )
        }
        return found
    }

    fun select(device: ReceiverDevice) {
        mutable.update {
            // The path belongs to the television it was measured against, so choosing another one
            // discards it rather than showing one device's round trip beside another's name.
            if (it.selected?.address == device.address) {
                it.copy(selected = device)
            } else {
                it.copy(selected = device, path = null)
            }
        }
    }

    /**
     * Replaces what is known about one television, by address, and selects it.
     *
     * This is how what ADB learned -- the platform, the model, the port that answered -- reaches
     * the list and the verdicts. A television that was not in the list yet, because nothing answered
     * on the receiver's port and only ADB did, is added by the same call.
     */
    fun update(device: ReceiverDevice) {
        mutable.update {
            val known = it.receivers.any { existing -> existing.address == device.address }
            it.copy(
                receivers = merge(it.receivers, listOf(device)),
                selected = device,
                // A path measured to this address is still the path to it.
                path = it.path.takeIf { _ -> known && it.selected?.address == device.address },
            )
        }
    }

    /** Forgets everything found, for the setting that hands a phone back to somebody else. */
    fun clear() {
        mutable.value = Discovered()
    }

    /**
     * The round trip, measured by this phone rather than taken from the television.
     *
     * The receiver reports zero in every STATS frame because it never measures one, and a zero on a
     * diagnostics row reads as an extraordinarily good link rather than as no measurement at all.
     * Throughput is left unmeasured here, and reported as unmeasured, because measuring it properly
     * needs a receiver willing to sink traffic and this probe is not that.
     */
    private suspend fun measure(network: LocalNetwork, device: ReceiverDevice): NetworkPath? {
        val samples = runner.measureRoundTripMillis(network, device)
        if (samples.isEmpty()) return null
        val sorted = samples.sorted()
        val median = sorted[sorted.size / 2]
        val jitter = sorted.last() - sorted.first()
        val attempts = DiscoveryRunner.ROUND_TRIP_ATTEMPTS
        val delivered = samples.size.coerceAtMost(attempts)
        val lost = attempts - delivered
        return NetworkPath(
            roundTripMs = median,
            jitterMs = jitter,
            throughputMbps = 0.0,
            packetLossPercent = (lost * 100.0 / attempts).coerceIn(0.0, 100.0),
            throughputMeasured = false,
        )
    }

    /**
     * Keeps a television that answered earlier but not this time.
     *
     * A list that emptied itself whenever one sweep missed would take the selection, the pairing and
     * the verdicts with it, and a Fire TV that was asleep for four seconds is the ordinary reason a
     * sweep misses.
     */
    private fun merge(
        existing: List<ReceiverDevice>,
        found: List<ReceiverDevice>,
    ): List<ReceiverDevice> {
        val byAddress = LinkedHashMap<String, ReceiverDevice>()
        existing.forEach { byAddress[it.address] = it }
        found.forEach { byAddress[it.address] = it }
        return byAddress.values.toList()
    }
}
