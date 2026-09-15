package com.rextechnologies.flint.receiver.browser

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.os.Handler
import android.os.Looper

/**
 * Verifies that Android exposes a validated VPN network after the tunnel backend reports UP.
 *
 * Backend UP alone only proves that a TUN interface was requested. Required-VPN browsing remains
 * blocked until this boundary reports success.
 */
fun interface BrowserVpnConnectionVerifier {
    fun verify(onResult: (Result<Unit>) -> Unit)

    fun cancel() = Unit

    /**
     * Watches an already-verified tunnel and reports when it stops being one.
     *
     * A single check at connect time proves the tunnel existed once. It does not survive a Wi-Fi
     * change, an unreachable endpoint, or Android revoking VPN consent — and a latched "Connected"
     * over a dead tunnel is worse than no VPN at all, because required-VPN browsing keeps loading
     * pages on the strength of it.
     *
     * Default no-op so a verifier that cannot observe loss simply never claims one.
     */
    fun watch(onLost: () -> Unit) = Unit

    /** Stops the watch started by [watch]. */
    fun stopWatching() = Unit
}

/** Fails closed when a caller has not installed a platform verifier. */
object UnavailableBrowserVpnConnectionVerifier : BrowserVpnConnectionVerifier {
    override fun verify(onResult: (Result<Unit>) -> Unit) {
        onResult(Result.failure(IllegalStateException("VPN route verification is unavailable")))
    }
}

/**
 * Android verifier for the app's VPN network.
 *
 * It observes only system network capabilities and never probes or logs the WireGuard endpoint,
 * private key, local address, or page URL. A timeout is a failed verification, not permission to
 * browse over the normal network.
 */
class AndroidBrowserVpnConnectionVerifier(
    context: Context,
    private val handler: Handler = Handler(Looper.getMainLooper()),
    private val timeoutMillis: Long = DEFAULT_TIMEOUT_MILLIS,
) : BrowserVpnConnectionVerifier {
    private val connectivity =
        context.applicationContext.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager

    private var activeCallback: ConnectivityManager.NetworkCallback? = null
    private var activeTimeout: Runnable? = null
    private var activeResult: ((Result<Unit>) -> Unit)? = null
    private var watchCallback: ConnectivityManager.NetworkCallback? = null

    @Synchronized
    override fun verify(onResult: (Result<Unit>) -> Unit) {
        cancel()
        val callback = object : ConnectivityManager.NetworkCallback() {
            override fun onAvailable(network: Network) = inspect(this, network)

            override fun onCapabilitiesChanged(network: Network, capabilities: NetworkCapabilities) {
                if (isVerified(capabilities)) complete(this, Result.success(Unit))
            }

            override fun onLost(network: Network) {
                // Another matching VPN may still become available before the bounded timeout.
            }
        }
        val timeout = Runnable {
            complete(
                callback,
                Result.failure(IllegalStateException("VPN route could not be verified")),
            )
        }
        activeCallback = callback
        activeTimeout = timeout
        activeResult = onResult

        val request = vpnVerificationRequest()
        try {
            connectivity.registerNetworkCallback(request, callback)
            handler.postDelayed(timeout, timeoutMillis)
        } catch (error: RuntimeException) {
            complete(callback, Result.failure(IllegalStateException("VPN route verification failed")))
        }
    }

    @Synchronized
    override fun cancel() {
        val callback = activeCallback ?: return
        activeCallback = null
        activeTimeout?.let(handler::removeCallbacks)
        activeTimeout = null
        activeResult = null
        runCatching { connectivity.unregisterNetworkCallback(callback) }
    }

    /**
     * Registers a lasting callback for the app's VPN network.
     *
     * Reports loss on two signals, because Android uses both: the network going away entirely, and
     * the network staying up while losing VALIDATED — which is what a tunnel to an endpoint that
     * has stopped answering looks like.
     */
    @Synchronized
    override fun watch(onLost: () -> Unit) {
        stopWatching()
        var reported = false
        val callback = object : ConnectivityManager.NetworkCallback() {
            private fun report() {
                if (reported) return
                reported = true
                handler.post(onLost)
            }

            override fun onLost(network: Network) = report()

            override fun onCapabilitiesChanged(
                network: Network,
                capabilities: NetworkCapabilities,
            ) {
                if (!isVerified(capabilities)) report()
            }
        }
        watchCallback = callback
        try {
            connectivity.registerNetworkCallback(vpnVerificationRequest(), callback)
        } catch (error: RuntimeException) {
            watchCallback = null
            // Unable to observe the tunnel is itself a reason to stop trusting it.
            handler.post(onLost)
        }
    }

    @Synchronized
    override fun stopWatching() {
        val callback = watchCallback ?: return
        watchCallback = null
        runCatching { connectivity.unregisterNetworkCallback(callback) }
    }

    private fun inspect(callback: ConnectivityManager.NetworkCallback, network: Network) {
        val capabilities = connectivity.getNetworkCapabilities(network)
        if (isVerified(capabilities)) complete(callback, Result.success(Unit))
    }

    private fun isVerified(capabilities: NetworkCapabilities?): Boolean =
        capabilities?.hasTransport(NetworkCapabilities.TRANSPORT_VPN) == true &&
            capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) &&
            capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED)

    private fun complete(
        callback: ConnectivityManager.NetworkCallback,
        result: Result<Unit>,
    ) {
        val consumer = synchronized(this) {
            if (activeCallback !== callback) return
            activeCallback = null
            activeTimeout?.let(handler::removeCallbacks)
            activeTimeout = null
            activeResult.also { activeResult = null }
        }
        runCatching { connectivity.unregisterNetworkCallback(callback) }
        consumer?.invoke(result)
    }

    private companion object {
        const val DEFAULT_TIMEOUT_MILLIS = 10_000L
    }
}

/** NetworkRequest defaults exclude VPNs; transport selection alone does not remove that filter. */
internal fun vpnVerificationRequest(): NetworkRequest = NetworkRequest.Builder()
    .removeCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN)
    .addTransportType(NetworkCapabilities.TRANSPORT_VPN)
    .addCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
    .build()
