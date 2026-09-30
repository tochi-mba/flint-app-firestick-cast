package com.rextechnologies.flint.mobile.platform

import android.content.Context
import android.net.ConnectivityManager
import android.net.LinkProperties
import android.net.Network
import android.net.NetworkCapabilities
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.protocol.network.InterfaceAddressSnapshot
import com.rextechnologies.flint.protocol.network.Ipv4
import com.rextechnologies.flint.protocol.network.NetworkInterfaceSnapshot
import com.rextechnologies.flint.protocol.network.NetworkInterfaceSource
import com.rextechnologies.flint.protocol.network.PlatformLink
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Shadows.shadowOf
import org.robolectric.shadows.ShadowNetwork
import org.robolectric.shadows.ShadowNetworkCapabilities
import org.robolectric.shadows.ShadowNetworkInfo
import kotlin.test.assertEquals

@RunWith(AndroidJUnit4::class)
@Suppress("DEPRECATION")
class PlatformNetworkInterfaceSourceTest {
    private val context: Context = ApplicationProvider.getApplicationContext()
    private val connectivity = context.getSystemService(ConnectivityManager::class.java)

    /** The list a phone on Wi-Fi with mobile data still up and a hotspot running would walk. */
    private val walked = NetworkInterfaceSource {
        listOf(
            snapshot("rmnet_data0", 21, "10.44.201.6", 30),
            snapshot("wlan0", 2, "10.182.169.140", 24),
            snapshot("ap_br_wlan1", 4, "10.182.169.1", 24),
            snapshot("tun0", 9, "10.8.0.2", 32),
        )
    }

    @Test
    fun `each interface is labelled with what the platform says it is`() {
        list(1, NetworkCapabilities.TRANSPORT_CELLULAR, "rmnet_data0")
        list(2, NetworkCapabilities.TRANSPORT_WIFI, "wlan0")
        list(3, NetworkCapabilities.TRANSPORT_VPN, "tun0")

        val labelled = PlatformNetworkInterfaceSource(connectivity, walked).snapshots().associate {
            it.name to
                it.platformLink
        }

        assertEquals(
            mapOf(
                "rmnet_data0" to PlatformLink.CELLULAR,
                "wlan0" to PlatformLink.LOCAL_CLIENT,
                "ap_br_wlan1" to PlatformLink.NOT_LISTED,
                "tun0" to PlatformLink.VPN,
            ),
            labelled,
        )
    }

    @Test
    fun `a network with no properties yet is skipped rather than failing the walk`() {
        list(1, NetworkCapabilities.TRANSPORT_WIFI, "wlan0")
        val bare = ShadowNetwork.newInstance(7)
        shadowOf(
            connectivity,
        ).addNetwork(bare, ShadowNetworkInfo.newInstance(null, ConnectivityManager.TYPE_WIFI, 0, true, null))

        val labelled = PlatformNetworkInterfaceSource(connectivity, walked).snapshots().associate {
            it.name to
                it.platformLink
        }

        assertEquals(PlatformLink.LOCAL_CLIENT, labelled["wlan0"])
        assertEquals(PlatformLink.NOT_LISTED, labelled["ap_br_wlan1"])
    }

    @Test
    fun `without a connectivity service nothing is claimed about any interface`() {
        val labelled = PlatformNetworkInterfaceSource(null, walked).snapshots().map { it.platformLink }.toSet()

        assertEquals(setOf(PlatformLink.UNKNOWN), labelled)
    }

    @Test
    fun `a table that could not be completed never invents a hotspot`() {
        val table = PlatformLinkTable(mapOf("wlan0" to PlatformLink.LOCAL_CLIENT), complete = false)

        val labelled = table.classify(walked.snapshots()).associate { it.name to it.platformLink }

        assertEquals(PlatformLink.LOCAL_CLIENT, labelled["wlan0"])
        assertEquals(PlatformLink.UNKNOWN, labelled["ap_br_wlan1"])
    }

    private fun list(id: Int, transport: Int, interfaceName: String) {
        val network: Network = ShadowNetwork.newInstance(id)
        val shadow = shadowOf(connectivity)
        shadow.addNetwork(network, ShadowNetworkInfo.newInstance(null, ConnectivityManager.TYPE_WIFI, 0, true, null))
        val capabilities = ShadowNetworkCapabilities.newInstance()
        shadowOf(capabilities).addTransportType(transport)
        shadow.setNetworkCapabilities(network, capabilities)
        val properties = LinkProperties().apply { this.interfaceName = interfaceName }
        shadow.setLinkProperties(network, properties)
    }

    private fun snapshot(name: String, index: Int, address: String, prefix: Int) = NetworkInterfaceSnapshot(
        name = name,
        index = index,
        isUp = true,
        isLoopback = false,
        addresses = listOf(InterfaceAddressSnapshot(Ipv4.parse(address), prefix)),
    )
}
