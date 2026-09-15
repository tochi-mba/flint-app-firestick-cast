package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.receiver.vpn.WireGuardConfigValidator
import com.wireguard.config.Config
import com.wireguard.config.InetNetwork
import java.io.ByteArrayInputStream
import java.net.InetAddress
import java.nio.charset.StandardCharsets
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * The config `tools/scripts/provision-wireguard-server.ps1` emits must be one Flint actually
 * admits.
 *
 * Every gate between a paste and a tunnel is exercised here against the exact shape that script
 * produces — full tunnel, preshared key, DNS line, keepalive — because each gate was written
 * against a hand-made fixture and none of them had seen a real server's output. A config the
 * generator emits and the receiver then rejects is indistinguishable, from a sofa, from a VPN
 * that does not work.
 */
class BrowserVpnProvisionedConfigTest {
    private val packageName = "com.rextechnologies.flint.receiver"
    private val televisionLan = listOf(InetNetwork.parse("192.168.20.0/24"))
    private val televisionAddress = listOf(InetAddress.getByName("192.168.20.15"))

    /** Byte-for-byte the generator's template, with its placeholders filled. */
    private val provisioned = """
        [Interface]
        PrivateKey = YAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
        Address = 10.77.0.2/32
        DNS = 9.9.9.9

        [Peer]
        PublicKey = XAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
        PresharedKey = WAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
        AllowedIPs = 0.0.0.0/0
        Endpoint = 141.147.109.67:51820
        PersistentKeepalive = 25
    """.trimIndent()

    @Test
    fun `the generated config passes the structural gate`() {
        assertTrue(WireGuardConfigValidator.isValid(provisioned))
    }

    @Test
    fun `the generated config is stored and auto-connect eligible`() {
        val settings = ProfileNetworkSettings(
            vpnEnabled = true,
            provider = VpnProvider.WIREGUARD,
            autoConnectOnBrowserStart = true,
            configText = provisioned,
        )

        assertTrue(settings.isConfiguredForAutoConnect)
        assertEquals(settings, settings.validated())
    }

    @Test
    fun `the generated config is admitted by route policy`() {
        val reason = BrowserVpnRoutePolicy.validate(
            config = parse(provisioned),
            localAddresses = televisionAddress,
            packageName = packageName,
            localNetworks = televisionLan,
        )

        assertNull(reason)
    }

    @Test
    fun `admission keeps the television's own LAN outside the tunnel`() {
        // Cast, pairing and the browser control session all run over Wi-Fi. If the full tunnel
        // swallowed the LAN, connecting the VPN would drop the very session that asked for it.
        val prepared = BrowserVpnRoutePolicy.withLocalRoutesCarvedOut(
            config = parse(provisioned),
            localNetworks = televisionLan,
        )
        val allowed = prepared.peers.single().allowedIps

        assertTrue(allowed.none { covers(it, InetAddress.getByName("192.168.20.15")) })
        assertTrue(allowed.any { covers(it, InetAddress.getByName("1.1.1.1")) })
        assertTrue(allowed.any { covers(it, InetAddress.getByName("9.9.9.9")) })
    }

    @Test
    fun `the tunnel address does not fall inside the default cloud subnet`() {
        // The generator defaults to 10.77.0.0/24 rather than 10.0.0.0/24 precisely because the
        // latter is the default OCI VCN subnet, and a tunnel address inside the server's own
        // subnet does not route. This pins that choice against a future edit of the template.
        val tunnelAddress = parse(provisioned).`interface`.addresses.single().address
        val defaultCloudSubnet = InetNetwork.parse("10.0.0.0/24")

        assertTrue(
            !BrowserVpnRoutePolicy.contains(
                defaultCloudSubnet.address,
                defaultCloudSubnet.mask,
                tunnelAddress,
            ),
        )
    }

    @Test
    fun `a peer whose endpoint is missing is refused before any tunnel work`() {
        // The generator always writes one. A hand-edited config that loses it would otherwise
        // reach the Go backend and fail there, where the reason is far harder to see.
        val withoutEndpoint = provisioned.lineSequence()
            .filterNot { it.startsWith("Endpoint = ") }
            .joinToString("\n")

        val reason = BrowserVpnRoutePolicy.validate(
            config = parse(withoutEndpoint),
            localAddresses = televisionAddress,
            packageName = packageName,
            localNetworks = televisionLan,
        )

        assertTrue(reason != null && reason.contains("endpoint"))
    }

    private fun covers(network: InetNetwork, address: InetAddress): Boolean =
        BrowserVpnRoutePolicy.contains(network.address, network.mask, address)

    private fun parse(text: String): Config = Config.parse(
        ByteArrayInputStream(text.toByteArray(StandardCharsets.UTF_8)),
    )
}
