package com.rextechnologies.flint.mobile.state

import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.PhoneCapabilities
import com.rextechnologies.flint.castcore.capability.ReceiverDevice
import com.rextechnologies.flint.castcore.copy.PairingCopy
import com.rextechnologies.flint.castcore.session.SessionFailure
import com.rextechnologies.flint.mobile.net.CastConnection
import com.rextechnologies.flint.mobile.net.CastConnectionListener
import com.rextechnologies.flint.mobile.platform.TokenStore
import com.rextechnologies.flint.protocol.BinaryData
import com.rextechnologies.flint.protocol.http.SessionToken
import com.rextechnologies.flint.protocol.session.CastSessionParameters
import com.rextechnologies.flint.protocol.session.DeviceProfile
import com.rextechnologies.flint.protocol.wire.AuthMethod
import com.rextechnologies.flint.protocol.wire.WireMessage
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import java.net.Inet4Address

/** Where the link to one television has got to. */
sealed interface LinkState {
    /** Nothing is connected and nothing is being attempted. */
    data object Idle : LinkState

    data class Connecting(val deviceName: String) : LinkState

    data class Connected(
        val device: ReceiverDevice,
        val parameters: CastSessionParameters,
    ) : LinkState

    /** The last attempt ended. [failure] is `null` only when this phone asked it to. */
    data class Closed(val failure: SessionFailure?) : LinkState
}

/**
 * Holds the one connection to the one television.
 *
 * The connection is opened by pairing and kept open afterwards: a phone that reconnected for every
 * action would show the television a pairing code every time, which is exactly the friction the
 * granted token exists to remove.
 *
 * Nothing in here draws or encodes. What it owns is the socket, the state machine behind it, and the
 * credential that opened it — the media path is [OutputCoordinator]'s, and it drives this one
 * through [send].
 */
class SessionCoordinator(
    private val tokens: TokenStore,
    private val scope: CoroutineScope,
    private val connect: (CastConnection) -> Unit = CastConnection::connect,
) : MediaLink {
    private val mutable = MutableStateFlow<LinkState>(LinkState.Idle)
    val state: StateFlow<LinkState> = mutable

    @Volatile
    private var connection: CastConnection? = null

    /** Messages the television sends after the handshake, for whoever is driving the media path. */
    private val listeners = mutableListOf<(WireMessage) -> Unit>()

    override fun onMessage(listener: (WireMessage) -> Unit) {
        synchronized(listeners) { listeners += listener }
    }

    override val isConnected: Boolean
        get() = mutable.value is LinkState.Connected

    /**
     * Opens a session with a typed pairing code.
     *
     * @return the failure to show, or `null` when the attempt was started. It is started, not
     *   finished: whether the television accepts the code is something only the television knows,
     *   and the answer arrives on [state].
     */
    fun pair(
        network: LocalNetwork,
        device: ReceiverDevice?,
        phone: PhoneCapabilities,
        code: String,
    ): String? {
        if (!PairingCopy.isWellFormed(code)) return PairingCopy.INVALID_CODE
        val target = device ?: return PairingCopy.NO_DEVICE
        val bound = network.boundAddress ?: return PairingCopy.NO_NETWORK
        open(bound, target, phone, AuthMethod.PAIRING_CODE, code)
        return null
    }

    /**
     * Reopens a session with the token this television granted earlier, if it granted one.
     *
     * @return whether there was a token to try. `false` is not a failure — it is a television this
     *   phone has never paired with.
     */
    suspend fun reconnect(
        network: LocalNetwork,
        device: ReceiverDevice,
        phone: PhoneCapabilities,
    ): Boolean {
        val bound = network.boundAddress ?: return false
        val token = withContext(Dispatchers.IO) { tokens.tokenFor(device.address) } ?: return false
        open(bound, device, phone, AuthMethod.SESSION_TOKEN, token.toString())
        return true
    }

    /** Whether this television has already granted a token. Reads storage, so it suspends. */
    suspend fun isRemembered(device: ReceiverDevice?): Boolean {
        val address = device?.address ?: return false
        return withContext(Dispatchers.IO) { tokens.tokenFor(address) != null }
    }

    /**
     * Sends a control message, or `false` when there is no session to send it on.
     *
     * On the IO dispatcher, whatever thread asks. Every caller of this is a coroutine on the main
     * dispatcher, and a socket write there is a `NetworkOnMainThreadException` -- which `send`
     * swallowed into a `false` nobody read, so the SURFACE message that tells the television to
     * switch screens was never leaving the phone.
     */
    override suspend fun send(message: WireMessage): Boolean {
        val current = connection ?: return false
        return withContext(Dispatchers.IO) { current.send(message) }
    }

    /** The live connection, for the media path. `null` whenever nothing is established. */
    fun active(): CastConnection? =
        connection?.takeIf { mutable.value is LinkState.Connected }

    /** Ends the session. Safe to call when there is none. */
    fun close() {
        val current = connection ?: return
        connection = null
        current.close()
        mutable.update { if (it is LinkState.Closed) it else LinkState.Closed(null) }
    }

    /** Unpairs every television, for the setting that exists so a lent phone can be handed back. */
    fun forgetEverything() {
        close()
        scope.launch { withContext(Dispatchers.IO) { tokens.forgetEverything() } }
    }

    private fun open(
        bound: Inet4Address,
        device: ReceiverDevice,
        phone: PhoneCapabilities,
        method: AuthMethod,
        credential: String,
    ) {
        // One at a time. The receiver admits one phone and refuses the second after a successful
        // handshake, so two connections from the same phone would have it refuse itself.
        connection?.close()
        mutable.value = LinkState.Connecting(device.displayName)

        val opened = CastConnection(
            localAddress = bound,
            remoteAddress = device.address,
            remotePort = device.port,
            profile = profileFor(phone),
            authMethod = method,
            credential = BinaryData.of(credential.toByteArray(Charsets.US_ASCII)),
            listener = Listener(device),
        )
        connection = opened
        connect(opened)
    }

    private fun profileFor(phone: PhoneCapabilities): DeviceProfile = DeviceProfile(
        deviceName = phone.deviceName,
        // What the probe found, not what the API level implies. An empty set is a phone that has not
        // been asked, and the capability verdicts block every streaming mode until it has been.
        codecCapabilities = phone.advertisedCodecs,
        screenWidth = phone.screenWidth,
        screenHeight = phone.screenHeight,
        densityDpi = phone.densityDpi,
    )

    private inner class Listener(private val device: ReceiverDevice) : CastConnectionListener {
        override fun onEstablished(parameters: CastSessionParameters, token: SessionToken?) {
            // Written off the main thread, because it is a keystore round trip and an
            // AES-GCM encrypt. This callback arrives on the connection's own IO dispatcher, but
            // saying so here rather than relying on it is what keeps that true after an edit.
            token?.let { granted ->
                scope.launch {
                    withContext(Dispatchers.IO) { tokens.remember(device.address, granted) }
                }
            }
            mutable.value = LinkState.Connected(device, parameters)
        }

        override fun onMessage(message: WireMessage) {
            val snapshot = synchronized(listeners) { listeners.toList() }
            snapshot.forEach { it(message) }
        }

        override fun onClosed(failure: SessionFailure?) {
            connection = null
            mutable.value = LinkState.Closed(failure)
        }
    }

    /**
     * Waits for the current attempt to settle, one way or the other.
     *
     * Separate from [pair] because pairing is started from a button press, and whether the caller
     * waits for the answer is the caller's decision rather than this class's. On a timeout it
     * returns whatever the state is then, which is [LinkState.Connecting] -- an honest answer, and
     * a different one from "failed".
     */
    suspend fun awaitSettled(timeoutMillis: Long = ESTABLISH_TIMEOUT_MILLIS): LinkState {
        val settled = CompletableDeferred<LinkState>()
        val job = scope.launch {
            state.collect { value ->
                if (value is LinkState.Connected || value is LinkState.Closed) settled.complete(value)
            }
        }
        return try {
            withTimeoutOrNull(timeoutMillis) { settled.await() } ?: mutable.value
        } finally {
            job.cancel()
        }
    }

    companion object {
        /** How long a caller waits for a session before it gives up and says so. */
        const val ESTABLISH_TIMEOUT_MILLIS: Long = 8_000
    }
}
