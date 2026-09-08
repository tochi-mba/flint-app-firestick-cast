package com.rextechnologies.flint.receiver.browser

import android.content.Context
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.net.NetworkRequest
import android.os.Looper
import androidx.test.core.app.ApplicationProvider
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.Shadows.shadowOf
import org.robolectric.annotation.Config
import org.robolectric.shadows.ShadowNetwork
import org.robolectric.shadows.ShadowNetworkCapabilities
import java.time.Duration
import kotlin.test.*

@RunWith(RobolectricTestRunner::class)
@Config(manifest = Config.NONE, sdk = [28])
class BrowserVpnConnectionVerifierTest {
    private val context = ApplicationProvider.getApplicationContext<Context>()
    private val connectivity = context.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager
    private val shadow = shadowOf(connectivity)
    private val network = ShadowNetwork.newInstance(101)

    @Test fun `VPN request removes the default exclusion while requiring internet and VPN transport`() {
        assertTrue(NetworkRequest.Builder().build().hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN))
        val request = vpnVerificationRequest()
        assertFalse(request.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_VPN))
        assertTrue(request.hasTransport(NetworkCapabilities.TRANSPORT_VPN))
        assertTrue(request.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET))
    }

    @Test fun `validated wifi does not count as a verified VPN`() {
        val results = mutableListOf<Result<Unit>>()
        val verifier = AndroidBrowserVpnConnectionVerifier(context, timeoutMillis = 100)
        verifier.verify(results::add)
        shadow.networkCallbacks.single().onCapabilitiesChanged(network, capabilities(vpn = false, validated = true))
        assertTrue(results.isEmpty())
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(100))
        assertTrue(results.single().isFailure)
        assertTrue(shadow.networkCallbacks.isEmpty())
    }

    @Test fun `VPN must validate before success and late callbacks cannot complete twice`() {
        val results = mutableListOf<Result<Unit>>()
        val verifier = AndroidBrowserVpnConnectionVerifier(context, timeoutMillis = 100)
        verifier.verify(results::add)
        val callback = shadow.networkCallbacks.single()
        callback.onCapabilitiesChanged(network, capabilities(vpn = true, validated = false))
        assertTrue(results.isEmpty())
        callback.onCapabilitiesChanged(network, capabilities(vpn = true, validated = true))
        callback.onCapabilitiesChanged(network, capabilities(vpn = true, validated = true))
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(200))
        assertEquals(1, results.size)
        assertTrue(results.single().isSuccess)
        assertTrue(shadow.networkCallbacks.isEmpty())
    }

    @Test fun `cancel removes listener and timeout and rejects its stale callback`() {
        val results = mutableListOf<Result<Unit>>()
        val verifier = AndroidBrowserVpnConnectionVerifier(context, timeoutMillis = 100)
        verifier.verify(results::add)
        val callback = shadow.networkCallbacks.single()
        verifier.cancel()
        callback.onCapabilitiesChanged(network, capabilities(vpn = true, validated = true))
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(200))
        assertTrue(results.isEmpty())
        assertTrue(shadow.networkCallbacks.isEmpty())
    }

    private fun capabilities(vpn: Boolean, validated: Boolean): NetworkCapabilities {
        val result = ShadowNetworkCapabilities.newInstance()
        shadowOf(result).addTransportType(if (vpn) NetworkCapabilities.TRANSPORT_VPN else NetworkCapabilities.TRANSPORT_WIFI)
        shadowOf(result).addCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
        if (validated) shadowOf(result).addCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED)
        return result
    }
}
