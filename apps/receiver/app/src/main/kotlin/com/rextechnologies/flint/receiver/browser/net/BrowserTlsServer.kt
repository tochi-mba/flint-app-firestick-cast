package com.rextechnologies.flint.receiver.browser.net

import android.os.Build
import android.util.Log
import com.rextechnologies.flint.protocol.discovery.PairingCode
import com.rextechnologies.flint.protocol.wire.AuthMessage
import com.rextechnologies.flint.protocol.wire.AuthMethod
import com.rextechnologies.flint.protocol.wire.BrowserCapabilityMessage
import com.rextechnologies.flint.protocol.wire.BrowserCapabilityStatus
import com.rextechnologies.flint.protocol.wire.BrowserCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserDialogReplyMessage
import com.rextechnologies.flint.protocol.wire.BrowserInputMessage
import com.rextechnologies.flint.protocol.wire.BrowserLibraryCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserLibraryStateMessage
import com.rextechnologies.flint.protocol.wire.BrowserNetworkCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserProfileCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserTabCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserViewCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserWireLimits
import com.rextechnologies.flint.protocol.wire.BrowserWireRules
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceInputMessage
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceResizeMessage
import com.rextechnologies.flint.protocol.wire.ByeMessage
import com.rextechnologies.flint.protocol.wire.ByeReason
import com.rextechnologies.flint.protocol.wire.CodecId
import com.rextechnologies.flint.protocol.wire.HelloMessage
import com.rextechnologies.flint.protocol.wire.ProtocolVersion
import com.rextechnologies.flint.protocol.wire.VersionNegotiator
import com.rextechnologies.flint.protocol.wire.WireCodec
import com.rextechnologies.flint.protocol.wire.WireFrame
import com.rextechnologies.flint.protocol.wire.WireMessage
import com.rextechnologies.flint.receiver.browser.BrowserPreviewCapture
import com.rextechnologies.flint.receiver.browser.BrowserPreviewLoop
import com.rextechnologies.flint.receiver.browser.identity.ReceiverIdentity
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.Closeable
import java.io.IOException
import java.net.Inet4Address
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.SocketTimeoutException
import java.security.KeyStore
import java.security.SecureRandom
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicLong
import java.util.concurrent.atomic.AtomicReference
import javax.net.ssl.KeyManager
import javax.net.ssl.KeyManagerFactory
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLServerSocket
import javax.net.ssl.SSLServerSocketFactory
import javax.net.ssl.SSLSocket

/** Dedicated TLS listener for browser protocol v2+. Never multiplexed with ReceiverServer. */
class BrowserTlsServer(
    private val address: Inet4Address,
    private val identity: ReceiverIdentity,
    private val pairingCodeProvider: () -> PairingCode,
    private val listener: BrowserSecureSessionListener,
    private val deviceName: String = Build.MODEL?.takeIf { it.isNotBlank() } ?: "Fire TV",
    private val apiLevel: Int = Build.VERSION.SDK_INT,
    private val webViewVersion: String = "unknown",
    requestedPort: Int = 0,
    /**
     * How long a read may block before the socket reports a timeout.
     *
     * A deadline, not a liveness signal — see [dispatchLoop]. Injectable so the idle-session tests
     * do not have to wait two real minutes to prove it.
     */
    private val readTimeoutMillis: Int = SOCKET_TIMEOUT_MILLIS,
) : Closeable {
    var port: Int = requestedPort
        private set

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val closed = AtomicBoolean()
    private val activeClients = AtomicInteger(0)
    private val nextSessionId = AtomicLong(0)
    private val activeWriter = AtomicReference<BrowserReliableWriter?>(null)
    private var server: SSLServerSocket? = null

    private val _listening = MutableStateFlow(false)
    val listening: StateFlow<Boolean> = _listening.asStateFlow()

    fun start(): Result<Unit> = runCatching {
        check(server == null) { "Browser TLS server is already running" }
        val factory = sslServerSocketFactory()
        val candidate = factory.createServerSocket() as SSLServerSocket
        try {
            candidate.reuseAddress = true
            candidate.enabledProtocols = arrayOf("TLSv1.2")
            // Forward-secret suites only. The receiver's key is signing-only by design, so a suite
            // that asks the server to decrypt cannot work — and Conscrypt, left to choose, picks one
            // and fails inside OpenSSL after the socket is already open. See [BrowserCipherSuites].
            val suites = BrowserCipherSuites.select(candidate.supportedCipherSuites)
            check(suites.isNotEmpty()) {
                "This device offers no forward-secret cipher suite the receiver key can serve"
            }
            candidate.enabledCipherSuites = suites.toTypedArray()
            candidate.needClientAuth = false
            candidate.wantClientAuth = false
            candidate.bind(InetSocketAddress(address, port), BACKLOG)
        } catch (failure: Throwable) {
            runCatching { candidate.close() }
            throw failure
        }
        port = candidate.localPort
        server = candidate
        _listening.value = true
        // The port is ephemeral and advertised over mDNS, so it is the one fact nobody can guess.
        // Logged because a listener that started silently is indistinguishable from one that never
        // did when a host cannot reach it.
        Log.i(TAG, "Browser TLS listening on ${address.hostAddress}:$port")
        scope.launch { acceptLoop(candidate) }
    }

    fun send(outbound: BrowserOutboundMessage): Boolean {
        val writer = activeWriter.get() ?: return false
        val message = when (outbound) {
            is BrowserOutboundMessage.State -> outbound.message
            is BrowserOutboundMessage.Preview -> outbound.message
            is BrowserOutboundMessage.Dialog -> outbound.message
            is BrowserOutboundMessage.Raw -> outbound.message
        }
        return writer.enqueue(message)
    }

    private fun acceptLoop(serverSocket: SSLServerSocket) {
        while (scope.isActive && !closed.get()) {
            val client = try {
                serverSocket.accept() as SSLSocket
            } catch (_: IOException) {
                break
            }
            if (activeClients.get() > 0) {
                refuseExtraClient(client)
                continue
            }
            scope.launch { serveClient(client) }
        }
    }

    private fun serveClient(client: SSLSocket) {
        if (!activeClients.compareAndSet(0, 1)) {
            refuseExtraClient(client)
            return
        }
        var writer: BrowserReliableWriter? = null
        var authenticatedSessionId: Long? = null
        try {
            client.soTimeout = readTimeoutMillis
            // A peer that vanishes without closing — a stick unplugged, a hotspot dropped — would
            // otherwise hold the single client slot forever now that a read timeout no longer ends
            // the session. Keep-alive is what still detects that.
            client.keepAlive = true
            client.startHandshake()
            Log.i(TAG, "Browser TLS handshake started")
            val input = BufferedInputStream(client.inputStream)
            val output = BufferedOutputStream(client.outputStream)
            val handshake = completeHandshake(input, output) ?: run {
                Log.w(TAG, "Browser TLS handshake aborted before auth")
                return
            }
            val hostHello = handshake.hostHello
            val negotiatedVersion = handshake.negotiatedVersion
            val sessionId = nextSessionId.incrementAndGet()
            authenticatedSessionId = sessionId
            Log.i(
                TAG,
                "Browser TLS authenticated sessionId=$sessionId protocol=v$negotiatedVersion host=${hostHello.deviceName.trim().ifBlank {
                    "Windows device"
                }}",
            )
            writer = BrowserReliableWriter(output, negotiatedVersion).also {
                activeWriter.set(it)
                it.start(scope)
            }
            listener.onSessionAuthenticated(
                sessionId,
                hostHello.deviceName.trim().ifBlank { "Windows device" },
                negotiatedVersion,
            )
            Log.i(TAG, "Browser TLS dispatch loop enter sessionId=$sessionId")
            dispatchLoop(input, negotiatedVersion)
            Log.i(TAG, "Browser TLS dispatch loop exit sessionId=$sessionId")
        } catch (failure: Throwable) {
            if (!closed.get()) {
                Log.w(TAG, "Browser TLS session ended: ${failure.javaClass.simpleName}", failure)
            }
        } finally {
            activeWriter.compareAndSet(writer, null)
            writer?.close()
            runCatching { client.close() }
            // Teardown first, then the slot. Releasing the slot first lets the next host connect
            // and open a page while this session's cleanup is still running — and that cleanup
            // then wipes the *new* session's state, leaving the television on a blank page with
            // chrome that names nothing. The slot is what serialises the two, so it has to be the
            // last thing released.
            authenticatedSessionId?.let {
                Log.i(TAG, "Browser TLS session teardown sessionId=$it")
                listener.onSessionEnded(it)
            }
            activeClients.set(0)
        }
    }

    private data class HandshakeResult(
        val hostHello: HelloMessage,
        val negotiatedVersion: Int,
    )

    private fun completeHandshake(
        input: BufferedInputStream,
        output: BufferedOutputStream,
    ): HandshakeResult? {
        val helloFrame = WireCodec.readFrom(input) ?: run {
            Log.w(TAG, "Browser TLS hello missing")
            return null
        }
        if (helloFrame.protocolVersion < BROWSER_PROTOCOL_MINIMUM) {
            Log.w(TAG, "Browser TLS hello unsupported protocol=${helloFrame.protocolVersion}")
            writeBye(output, ByeReason.UNSUPPORTED_VERSION, "browser requires v2")
            return null
        }
        val hello = helloFrame.message as? HelloMessage ?: run {
            Log.w(TAG, "Browser TLS expected hello")
            writeBye(output, ByeReason.PROTOCOL_ERROR, "expected hello")
            return null
        }
        val negotiated = VersionNegotiator.negotiate(
            localMinimum = BROWSER_PROTOCOL_MINIMUM,
            localMaximum = ProtocolVersion.CURRENT,
            remote = hello,
        ) ?: run {
            Log.w(TAG, "Browser TLS version negotiate failed")
            writeBye(output, ByeReason.UNSUPPORTED_VERSION, "browser requires v2")
            return null
        }
        WireCodec.writeTo(output, WireFrame(negotiated, receiverHello()))
        output.flush()
        Log.i(TAG, "Browser TLS hello ok protocol=v$negotiated")

        val authFrame = WireCodec.readFrom(input) ?: run {
            Log.w(TAG, "Browser TLS auth missing")
            return null
        }
        requireNegotiated(authFrame, negotiated)
        when (val message = authFrame.message) {
            is AuthMessage -> {
                if (message.method != AuthMethod.PAIRING_CODE) {
                    Log.w(TAG, "Browser TLS auth method rejected")
                    writeBye(output, ByeReason.AUTHENTICATION_FAILED, "pairing required")
                    return null
                }
                val presented = String(message.credential.toByteArray(), Charsets.US_ASCII)
                if (!pairingCodeProvider().constantTimeMatches(presented)) {
                    Log.w(TAG, "Browser TLS pairing rejected")
                    writeBye(output, ByeReason.AUTHENTICATION_FAILED, "pairing rejected")
                    return null
                }
                Log.i(TAG, "Browser TLS pairing accepted")
            }
            else -> {
                if (BrowserWireRules.isBrowserMessage(message)) {
                    Log.w(TAG, "Browser TLS browser message before auth")
                    writeBye(output, ByeReason.PROTOCOL_ERROR, "auth required")
                } else {
                    Log.w(TAG, "Browser TLS expected auth")
                    writeBye(output, ByeReason.PROTOCOL_ERROR, "expected auth")
                }
                return null
            }
        }

        WireCodec.writeTo(output, WireFrame(negotiated, capability()))
        output.flush()
        return HandshakeResult(hello, negotiated)
    }

    /**
     * Reads authenticated browser frames until the peer goes away.
     *
     * A read timeout here is **not** the end of the session. `SO_TIMEOUT` is a deadline on one read,
     * and after authentication silence is the normal state of a browser: someone is reading a page
     * and not pressing anything. Treating that as death is what a Fire TV Stick 4K reported as
     * `SocketTimeoutException: Read timed out` exactly two minutes into a working session — the
     * television dropped to its idle screen while the desktop still showed "connected".
     *
     * The deadline still earns its place before authentication, where a silent peer is holding the
     * single client slot for nothing. Liveness after that is TCP keep-alive's job, not this loop's.
     */
    private fun dispatchLoop(input: BufferedInputStream, negotiatedVersion: Int) {
        while (!closed.get()) {
            val frame = try {
                WireCodec.readFrom(input) ?: break
            } catch (_: SocketTimeoutException) {
                continue
            }
            requireNegotiated(frame, negotiatedVersion)
            when (val message = frame.message) {
                is BrowserCommandMessage -> listener.onCommand(message)
                is BrowserInputMessage -> listener.onInput(message)
                is BrowserDialogReplyMessage -> listener.onDialogReply(message)
                is BrowserTabCommandMessage -> listener.onTabCommand(message)
                is BrowserViewCommandMessage -> listener.onViewCommand(message)
                is BrowserLibraryCommandMessage -> listener.onLibraryCommand(message)
                is BrowserLibraryStateMessage -> listener.onLibraryState(message)
                is BrowserProfileCommandMessage -> listener.onProfileCommand(message)
                is BrowserNetworkCommandMessage -> listener.onNetworkCommand(message)
                is BrowserWorkspaceCommandMessage -> listener.onWorkspaceCommand(message)
                is BrowserWorkspaceResizeMessage -> listener.onWorkspaceResize(message)
                is BrowserWorkspaceInputMessage -> listener.onWorkspaceInput(message)
                is ByeMessage -> break
                else -> {
                    if (BrowserWireRules.isBrowserMessage(message)) {
                        // Already authenticated path; unknown browser subtype is ignored safely.
                        continue
                    }
                    throw IOException("Non-browser message on BrowserTlsServer")
                }
            }
        }
    }

    private fun refuseExtraClient(client: Socket) {
        Log.w(TAG, "Browser TLS refused extra client (one session only)")
        runCatching {
            if (client is SSLSocket) {
                client.startHandshake()
                val output = BufferedOutputStream(client.outputStream)
                writeBye(output, ByeReason.PROTOCOL_ERROR, "one browser client")
            }
        }
        runCatching { client.close() }
    }

    private fun receiverHello(): HelloMessage = HelloMessage(
        minimumVersion = BROWSER_PROTOCOL_MINIMUM,
        maximumVersion = ProtocolVersion.CURRENT,
        deviceName = deviceName.take(64).ifBlank { "Fire TV" },
        codecCapabilities = setOf(CodecId.H264),
        screenWidth = 1920,
        screenHeight = 1080,
        densityDpi = 320,
    )

    private fun capability(): BrowserCapabilityMessage = BrowserCapabilityMessage(
        status = BrowserCapabilityStatus.AVAILABLE,
        secureEndpointPort = port,
        apiLevel = apiLevel.coerceIn(1, 0xffff),
        webViewVersion = webViewVersion.take(BrowserWireLimits.MAX_TITLE_BYTES),
        // Honest capture limits from BrowserPreviewLoop / BrowserPreviewCapture — not aspirational.
        previewSupported = true,
        previewMaxWidth = BrowserPreviewLoop.MAX_WIDTH,
        previewMaxHeight = BrowserPreviewLoop.MAX_HEIGHT,
        interactivePreviewFramesPerSecond = BrowserPreviewLoop.INTERACTIVE_FPS,
        idlePreviewFramesPerSecond = BrowserPreviewLoop.IDLE_FPS,
        previewMaxBytes = BrowserPreviewCapture.MAX_JPEG_BYTES,
        detail = "secure receiver ready",
    )

    private fun sslServerSocketFactory(): SSLServerSocketFactory {
        val keyManagers = identity.androidKeyStoreAlias
            ?.let(::androidKeystoreKeyManagers)
            ?: inProcessKeyManagers()
        val context = SSLContext.getInstance("TLSv1.2").apply {
            init(keyManagers, null, SecureRandom())
        }
        return context.serverSocketFactory
    }

    /**
     * Key managers backed by the Android Keystore, for a key that cannot leave it.
     *
     * The private key is used in place: the factory is handed the Keystore itself and signs through
     * it. Copying the key into a software keystore first is the obvious approach and cannot work —
     * `getEncoded()` on a Keystore-backed key returns null, and the provider then fails with a
     * `KeyStoreException` wrapping a `NullPointerException` that names neither the key nor the
     * cause. That shipped, and it disabled the browser on every device that had a working Keystore.
     */
    private fun androidKeystoreKeyManagers(alias: String): Array<KeyManager> {
        val keyStore = KeyStore.getInstance(ANDROID_KEY_STORE).apply { load(null) }
        check(keyStore.containsAlias(alias)) { "Android Keystore has no entry for $alias" }
        return KeyManagerFactory.getInstance(KEY_MANAGER_ALGORITHM).apply {
            // No password: entries in the Android Keystore are protected by the system, not by one.
            init(keyStore, null)
        }.keyManagers
    }

    /**
     * Key managers for an ordinary in-process key.
     *
     * Used on development devices whose Keystore is unavailable, and by the unit tests, which run on
     * a desktop JVM where there is no Android Keystore at all.
     */
    private fun inProcessKeyManagers(): Array<KeyManager> {
        val keyStore = KeyStore.getInstance(KeyStore.getDefaultType()).apply {
            load(null, CharArray(0))
            setKeyEntry(
                "browser",
                identity.keyPair.private,
                CharArray(0),
                arrayOf(identity.certificate),
            )
        }
        return KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm()).apply {
            init(keyStore, CharArray(0))
        }.keyManagers
    }

    private fun writeBye(output: BufferedOutputStream, reason: ByeReason, detail: String) {
        WireCodec.writeTo(output, WireFrame(BROWSER_PROTOCOL_MINIMUM, ByeMessage(reason, detail)))
        output.flush()
    }

    private fun requireNegotiated(frame: WireFrame, negotiatedVersion: Int) {
        if (negotiatedVersion < BROWSER_PROTOCOL_MINIMUM || frame.protocolVersion != negotiatedVersion) {
            throw IOException("Browser TLS requires negotiated protocol v$negotiatedVersion")
        }
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) return
        _listening.value = false
        activeWriter.getAndSet(null)?.close()
        runCatching { server?.close() }
        server = null
        scope.cancel()
    }

    private companion object {
        const val TAG = "BrowserTlsServer"
        const val BROWSER_PROTOCOL_MINIMUM = 2

        /** The Android Keystore provider name. */
        private const val ANDROID_KEY_STORE = "AndroidKeyStore"

        /**
         * The key manager algorithm that accepts an Android Keystore.
         *
         * `KeyManagerFactory.getDefaultAlgorithm()` returns "PKIX" on Android, whose factory does
         * not accept the Keystore; "X509" does.
         */
        private const val KEY_MANAGER_ALGORITHM = "X509"
        const val BACKLOG = 4
        const val SOCKET_TIMEOUT_MILLIS = 120_000
    }
}

/** Single-writer mailbox: control/state ordered, capacity bounded, no task-per-message. */
internal class BrowserReliableWriter(
    private val output: BufferedOutputStream,
    private val protocolVersion: Int,
    private val capacity: Int = 32,
) : Closeable {
    private val queue = ArrayBlockingQueue<WireMessage>(capacity)
    private val closed = AtomicBoolean()

    fun start(scope: CoroutineScope) {
        scope.launch {
            while (!closed.get()) {
                val message = queue.poll(250, TimeUnit.MILLISECONDS) ?: continue
                try {
                    WireCodec.writeTo(output, WireFrame(protocolVersion, message))
                    output.flush()
                } catch (_: IOException) {
                    break
                }
            }
        }
    }

    fun enqueue(message: WireMessage): Boolean {
        if (closed.get()) return false
        return queue.offer(message)
    }

    override fun close() {
        closed.set(true)
        queue.clear()
    }
}
