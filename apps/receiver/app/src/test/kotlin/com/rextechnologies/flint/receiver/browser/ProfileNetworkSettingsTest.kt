package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class ProfileNetworkSettingsTest {
    @Test
    fun `disabled settings always validate`() {
        val settings = ProfileNetworkSettings(
            vpnEnabled = false,
            provider = VpnProvider.NONE,
            autoConnectOnBrowserStart = true,
            configText = "not a config",
        )

        assertEquals(settings, settings.validated())
        assertFalse(settings.isConfiguredForAutoConnect)
    }

    @Test
    fun `enabled wireguard with interface and peer is valid and auto-connect ready`() {
        val settings = ProfileNetworkSettings(
            vpnEnabled = true,
            provider = VpnProvider.WIREGUARD,
            autoConnectOnBrowserStart = true,
            configText = VALID_CONFIG,
        )

        assertEquals(settings, assertNotNull(settings.validated()))
        assertTrue(settings.isConfiguredForAutoConnect)
    }

    @Test
    fun `enabled without provider or peer fails validation`() {
        assertNull(
            ProfileNetworkSettings(
                vpnEnabled = true,
                provider = VpnProvider.NONE,
                autoConnectOnBrowserStart = true,
                configText = VALID_CONFIG,
            ).validated(),
        )
        assertNull(
            ProfileNetworkSettings(
                vpnEnabled = true,
                provider = VpnProvider.WIREGUARD,
                autoConnectOnBrowserStart = true,
                configText = "[Interface]\nPrivateKey = x\n",
            ).validated(),
        )
        assertFalse(
            ProfileNetworkSettings(
                vpnEnabled = true,
                provider = VpnProvider.WIREGUARD,
                autoConnectOnBrowserStart = false,
                configText = VALID_CONFIG,
            ).isConfiguredForAutoConnect,
        )
    }

    @Test
    fun `oversized config is rejected`() {
        val huge = buildString {
            append("[Interface]\nPrivateKey = x\n\n[Peer]\nPublicKey = y\n")
            repeat(ProfileNetworkSettings.MAX_CONFIG_UTF8_BYTES) { append('a') }
        }
        val settings = ProfileNetworkSettings(
            vpnEnabled = true,
            provider = VpnProvider.WIREGUARD,
            autoConnectOnBrowserStart = true,
            configText = huge,
        )

        assertNull(settings.validated())
        assertFalse(settings.isConfiguredForAutoConnect)
    }

    companion object {
        private val VALID_CONFIG = """
            [Interface]
            PrivateKey = YAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = XAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
            Endpoint = 203.0.113.1:51820
            AllowedIPs = 0.0.0.0/0
        """.trimIndent()
    }
}
