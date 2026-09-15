package com.rextechnologies.flint.receiver.browser

import java.io.File
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertNull
import kotlin.test.assertTrue

class BrowserVpnCoordinatorTest {
    private val directory = File(
        System.getProperty("java.io.tmpdir"),
        "flint-browser-vpn-coord-${System.nanoTime()}",
    )
    private val networkFile = File(directory, "browser-network.json")
    private val verified = BrowserVpnConnectionVerifier { result ->
        result(Result.success(Unit))
    }

    @AfterTest
    fun cleanUp() {
        directory.deleteRecursively()
    }

    @Test
    fun `unconfigured settings leave idle and do not touch the tunnel`() {
        val tunnel = RecordingTunnel()
        val coordinator = BrowserVpnCoordinator(tunnel = tunnel, verifier = verified)

        coordinator.onBrowserSessionStart(
            profileId = "default",
            settings = ProfileNetworkSettings(),
            capability = VpnCapability(preparable = true, reason = "ok"),
        )

        assertEquals(BrowserVpnState.Idle, coordinator.state.value)
        assertEquals(0, tunnel.connectCalls)
    }

    @Test
    fun `unpreparable capability soft-fails to unavailable`() {
        val coordinator = BrowserVpnCoordinator(tunnel = RecordingTunnel(), verifier = verified)

        coordinator.onBrowserSessionStart(
            profileId = "default",
            settings = autoConnectSettings(),
            capability = VpnCapability(preparable = false, reason = "No Android context"),
        )

        assertEquals(BrowserVpnState.Unavailable, coordinator.state.value)
    }

    @Test
    fun `needs consent surfaces NeedsConsent without connecting`() {
        val tunnel = RecordingTunnel()
        val coordinator = BrowserVpnCoordinator(
            tunnel = tunnel,
            needsConsent = { true },
            verifier = verified,
        )

        coordinator.onBrowserSessionStart(
            profileId = "default",
            settings = autoConnectSettings(),
            capability = VpnCapability(preparable = true, reason = "ok"),
        )

        assertEquals(BrowserVpnState.NeedsConsent, coordinator.state.value)
        assertEquals(0, tunnel.connectCalls)
    }

    @Test
    fun `successful connect reaches Connected and session end disconnects`() {
        val tunnel = RecordingTunnel(connectResult = Result.success(Unit))
        val coordinator = BrowserVpnCoordinator(tunnel = tunnel, verifier = verified)

        coordinator.onBrowserSessionStart(
            profileId = "default",
            settings = autoConnectSettings(),
            capability = VpnCapability(preparable = true, reason = "ok"),
        )

        assertEquals(BrowserVpnState.Connected, coordinator.state.value)
        assertEquals(1, tunnel.connectCalls)

        coordinator.onBrowserSessionEnd()

        assertEquals(1, tunnel.disconnectCalls)
        assertEquals(BrowserVpnState.Idle, coordinator.state.value)
    }

    @Test
    fun `tunnel failure soft-fails without throwing`() {
        val coordinator = BrowserVpnCoordinator(tunnel = NoOpBrowserVpnTunnel(), verifier = verified)

        coordinator.onBrowserSessionStart(
            profileId = "default",
            settings = autoConnectSettings(),
            capability = VpnCapability(preparable = true, reason = "ok"),
        )

        val failed = assertIs<BrowserVpnState.Failed>(coordinator.state.value)
        assertEquals("VPN connect failed", failed.message)
    }

    @Test
    fun `clearSettings removes store entry and resets active session`() {
        val store = BrowserNetworkStore(
            file = networkFile,
            crypto = RoundTripTestCrypto,
            profileAuthority = BrowserNetworkProfileAuthority {
                it == BrowserLibraryStore.DEFAULT_PROFILE_ID
            },
        )
        assertTrue(store.put("default", autoConnectSettings()))
        val tunnel = RecordingTunnel(connectResult = Result.success(Unit))
        val coordinator = BrowserVpnCoordinator(tunnel = tunnel, networkStore = store, verifier = verified)

        coordinator.onBrowserSessionStart(
            profileId = "default",
            settings = autoConnectSettings(),
            capability = VpnCapability(preparable = true, reason = "ok"),
        )
        assertEquals(BrowserVpnState.Connected, coordinator.state.value)

        coordinator.clearSettings("default")

        assertNull(store.get("default"))
        assertEquals(1, tunnel.disconnectCalls)
        assertEquals(BrowserVpnState.Idle, coordinator.state.value)
    }

    @Test
    fun `fixed capability probe returns the configured result`() {
        val probe = FixedVpnCapabilityProbe(VpnCapability(false, "test"))
        assertEquals(VpnCapability(false, "test"), probe.probe())
    }

    private fun autoConnectSettings(): ProfileNetworkSettings = ProfileNetworkSettings(
        vpnEnabled = true,
        provider = VpnProvider.WIREGUARD,
        autoConnectOnBrowserStart = true,
        configText = VALID_CONFIG,
    )

    @Test
    fun `switching to a profile without VPN disconnects the previous tunnel`() {
        val tunnel = RecordingTunnel()
        val coordinator = BrowserVpnCoordinator(tunnel = tunnel, verifier = verified)
        val capability = VpnCapability(true, "ok")
        coordinator.onBrowserSessionStart("family", autoConnectSettings(), capability)
        coordinator.onBrowserSessionStart("guest", ProfileNetworkSettings(), capability)
        assertEquals(1, tunnel.disconnectCalls)
        assertEquals(BrowserVpnState.Idle, coordinator.state.value)
    }

    @Test
    fun `recomposing the same connected profile does not restart its tunnel`() {
        val tunnel = RecordingTunnel()
        val coordinator = BrowserVpnCoordinator(tunnel = tunnel, verifier = verified)
        repeat(3) { coordinator.onBrowserSessionStart("family", autoConnectSettings(), VpnCapability(true, "ok")) }
        assertEquals(1, tunnel.connectCalls)
        assertEquals(0, tunnel.disconnectCalls)
    }

    @Test
    fun `backend failures cannot publish config secrets in the UI`() {
        val tunnel = RecordingTunnel(Result.failure(IllegalStateException("PrivateKey = secret")))
        val coordinator = BrowserVpnCoordinator(tunnel = tunnel, verifier = verified)
        coordinator.connectNow("family", autoConnectSettings(), VpnCapability(true, "ok"))
        assertEquals("VPN connect failed", assertIs<BrowserVpnState.Failed>(coordinator.state.value).message)
        coordinator.onBrowserSessionEnd()
        assertEquals(1, tunnel.disconnectCalls)
    }

    @Test
    fun `connect now still surfaces NeedsConsent before touching the tunnel`() {
        val tunnel = RecordingTunnel()
        val coordinator = BrowserVpnCoordinator(
            tunnel = tunnel,
            needsConsent = { true },
            verifier = verified,
        )

        coordinator.connectNow(
            profileId = "default",
            settings = autoConnectSettings().copy(autoConnectOnBrowserStart = false),
            capability = VpnCapability(preparable = true, reason = "ok"),
        )

        assertEquals(BrowserVpnState.NeedsConsent, coordinator.state.value)
        assertEquals(0, tunnel.connectCalls)
    }

    @Test
    fun `consent denial soft-fails without connecting`() {
        val tunnel = RecordingTunnel()
        val coordinator = BrowserVpnCoordinator(tunnel = tunnel, verifier = verified)
        coordinator.onConsentDenied()
        assertIs<BrowserVpnState.Failed>(coordinator.state.value)
        assertEquals(0, tunnel.connectCalls)
    }

    @Test
    fun `tunnel up remains blocked until route verification succeeds`() {
        val verifier = RecordingVerifier()
        val coordinator = BrowserVpnCoordinator(
            tunnel = RecordingTunnel(),
            verifier = verifier,
        )

        coordinator.connectNow("family", autoConnectSettings(), VpnCapability(true, "ok"))

        assertEquals(BrowserVpnState.TunnelUpUnverified, coordinator.state.value)
        verifier.complete(Result.success(Unit))
        assertEquals(BrowserVpnState.Connected, coordinator.state.value)
    }

    @Test
    fun `failed route verification disconnects and remains fail closed`() {
        val tunnel = RecordingTunnel()
        val verifier = RecordingVerifier()
        val coordinator = BrowserVpnCoordinator(tunnel = tunnel, verifier = verifier)

        coordinator.connectNow("family", autoConnectSettings(), VpnCapability(true, "ok"))
        verifier.complete(Result.failure(IllegalStateException("not validated")))

        assertEquals(1, tunnel.disconnectCalls)
        assertEquals(
            "VPN route could not be verified",
            assertIs<BrowserVpnState.Failed>(coordinator.state.value).message,
        )
    }

    @Test
    fun `late verification from a stopped session cannot reconnect it`() {
        val verifier = RecordingVerifier()
        val coordinator = BrowserVpnCoordinator(
            tunnel = RecordingTunnel(),
            verifier = verifier,
        )
        coordinator.connectNow("family", autoConnectSettings(), VpnCapability(true, "ok"))

        coordinator.onBrowserSessionEnd()
        verifier.complete(Result.success(Unit))

        assertEquals(BrowserVpnState.Idle, coordinator.state.value)
    }

    private class RecordingTunnel(
        private val connectResult: Result<Unit> = Result.success(Unit),
    ) : BrowserVpnTunnel {
        var connectCalls = 0
        var disconnectCalls = 0

        override fun connect(configText: String): Result<Unit> {
            connectCalls += 1
            return connectResult
        }

        override fun disconnect() {
            disconnectCalls += 1
        }
    }

    private class RecordingVerifier : BrowserVpnConnectionVerifier {
        private var result: ((Result<Unit>) -> Unit)? = null

        override fun verify(onResult: (Result<Unit>) -> Unit) {
            result = onResult
        }

        override fun cancel() = Unit

        fun complete(value: Result<Unit>) {
            result?.invoke(value)
        }
    }

    private object RoundTripTestCrypto : BrowserNetworkCrypto {
        override fun encrypt(plaintext: ByteArray, associatedData: ByteArray): BrowserNetworkCiphertext =
            BrowserNetworkCiphertext(IV, plaintext + ByteArray(TAG_BYTES))

        override fun decrypt(ciphertext: BrowserNetworkCiphertext, associatedData: ByteArray): ByteArray {
            if (!ciphertext.initializationVector.contentEquals(IV) || ciphertext.encryptedBytes.size < TAG_BYTES) {
                throw BrowserNetworkCryptoException("Invalid test payload")
            }
            return ciphertext.encryptedBytes.copyOf(ciphertext.encryptedBytes.size - TAG_BYTES)
        }

        private const val TAG_BYTES = 16
        private val IV = ByteArray(12)
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
