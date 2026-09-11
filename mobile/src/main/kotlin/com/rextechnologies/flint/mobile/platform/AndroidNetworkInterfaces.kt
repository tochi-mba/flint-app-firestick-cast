package com.rextechnologies.flint.mobile.platform

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.LocalNetworkAssessor
import com.rextechnologies.flint.protocol.network.JvmNetworkInterfaceSource
import com.rextechnologies.flint.protocol.network.NetworkInterfaceSource
import java.io.Closeable
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch

/**
 * Watches which end of the local network this phone is on.
 *
 * There is no public API for "am I tethering" — `TetheringManager` is a system API — so the evidence
 * is the interface list, which an ordinary app can read. What [ConnectivityManager] is used for here
 * is only the nudge: it says something about connectivity changed, and the interfaces are re-read.
 * Its own idea of the current network is deliberately ignored, because while the phone is tethering
 * from mobile data the network it reports is the cellular one, which has nothing to do with the
 * television.
 *
 * ## Where the sampling happens
 *
 * Reading the interface list is a JNI walk of every interface and every address on the device. It is
 * not expensive once; it is expensive many times a second, which is what a Wi-Fi scan produces —
 * `onCapabilitiesChanged` fires repeatedly as signal strength moves. Those callbacks arrive on a
 * binder thread, and an earlier version did the walk inline on it and again on the main thread from
 * the constructor. Every sample now happens on [Dispatchers.IO], no faster than [DEBOUNCE_MILLIS],
 * and a burst of nudges collapses into one walk rather than dozens.
 */
class AndroidNetworkWatcher(
    context: Context,
    private val interfaces: NetworkInterfaceSource = JvmNetworkInterfaceSource(),
    private val assessor: LocalNetworkAssessor = LocalNetworkAssessor(),
    private val scope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.IO),
) : Closeable {
    private val applicationContext = context.applicationContext
    private val connectivity =
        applicationContext.getSystemService(ConnectivityManager::class.java)

    /**
     * Starts at "no local network", which is what is true before anything has looked.
     *
     * Sampling here instead would put the interface walk on whichever thread constructed this, and
     * that thread is the main one.
     */
    private val state = MutableStateFlow<LocalNetwork>(LocalNetwork.NoLocalNetwork)

    /** The current verdict. Re-sampled on every connectivity change, not cached from start-up. */
    val localNetwork: StateFlow<LocalNetwork> = state

    /**
     * One slot, keeping the newest request.
     *
     * A nudge that arrives while a walk is running is not lost — it is remembered and satisfied by
     * the next one — and ten nudges that arrive together become one walk, which is the entire point.
     */
    private val nudges = MutableSharedFlow<Unit>(
        replay = 1,
        onBufferOverflow = BufferOverflow.DROP_OLDEST,
    )

    private val callback = object : ConnectivityManager.NetworkCallback() {
        override fun onAvailable(network: Network) = resample()
        override fun onLost(network: Network) = resample()
        override fun onCapabilitiesChanged(
            network: Network,
            networkCapabilities: NetworkCapabilities,
        ) = resample()
    }

    private var registered = false

    init {
        scope.launch {
            nudges.collect {
                state.value = assessor.assess(interfaces.snapshots())
                // Rate limit rather than a leading-edge debounce: the first sample of a burst is the
                // one that matters and it has already happened, and the replay slot above holds any
                // nudge that arrives during the wait so the last state of the burst is seen too.
                delay(DEBOUNCE_MILLIS)
            }
        }
    }

    fun start() {
        if (registered) return
        val request = NetworkRequest.Builder()
            .addCapability(NetworkCapabilities.NET_CAPABILITY_NOT_RESTRICTED)
            .build()
        // A phone with the hotspot on and nothing else may have no network the framework will hand
        // out at all, so a failure to register is not a reason to stop: the sample below still works.
        runCatching { connectivity?.registerNetworkCallback(request, callback) }
            .onSuccess { registered = true }
        resample()
    }

    fun stop() {
        if (!registered) return
        runCatching { connectivity?.unregisterNetworkCallback(callback) }
        registered = false
    }

    /**
     * Asks for a fresh sample. Returns immediately; the answer arrives on [localNetwork].
     *
     * Cheap enough to call on any screen that shows the verdict, and cheap in the sense that
     * matters: it does no work on the calling thread at all.
     */
    fun resample() {
        nudges.tryEmit(Unit)
    }

    /** Stops watching for good. [stop] can be undone by [start]; this cannot. */
    override fun close() {
        stop()
        scope.cancel()
    }

    private companion object {
        /**
         * Fast enough that turning a hotspot on feels immediate, slow enough that a Wi-Fi scan does
         * not turn into a hundred interface walks.
         */
        const val DEBOUNCE_MILLIS = 400L
    }
}
