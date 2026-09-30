package com.rextechnologies.flint.receiver.net

import android.content.Context
import android.content.ContextWrapper
import android.net.wifi.WifiManager
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assume.assumeFalse
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Shadows.shadowOf
import java.net.Inet4Address
import java.net.NetworkInterface
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

/** The Wi-Fi multicast lock the receiver's advertisement needs to hear any query at all. */
@RunWith(AndroidJUnit4::class)
class WifiMulticastTest {
    private val context: Context = ApplicationProvider.getApplicationContext()
    private val wifi = shadowOf(context.getSystemService(WifiManager::class.java))

    @Test
    fun `the lock is held until the handle is closed, and closing again is harmless`() {
        val handle = WifiMulticast.hold(context, "test")
        assertEquals(1, wifi.activeLockCount)

        handle.close()
        assertEquals(0, wifi.activeLockCount)

        handle.close()
        assertEquals(0, wifi.activeLockCount)
    }

    @Test
    fun `a device with no wifi service has nothing to hold`() {
        val wired = object : ContextWrapper(context) {
            override fun getApplicationContext(): Context = this

            override fun getSystemService(name: String): Any? = null
        }

        val handle = WifiMulticast.hold(wired, "test")
        assertEquals(0, wifi.activeLockCount)

        handle.close()
        assertEquals(0, wifi.activeLockCount)
    }

    @Test
    fun `a platform that refuses the lock still gets a handle, so the advertisement still goes out`() {
        val refusing = object : ContextWrapper(context) {
            override fun getApplicationContext(): Context = this

            override fun getSystemService(name: String): Any? = throw SecurityException("no multicast for you")
        }

        WifiMulticast.hold(refusing, "test").close()

        assertEquals(0, wifi.activeLockCount)
    }

    @Test
    fun `the receiver's own advertisement holds the lock from start to close`() {
        // The responder binds the multicast group, which Linux allows and Windows refuses.
        assumeFalse("Windows refuses to bind a socket to a multicast group address", isWindows)
        val address = assertNotNull(
            NetworkInterface.getNetworkInterfaces()
                .asSequence()
                .filter { runCatching { it.isUp && !it.isLoopback && it.supportsMulticast() }.getOrDefault(false) }
                .flatMap { it.inetAddresses.asSequence() }
                .filterIsInstance<Inet4Address>()
                .firstOrNull(),
            "this host has no multicast-capable IPv4 interface",
        )

        ReceiverMdnsResponder.onWifi(context, address, port = 47_855, browserPort = 0).use { responder ->
            assertTrue(responder.start().isSuccess)
            assertEquals(1, wifi.activeLockCount)
        }

        assertEquals(0, wifi.activeLockCount)
    }

    private companion object {
        val isWindows: Boolean = System.getProperty("os.name").orEmpty().startsWith("Windows")
    }
}
