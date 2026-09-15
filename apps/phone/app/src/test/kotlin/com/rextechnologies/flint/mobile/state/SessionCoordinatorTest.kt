package com.rextechnologies.flint.mobile.state

import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.PhoneCapabilities
import com.rextechnologies.flint.castcore.capability.ProbeOutcome
import com.rextechnologies.flint.castcore.capability.ReceiverDevice
import com.rextechnologies.flint.castcore.copy.PairingCopy
import com.rextechnologies.flint.castcore.session.SessionFailure
import com.rextechnologies.flint.castcore.session.SessionFailureKind
import com.rextechnologies.flint.mobile.platform.SessionTokens
import com.rextechnologies.flint.protocol.http.SessionToken
import com.rextechnologies.flint.protocol.network.SelectedHotspotInterface
import com.rextechnologies.flint.protocol.session.CastSessionParameters
import com.rextechnologies.flint.protocol.wire.ByeMessage
import com.rextechnologies.flint.protocol.wire.ByeReason
import com.rextechnologies.flint.protocol.wire.CodecId
import com.rextechnologies.flint.protocol.wire.HelloMessage
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import java.net.Inet4Address
import java.net.InetAddress
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** The coordinator's own bookkeeping, with a connection that never opens a socket. */
class SessionCoordinatorTest {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)
    private val network = LocalNetwork.PhoneIsHost(
        SelectedHotspotInterface("ap0", 7, InetAddress.getByName("192.168.43.1") as Inet4Address, 24),
    )
    private val television = ReceiverDevice("192.168.43.31", friendlyName = "Fire TV Stick")
    private val phone = PhoneCapabilities(
        apiLevel = 34,
        deviceName = "Pixel 8",
        screenWidth = 1080,
        screenHeight = 2400,
        densityDpi = 420,
        hardwareVideoEncoders = setOf(CodecId.H264),
        encoderProbe = ProbeOutcome.SUPPORTED,
    )

    /** Nothing here reaches a keystore: no test in this file stores or reads a token. */
    private val tokens = object : SessionTokens {
        var forgotten = false
            private set

        override fun tokenFor(receiverAddress: String): SessionToken? = null

        override fun remember(receiverAddress: String, token: SessionToken) = Unit

        override fun forgetEverything() {
            forgotten = true
        }
    }

    private val coordinator = SessionCoordinator(tokens, scope, connect = {})

    @AfterTest
    fun stop() {
        scope.cancel()
    }

    @Test
    fun `a malformed code never opens anything`() {
        assertEquals(PairingCopy.INVALID_CODE, coordinator.pair(network, television, phone, "12345"))
        assertEquals(PairingCopy.INVALID_CODE, coordinator.pair(network, television, phone, "12345a"))
        assertEquals(PairingCopy.NO_DEVICE, coordinator.pair(network, null, phone, "123456"))
        assertEquals(
            PairingCopy.NO_NETWORK,
            coordinator.pair(LocalNetwork.NoLocalNetwork, television, phone, "123456"),
        )
        assertIs<LinkState.Idle>(coordinator.state.value)
        assertNull(coordinator.installedListener)
    }

    @Test
    fun `a well formed code starts an attempt`() {
        assertNull(coordinator.pair(network, television, phone, "123456"))

        val connecting = assertIs<LinkState.Connecting>(coordinator.state.value)
        assertEquals("Fire TV Stick", connecting.deviceName)
        assertNotNull(coordinator.installedListener)
    }

    @Test
    fun `the retry after a mistyped code survives the first attempt closing`() {
        // The bug this pins. CastConnection.close does its goodbye on its own dispatcher, so the
        // first attempt's onClosed lands after the second has been opened. It used to null the
        // live connection and move the state to Closed, so the retry the user had just started
        // died silently -- and mistyping a six-digit code is how most people meet this path.
        assertNull(coordinator.pair(network, television, phone, "111111"))
        val first = assertNotNull(coordinator.installedListener)

        assertNull(coordinator.pair(network, television, phone, "222222"))
        val second = assertNotNull(coordinator.installedListener)
        assertTrue(first !== second, "the retry must install its own callbacks")

        first.onClosed(SessionFailure(SessionFailureKind.AUTHENTICATION, "That code was not accepted."))

        assertIs<LinkState.Connecting>(coordinator.state.value)
    }

    @Test
    fun `a retired attempt cannot report itself established`() {
        assertNull(coordinator.pair(network, television, phone, "111111"))
        val first = assertNotNull(coordinator.installedListener)
        assertNull(coordinator.pair(network, television, phone, "222222"))

        first.onEstablished(parameters(), token = null)

        assertIs<LinkState.Connecting>(coordinator.state.value)
        assertNull(coordinator.active())
    }

    @Test
    fun `a retired attempt cannot deliver receiver messages`() {
        val seen = mutableListOf<String>()
        coordinator.onMessage { seen += it::class.simpleName.orEmpty() }

        assertNull(coordinator.pair(network, television, phone, "111111"))
        val first = assertNotNull(coordinator.installedListener)
        assertNull(coordinator.pair(network, television, phone, "222222"))

        first.onMessage(ByeMessage(ByeReason.NORMAL, "gone"))

        assertTrue(seen.isEmpty(), "a replaced attempt still delivered $seen")
    }

    @Test
    fun `a deliberate close names the state and a late callback does not rename it`() {
        assertNull(coordinator.pair(network, television, phone, "111111"))
        val first = assertNotNull(coordinator.installedListener)

        coordinator.close()
        val closed = assertIs<LinkState.Closed>(coordinator.state.value)
        assertNull(closed.failure, "this phone asked for the close, so there is no failure to show")

        first.onClosed(SessionFailure(SessionFailureKind.TRANSPORT, "The socket went away."))

        assertNull(assertIs<LinkState.Closed>(coordinator.state.value).failure)
    }

    @Test
    fun `a failure on the attempt in progress is reported`() {
        assertNull(coordinator.pair(network, television, phone, "111111"))
        val listener = assertNotNull(coordinator.installedListener)

        listener.onClosed(SessionFailure(SessionFailureKind.AUTHENTICATION, "That code was not accepted."))

        val closed = assertIs<LinkState.Closed>(coordinator.state.value)
        assertEquals(SessionFailureKind.AUTHENTICATION, assertNotNull(closed.failure).kind)
    }

    @Test
    fun `forgetting everything closes the session and clears storage`() {
        assertNull(coordinator.pair(network, television, phone, "111111"))

        coordinator.forgetEverything()

        // The close is this thread's work and is done by the time the call returns. Clearing
        // storage is not: it is a file write, so it goes to the IO dispatcher and lands whenever
        // that thread gets to it.
        assertIs<LinkState.Closed>(coordinator.state.value)
        assertTrue(waitFor { tokens.forgotten }, "storage was never cleared")
    }

    private fun waitFor(condition: () -> Boolean): Boolean {
        val deadline = System.nanoTime() + 2_000_000_000L
        while (System.nanoTime() < deadline) {
            if (condition()) return true
            Thread.sleep(5)
        }
        return condition()
    }

    private fun parameters(): CastSessionParameters = CastSessionParameters(
        protocolVersion = 1,
        peer = HelloMessage(
            minimumVersion = 1,
            maximumVersion = 1,
            deviceName = "Fire TV Stick",
            codecCapabilities = setOf(CodecId.H264),
            screenWidth = 1920,
            screenHeight = 1080,
            densityDpi = 320,
        ),
        videoCodec = CodecId.H264,
    )
}
