package com.rextechnologies.flint.mobile.platform

import android.content.Context
import android.net.ConnectivityManager
import android.net.LinkProperties
import android.net.Network
import android.net.NetworkCapabilities
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.LocalNetworkAssessor
import com.rextechnologies.flint.protocol.network.InterfaceAddressSnapshot
import com.rextechnologies.flint.protocol.network.Ipv4
import com.rextechnologies.flint.protocol.network.JvmNetworkInterfaceSource
import com.rextechnologies.flint.protocol.network.NetworkInterfaceSnapshot
import com.rextechnologies.flint.protocol.network.NetworkInterfaceSource
import com.rextechnologies.flint.protocol.network.PlatformLink
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Shadows.shadowOf
import org.robolectric.shadows.ShadowNetwork
import org.robolectric.shadows.ShadowNetworkCapabilities
import org.robolectric.shadows.ShadowNetworkInfo
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertTrue

@RunWith(AndroidJUnit4::class)
@Suppress("DEPRECATION")
class PlatformNetworkInterfaceSourceTest {
    private val context: Context = ApplicationProvider.getApplicationContext()
    private val connectivity = context.getSystemService(ConnectivityManager::class.java)

    /**
     * Robolectric starts with default networks of its own that it cannot fully describe. Left in,
     * they would make every walk incomplete; each test lists exactly the networks it is about.
     */
    @Before
    fun forgetTheDefaultNetworks() {
        shadowOf(connectivity).clearAllNetworks()
    }

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

        val labelled = labels(PlatformNetworkInterfaceSource(connectivity, walked))

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
    fun `a listed network with no properties yet leaves every unlisted interface unknown`() {
        val wifi = list(1, NetworkCapabilities.TRANSPORT_WIFI, "wlan0")
        val bare = ShadowNetwork.newInstance(7)
        shadowOf(connectivity).addNetwork(bare, connectedWifi())

        val labelled = labels(PlatformNetworkInterfaceSource(connectivity, walked) { arrayOf(wifi, bare) })

        // The walk still says what it could, but "not listed" no longer proves "the phone's own".
        assertEquals(PlatformLink.LOCAL_CLIENT, labelled["wlan0"])
        assertEquals(PlatformLink.UNKNOWN, labelled["ap_br_wlan1"])
    }

    @Test
    fun `mobile data being torn down is never taken for the phone's own hotspot`() {
        // Listed and known to be cellular, but already without link properties, so which interface
        // is its cannot be said. Calling the rest "not listed" made rmnet_data0 a hotspot, and the
        // carrier's two-host link then won the client comparison on width alone.
        val wifi = list(2, NetworkCapabilities.TRANSPORT_WIFI, "wlan0")
        val vpn = list(3, NetworkCapabilities.TRANSPORT_VPN, "tun0")
        val cellular = ShadowNetwork.newInstance(1)
        val shadow = shadowOf(connectivity)
        shadow.addNetwork(cellular, connectedWifi())
        val capabilities = ShadowNetworkCapabilities.newInstance()
        shadowOf(capabilities).addTransportType(NetworkCapabilities.TRANSPORT_CELLULAR)
        shadow.setNetworkCapabilities(cellular, capabilities)

        val source = PlatformNetworkInterfaceSource(connectivity, walked) { arrayOf(cellular, wifi, vpn) }
        val snapshots = source.snapshots()

        assertEquals(PlatformLink.UNKNOWN, snapshots.single { it.name == "rmnet_data0" }.platformLink)
        val verdict = assertIs<LocalNetwork.PhoneIsClient>(LocalNetworkAssessor().assess(snapshots))
        assertEquals("wlan0", verdict.interfaceName)
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

    @Test
    fun `a listed network with no interface name leaves every unlisted interface unknown`() {
        val wifi = list(1, NetworkCapabilities.TRANSPORT_WIFI, "wlan0")
        val nameless = ShadowNetwork.newInstance(8)
        val shadow = shadowOf(connectivity)
        shadow.addNetwork(nameless, connectedWifi())
        shadow.setNetworkCapabilities(nameless, ShadowNetworkCapabilities.newInstance())
        shadow.setLinkProperties(nameless, LinkProperties())

        val labelled = labels(PlatformNetworkInterfaceSource(connectivity, walked) { arrayOf(wifi, nameless) })

        assertEquals(PlatformLink.LOCAL_CLIENT, labelled["wlan0"])
        assertEquals(PlatformLink.UNKNOWN, labelled["ap_br_wlan1"])
    }

    @Test
    fun `a platform that fails mid-walk leaves every interface unknown rather than half-labelled`() {
        list(1, NetworkCapabilities.TRANSPORT_WIFI, "wlan0")
        val failing = PlatformNetworkInterfaceSource(connectivity, walked) {
            throw SecurityException("network vanished")
        }

        val labelled = failing.snapshots().map { it.platformLink }.toSet()

        assertEquals(setOf(PlatformLink.UNKNOWN), labelled)
    }

    @Test
    fun `by default the device's own interfaces are walked and each is labelled`() {
        // The real interface list of whatever runs the test, which always has a loopback at least.
        // One of its interfaces is listed as a tunnel, which only the platform can have said.
        val names = JvmNetworkInterfaceSource().snapshots().map { it.name }
        val tunnel = names.first()
        list(1, NetworkCapabilities.TRANSPORT_VPN, tunnel)

        val labelled = labels(PlatformNetworkInterfaceSource(connectivity))

        assertEquals(names.toSet(), labelled.keys)
        assertEquals(PlatformLink.VPN, labelled[tunnel])
        assertTrue(labelled.values.none { it == PlatformLink.UNKNOWN })
    }

    private fun list(id: Int, transport: Int, interfaceName: String): Network {
        val network: Network = ShadowNetwork.newInstance(id)
        val shadow = shadowOf(connectivity)
        shadow.addNetwork(network, connectedWifi())
        val capabilities = ShadowNetworkCapabilities.newInstance()
        shadowOf(capabilities).addTransportType(transport)
        shadow.setNetworkCapabilities(network, capabilities)
        val properties = LinkProperties().apply { this.interfaceName = interfaceName }
        shadow.setLinkProperties(network, properties)
        return network
    }

    private fun labels(source: NetworkInterfaceSource): Map<String, PlatformLink> =
        source.snapshots().associate { it.name to it.platformLink }

    private fun connectedWifi() = ShadowNetworkInfo.newInstance(null, ConnectivityManager.TYPE_WIFI, 0, true, null)

    private fun snapshot(name: String, index: Int, address: String, prefix: Int) = NetworkInterfaceSnapshot(
        name = name,
        index = index,
        isUp = true,
        isLoopback = false,
        addresses = listOf(InterfaceAddressSnapshot(Ipv4.parse(address), prefix)),
    )
}
