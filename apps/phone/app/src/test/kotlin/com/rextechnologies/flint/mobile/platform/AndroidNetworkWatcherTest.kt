package com.rextechnologies.flint.mobile.platform

import android.content.Context
import android.net.ConnectivityManager
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.protocol.network.InterfaceAddressSnapshot
import com.rextechnologies.flint.protocol.network.Ipv4
import com.rextechnologies.flint.protocol.network.NetworkInterfaceSnapshot
import com.rextechnologies.flint.protocol.network.NetworkInterfaceSource
import com.rextechnologies.flint.protocol.network.PlatformLink
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Shadows.shadowOf
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertTrue

/** The watcher turns an interface list into a verdict, from whichever source it was given. */
@RunWith(AndroidJUnit4::class)
class AndroidNetworkWatcherTest {
    private val context: Context = ApplicationProvider.getApplicationContext()

    @Test
    fun `a supplied source is what the verdict is drawn from`() = runBlocking<Unit> {
        val wifi = NetworkInterfaceSource {
            listOf(
                NetworkInterfaceSnapshot(
                    name = "wlan0",
                    index = 2,
                    isUp = true,
                    isLoopback = false,
                    addresses = listOf(InterfaceAddressSnapshot(Ipv4.parse("192.168.1.34"), 24)),
                    platformLink = PlatformLink.LOCAL_CLIENT,
                ),
            )
        }
        val watcher = AndroidNetworkWatcher(context, wifi)
        try {
            watcher.start()

            val verdict = withTimeout(5_000) { watcher.localNetwork.first { it !is LocalNetwork.NoLocalNetwork } }

            val client = assertIs<LocalNetwork.PhoneIsClient>(verdict)
            assertEquals("wlan0", client.interfaceName)
        } finally {
            watcher.close()
        }
    }

    @Test
    fun `without a source the platform-labelled interface list is used`() {
        // The bare interface list cannot tell mobile data from Wi-Fi, which is the bug the
        // platform labels fixed, so the default has to be the labelled source.
        val watcher = AndroidNetworkWatcher(context)
        try {
            assertIs<PlatformNetworkInterfaceSource>(watcher.interfaces)
        } finally {
            watcher.close()
        }
    }

    @Test
    fun `starting registers for connectivity changes once and stopping unregisters`() {
        val connectivity = shadowOf(context.getSystemService(ConnectivityManager::class.java))
        val watcher = AndroidNetworkWatcher(context)
        try {
            watcher.start()
            watcher.start()
            assertEquals(1, connectivity.networkCallbacks.size)

            watcher.resample()
            watcher.stop()
            watcher.stop()
            assertTrue(connectivity.networkCallbacks.isEmpty())
        } finally {
            watcher.close()
        }
    }
}
