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
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

/**
 * Watches which end of the local network this phone is on.
 *
 * There is no public API for "am I tethering" — `TetheringManager` is a system API — so the evidence
 * is the interface list, which an ordinary app can read. What [ConnectivityManager] is used for here
 * is only the nudge: it says something about connectivity changed, and the interfaces are re-read.
 * Its own idea of the current network is deliberately ignored, because while the phone is tethering
 * from mobile data the network it reports is the cellular one, which has nothing to do with the
 * television.
 */
class AndroidNetworkWatcher(
    context: Context,
    private val interfaces: NetworkInterfaceSource = JvmNetworkInterfaceSource(),
    private val assessor: LocalNetworkAssessor = LocalNetworkAssessor(),
) {
    private val applicationContext = context.applicationContext
    private val connectivity =
        applicationContext.getSystemService(ConnectivityManager::class.java)

    private val state = MutableStateFlow(assessor.assess(interfaces.snapshots()))

    /** The current verdict. Re-sampled on every connectivity change, not cached from start-up. */
    val localNetwork: StateFlow<LocalNetwork> = state

    private val callback = object : ConnectivityManager.NetworkCallback() {
        override fun onAvailable(network: Network) = resample()
        override fun onLost(network: Network) = resample()
        override fun onCapabilitiesChanged(
            network: Network,
            networkCapabilities: NetworkCapabilities,
        ) = resample()
    }

    private var registered = false

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

    /** Re-reads the interface list now. Cheap enough to call on any screen that shows the verdict. */
    fun resample() {
        state.value = assessor.assess(interfaces.snapshots())
    }
}
