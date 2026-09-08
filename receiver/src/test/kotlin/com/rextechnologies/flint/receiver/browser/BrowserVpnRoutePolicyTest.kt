package com.rextechnologies.flint.receiver.browser

import com.wireguard.config.Config
import com.wireguard.config.InetNetwork
import java.io.ByteArrayInputStream
import java.net.InetAddress
import java.nio.charset.StandardCharsets
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class BrowserVpnRoutePolicyTest {
    private val localLan = listOf(InetNetwork.parse("192.168.20.0/24"))
    private val localHost = listOf(InetAddress.getByName("192.168.20.15"))
    private val packageName = "com.rextechnologies.flint.receiver"

    @Test
    fun `default route is accepted when LAN carve-out leaves internet routes`() {
        val reason = BrowserVpnRoutePolicy.validate(
            config = configWithRoute("0.0.0.0/0"),
            localAddresses = localHost,
            packageName = packageName,
            localNetworks = localLan,
        )

        assertNull(reason)
    }

    @Test
    fun `route containing only the local control network is rejected`() {
        val reason = BrowserVpnRoutePolicy.validate(
            config = configWithRoute("192.168.20.0/24"),
            localAddresses = localHost,
            packageName = packageName,
            localNetworks = localLan,
        )

        assertNotNull(reason)
    }

    @Test
    fun `split route outside local control network is accepted`() {
        val reason = BrowserVpnRoutePolicy.validate(
            config = configWithRoute("203.0.113.0/24"),
            localAddresses = localHost,
            packageName = packageName,
            localNetworks = localLan,
        )

        assertNull(reason)
    }

    @Test
    fun `missing local address evidence fails closed`() {
        val reason = BrowserVpnRoutePolicy.validate(
            config = configWithRoute("203.0.113.0/24"),
            localAddresses = emptyList(),
            packageName = packageName,
            localNetworks = emptyList(),
        )

        assertNotNull(reason)
    }

    @Test
    fun `full tunnel carve-out excludes the local LAN prefix`() {
        val carved = BrowserVpnRoutePolicy.carveAllowedIps(
            allowed = listOf(InetNetwork.parse("0.0.0.0/0")),
            excluded = localLan,
        )

        assertTrue(carved.isNotEmpty())
        assertTrue(carved.none { covers(it, InetAddress.getByName("192.168.20.15")) })
        assertTrue(carved.any { covers(it, InetAddress.getByName("1.1.1.1")) })
        assertTrue(carved.any { covers(it, InetAddress.getByName("8.8.8.8")) })
    }

    @Test
    fun `withLocalRoutesCarvedOut rewrites peer AllowedIPs`() {
        val prepared = BrowserVpnRoutePolicy.withLocalRoutesCarvedOut(
            config = configWithRoute("0.0.0.0/0"),
            localNetworks = localLan,
        )
        val allowed = prepared.peers.single().allowedIps

        assertTrue(allowed.none { it.mask == 0 })
        assertTrue(allowed.none { covers(it, InetAddress.getByName("192.168.20.15")) })
        assertEquals(1, prepared.peers.size)
    }

    private fun covers(network: InetNetwork, address: InetAddress): Boolean =
        BrowserVpnRoutePolicy.contains(network.address, network.mask, address)

    private fun configWithRoute(route: String): Config = Config.parse(
        ByteArrayInputStream(
            """
            [Interface]
            PrivateKey = YAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = XAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
            Endpoint = 203.0.113.1:51820
            AllowedIPs = $route
            """.trimIndent().toByteArray(StandardCharsets.UTF_8),
        ),
    )
}
