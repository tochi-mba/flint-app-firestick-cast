package com.rextechnologies.flint.receiver.browser

import androidx.test.core.app.ApplicationProvider
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

@RunWith(RobolectricTestRunner::class)
class BrowserVpnTunnelImplTest {
    @Test
    fun `incomplete config fails before backend factory runs`() {
        val tunnel = BrowserVpnTunnelImpl(
            context = ApplicationProvider.getApplicationContext(),
            backendFactory = { error("backend must not load") },
            needsSystemConsent = { false },
        )

        val result = tunnel.connect("[Interface]\nPrivateKey = aaa\n")

        assertTrue(result.isFailure)
        assertEquals("WireGuard config is incomplete", result.exceptionOrNull()?.message)
    }

    @Test
    fun `invalid keys fail without starting the Go backend`() {
        val tunnel = BrowserVpnTunnelImpl(
            context = ApplicationProvider.getApplicationContext(),
            backendFactory = { error("backend must not load for invalid keys") },
            needsSystemConsent = { false },
        )

        val result = tunnel.connect(
            """
            [Interface]
            PrivateKey = not-a-valid-wireguard-key
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = also-not-a-valid-wireguard-key
            Endpoint = 203.0.113.1:51820
            AllowedIPs = 0.0.0.0/0
            """.trimIndent(),
        )

        assertTrue(result.isFailure)
        assertEquals("WireGuard config is invalid", result.exceptionOrNull()?.message)
    }

    @Test
    fun `missing consent soft-fails before parsing`() {
        val tunnel = BrowserVpnTunnelImpl(
            context = ApplicationProvider.getApplicationContext(),
            backendFactory = { error("backend must not load without consent") },
            needsSystemConsent = { true },
        )

        val result = tunnel.connect(
            """
            [Interface]
            PrivateKey = YAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = XAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
            Endpoint = 203.0.113.1:51820
            AllowedIPs = 0.0.0.0/0
            """.trimIndent(),
        )

        assertTrue(result.isFailure)
        assertEquals("VPN permission not granted", result.exceptionOrNull()?.message)
    }

    @Test
    fun `unsafe routes fail before the backend starts`() {
        val tunnel = BrowserVpnTunnelImpl(
            context = ApplicationProvider.getApplicationContext(),
            backendFactory = { error("backend must not load for rejected routes") },
            needsSystemConsent = { false },
            validateRoutes = { "VPN route includes the TV control network" },
        )

        val result = tunnel.connect(
            """
            [Interface]
            PrivateKey = YAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = XAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
            Endpoint = 203.0.113.1:51820
            AllowedIPs = 203.0.113.0/24
            """.trimIndent(),
        )

        assertTrue(result.isFailure)
        assertEquals("VPN route includes the TV control network", result.exceptionOrNull()?.message)
    }
}
