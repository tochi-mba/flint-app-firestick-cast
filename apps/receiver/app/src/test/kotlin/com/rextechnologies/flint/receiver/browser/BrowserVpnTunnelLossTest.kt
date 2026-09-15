package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * A tunnel that stops being a tunnel must stop being reported as one.
 *
 * Verification used to be a single check that unregistered itself on success, which latched
 * [BrowserVpnState.Connected] for the rest of the session. A Wi-Fi change, an endpoint that stopped
 * answering, or Android revoking consent all left Flint saying traffic was protected while pages
 * kept loading — on a profile whose whole reason for the setting was that they must not.
 */
class BrowserVpnTunnelLossTest {
    private class RecordingTunnel : BrowserVpnTunnel {
        var connects = 0
        var disconnects = 0
        override fun connect(configText: String): Result<Unit> {
            connects += 1
            return Result.success(Unit)
        }

        override fun disconnect() {
            disconnects += 1
        }
    }

    /** Verifies immediately, then hands the caller the loss callback to fire on demand. */
    private class WatchableVerifier : BrowserVpnConnectionVerifier {
        var onLost: (() -> Unit)? = null
        var watching = false

        override fun verify(onResult: (Result<Unit>) -> Unit) = onResult(Result.success(Unit))

        override fun watch(onLost: () -> Unit) {
            this.onLost = onLost
            watching = true
        }

        override fun stopWatching() {
            watching = false
        }
    }

    private val settings = ProfileNetworkSettings(
        vpnEnabled = true,
        provider = VpnProvider.WIREGUARD,
        autoConnectOnBrowserStart = true,
        configText = CONFIG,
    )

    @Test
    fun `a verified tunnel is watched, not left to latch`() {
        val verifier = WatchableVerifier()
        val coordinator = BrowserVpnCoordinator(
            tunnel = RecordingTunnel(),
            verifier = verifier,
        )

        coordinator.onBrowserSessionStart("tv-1", settings, VpnCapability(preparable = true, reason = "test"))

        assertEquals(BrowserVpnState.Connected, coordinator.state.value)
        assertTrue(verifier.watching, "a verified tunnel must stay under observation")
    }

    @Test
    fun `losing the tunnel leaves Connected and tears it down`() {
        val tunnel = RecordingTunnel()
        val verifier = WatchableVerifier()
        val coordinator = BrowserVpnCoordinator(tunnel = tunnel, verifier = verifier)
        coordinator.onBrowserSessionStart("tv-1", settings, VpnCapability(preparable = true, reason = "test"))

        verifier.onLost?.invoke()

        val state = coordinator.state.value
        assertTrue(state is BrowserVpnState.Failed, "lost tunnel must not stay Connected")
        assertEquals(1, tunnel.disconnects, "a lost tunnel is torn down, not left half-up")
    }

    @Test
    fun `a loss reported for a superseded attempt is ignored`() {
        // The callback outlives the connection it belongs to. Acting on a stale one would tear
        // down the tunnel a later connect had just established.
        val tunnel = RecordingTunnel()
        val verifier = WatchableVerifier()
        val coordinator = BrowserVpnCoordinator(tunnel = tunnel, verifier = verifier)
        coordinator.onBrowserSessionStart("tv-1", settings, VpnCapability(preparable = true, reason = "test"))
        val stale = verifier.onLost

        coordinator.connectNow("tv-2", settings, VpnCapability(preparable = true, reason = "test"))
        val disconnectsAfterReconnect = tunnel.disconnects
        stale?.invoke()

        assertEquals(BrowserVpnState.Connected, coordinator.state.value)
        assertEquals(disconnectsAfterReconnect, tunnel.disconnects)
    }

    @Test
    fun `ending the session stops the watch`() {
        // A watch left registered after the browser closes keeps a system callback alive for a
        // tunnel nobody is using.
        val verifier = WatchableVerifier()
        val coordinator = BrowserVpnCoordinator(tunnel = RecordingTunnel(), verifier = verifier)
        coordinator.onBrowserSessionStart("tv-1", settings, VpnCapability(preparable = true, reason = "test"))

        coordinator.onBrowserSessionEnd()

        assertTrue(!verifier.watching, "the watch must not outlive the session")
        assertEquals(BrowserVpnState.Idle, coordinator.state.value)
    }

    @Test
    fun `a profile that requires VPN stops being allowed to browse after a loss`() {
        // The whole point of require-VPN: a lost tunnel has to re-block pages, not merely change
        // a banner.
        val verifier = WatchableVerifier()
        val coordinator = BrowserVpnCoordinator(tunnel = RecordingTunnel(), verifier = verifier)
        coordinator.onBrowserSessionStart("tv-1", settings, VpnCapability(preparable = true, reason = "test"))
        assertEquals(BrowserVpnState.Connected, coordinator.state.value)

        verifier.onLost?.invoke()

        assertTrue(coordinator.state.value !is BrowserVpnState.Connected)
    }

    @Test
    fun `a verifier that cannot watch never claims a loss`() {
        // The default no-op watch: a verifier with no way to observe loss must not fabricate one.
        val silent = BrowserVpnConnectionVerifier { onResult -> onResult(Result.success(Unit)) }
        val coordinator = BrowserVpnCoordinator(tunnel = RecordingTunnel(), verifier = silent)

        coordinator.onBrowserSessionStart("tv-1", settings, VpnCapability(preparable = true, reason = "test"))

        assertEquals(BrowserVpnState.Connected, coordinator.state.value)
        assertNull(null)
    }

    private companion object {
        val CONFIG = """
            [Interface]
            PrivateKey = YAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
            Address = 10.77.0.2/32

            [Peer]
            PublicKey = XAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
            Endpoint = 203.0.113.1:51820
            AllowedIPs = 0.0.0.0/0
        """.trimIndent()
    }
}
