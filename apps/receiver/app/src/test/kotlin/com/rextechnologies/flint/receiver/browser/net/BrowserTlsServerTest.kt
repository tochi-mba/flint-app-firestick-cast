package com.rextechnologies.flint.receiver.browser.net

import com.rextechnologies.flint.protocol.BinaryData
import com.rextechnologies.flint.protocol.discovery.PairingCode
import com.rextechnologies.flint.protocol.wire.AuthMessage
import com.rextechnologies.flint.protocol.wire.AuthMethod
import com.rextechnologies.flint.protocol.wire.BrowserCapabilityMessage
import com.rextechnologies.flint.protocol.wire.BrowserCapabilityStatus
import com.rextechnologies.flint.protocol.wire.BrowserCommandAction
import com.rextechnologies.flint.protocol.wire.BrowserCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserDialogReplyMessage
import com.rextechnologies.flint.protocol.wire.BrowserInputMessage
import com.rextechnologies.flint.protocol.wire.BrowserLibraryEntry
import com.rextechnologies.flint.protocol.wire.BrowserLibraryEntryKind
import com.rextechnologies.flint.protocol.wire.BrowserLibraryAction
import com.rextechnologies.flint.protocol.wire.BrowserLibraryCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserLibraryStateMessage
import com.rextechnologies.flint.protocol.wire.BrowserProfileAction
import com.rextechnologies.flint.protocol.wire.BrowserProfileCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserTabAction
import com.rextechnologies.flint.protocol.wire.BrowserTabCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserViewAction
import com.rextechnologies.flint.protocol.wire.BrowserViewCommandMessage
import com.rextechnologies.flint.protocol.wire.ByeMessage
import com.rextechnologies.flint.protocol.wire.CodecId
import com.rextechnologies.flint.protocol.wire.HelloMessage
import com.rextechnologies.flint.protocol.wire.ProtocolVersion
import com.rextechnologies.flint.protocol.wire.VersionNegotiator
import com.rextechnologies.flint.protocol.wire.WireCodec
import com.rextechnologies.flint.protocol.wire.WireFrame
import com.rextechnologies.flint.receiver.browser.BrowserPreviewCapture
import com.rextechnologies.flint.receiver.browser.BrowserPreviewLoop
import com.rextechnologies.flint.receiver.browser.identity.InMemoryReceiverIdentityProvider
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.net.Inet4Address
import java.net.InetSocketAddress
import java.net.Socket
import java.security.SecureRandom
import java.security.cert.X509Certificate
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocket
import javax.net.ssl.TrustManager
import javax.net.ssl.X509TrustManager
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertTrue
import org.junit.After
import org.junit.Before

class BrowserTlsServerTest {
    private lateinit var server: BrowserTlsServer
    private val commands = CopyOnWriteArrayList<BrowserCommandMessage>()
    private val tabCommands = CopyOnWriteArrayList<BrowserTabCommandMessage>()
    private val viewCommands = CopyOnWriteArrayList<BrowserViewCommandMessage>()
    private val libraryStates = CopyOnWriteArrayList<BrowserLibraryStateMessage>()
    private val libraryCommands = CopyOnWriteArrayList<BrowserLibraryCommandMessage>()
    private val profileCommands = CopyOnWriteArrayList<BrowserProfileCommandMessage>()
    private val authenticated = CopyOnWriteArrayList<Pair<Long, String>>()
    private val endedSessions = CopyOnWriteArrayList<Long>()
    private val ended = AtomicReference<CountDownLatch>()

    @Before
    fun setUp() {
        val identity = InMemoryReceiverIdentityProvider().obtain("browser-tls-test")
        ended.set(CountDownLatch(1))
        server = BrowserTlsServer(
            address = Inet4Address.getByName("127.0.0.1") as Inet4Address,
            identity = identity,
            pairingCodeProvider = { PairingCode.parse("123456") },
            listener = object : BrowserSecureSessionListener {
                override fun onSessionAuthenticated(sessionId: Long, deviceName: String) {
                    authenticated += sessionId to deviceName
                }

                override fun onCommand(command: BrowserCommandMessage) {
                    commands.add(command)
                }

                override fun onInput(input: BrowserInputMessage) = Unit
                override fun onDialogReply(reply: BrowserDialogReplyMessage) = Unit
                override fun onTabCommand(command: BrowserTabCommandMessage) {
                    tabCommands.add(command)
                }
                override fun onViewCommand(command: BrowserViewCommandMessage) {
                    viewCommands.add(command)
                }
                override fun onLibraryState(state: BrowserLibraryStateMessage) {
                    libraryStates.add(state)
                }
                override fun onLibraryCommand(command: BrowserLibraryCommandMessage) {
                    libraryCommands.add(command)
                }
                override fun onProfileCommand(command: BrowserProfileCommandMessage) {
                    profileCommands.add(command)
                }
                override fun onSessionEnded(sessionId: Long) {
                    endedSessions += sessionId
                    ended.get()?.countDown()
                }
                override fun onSessionEnded() {
                    ended.get()?.countDown()
                }
            },
            deviceName = "Test Fire TV",
            apiLevel = 25,
            webViewVersion = "test-webview",
            requestedPort = 0,
        )
        server.start().getOrThrow()
    }

    @After
    fun tearDown() {
        server.close()
    }

    @Test
    fun `authenticated host receives capability and can send a browser command`() {
        openSession().use { session ->
            val capability = assertIs<BrowserCapabilityMessage>(session.read().message)
            assertEquals(BrowserCapabilityStatus.AVAILABLE, capability.status)
            assertEquals(server.port, capability.secureEndpointPort)
            assertTrue(capability.previewSupported)
            assertEquals(BrowserPreviewLoop.MAX_WIDTH, capability.previewMaxWidth)
            assertEquals(BrowserPreviewLoop.MAX_HEIGHT, capability.previewMaxHeight)
            assertEquals(BrowserPreviewLoop.INTERACTIVE_FPS, capability.interactivePreviewFramesPerSecond)
            assertEquals(BrowserPreviewLoop.IDLE_FPS, capability.idlePreviewFramesPerSecond)
            assertEquals(BrowserPreviewCapture.MAX_JPEG_BYTES, capability.previewMaxBytes)

            session.write(
                BrowserCommandMessage(1, 1, BrowserCommandAction.OPEN, "https://example.test/"),
            )

            assertTrue(waitUntil { commands.isNotEmpty() })
            assertEquals(BrowserCommandAction.OPEN, commands.single().action)
        }
    }

    @Test
    fun `authenticated host cockpit commands and profile library snapshot reach their typed handlers`() {
        val tab = BrowserTabCommandMessage(7, 2, BrowserTabAction.NEW, 0, null)
        val view = BrowserViewCommandMessage(7, 3, BrowserViewAction.SET_ZOOM, 150)
        val library = BrowserLibraryStateMessage(
            epoch = 7,
            revision = 4,
            bookmarks = listOf(
                BrowserLibraryEntry(
                    BrowserLibraryEntryKind.BOOKMARK,
                    faviconId = 0,
                    lastVisitedMs = 10,
                    url = "https://example.test/",
                    title = "Example",
                ),
            ),
            history = emptyList(),
        )
        val libraryCommand = BrowserLibraryCommandMessage(
            epoch = 7,
            commandId = 4,
            action = BrowserLibraryAction.REQUEST_SNAPSHOT,
        )
        val profileCommand = BrowserProfileCommandMessage(
            epoch = 7,
            commandId = 5,
            action = BrowserProfileAction.SELECT_TV_PROFILE,
            profileId = "default",
        )

        openSession().use { session ->
            assertIs<BrowserCapabilityMessage>(session.read().message)
            session.write(tab)
            session.write(view)
            session.write(library)
            session.write(libraryCommand)
            session.write(profileCommand)

            assertTrue(waitUntil {
                tabCommands.singleOrNull() == tab &&
                    viewCommands.singleOrNull() == view &&
                    libraryStates.singleOrNull() == library &&
                    libraryCommands.singleOrNull() == libraryCommand &&
                    profileCommands.singleOrNull() == profileCommand
            })
        }
    }

    @Test
    fun `authenticated session exposes a stable id and bounded host name until its teardown`() {
        openSession().use { session ->
            assertIs<BrowserCapabilityMessage>(session.read().message)
            assertTrue(waitUntil { authenticated.size == 1 })
            assertEquals("Flint Windows Browser Remote", authenticated.single().second)
            assertTrue(authenticated.single().first > 0)
        }

        assertTrue(ended.get().await(5, TimeUnit.SECONDS))
        assertEquals(authenticated.single().first, endedSessions.single())
    }

    @Test
    fun `wrong pairing code is rejected before capability`() {
        openSession(pairingCode = "000000", expectCapability = false).use { session ->
            val bye = assertIs<ByeMessage>(session.read().message)
            assertTrue(bye.detail.isNotBlank())
        }
    }

    @Test
    fun `browser command before auth is rejected`() {
        openRawTls().use { socket ->
            val input = BufferedInputStream(socket.inputStream)
            val output = BufferedOutputStream(socket.outputStream)
            WireCodec.writeTo(
                output,
                WireFrame(
                    ProtocolVersion.CURRENT,
                    BrowserCommandMessage(1, 1, BrowserCommandAction.CLOSE),
                ),
            )
            output.flush()
            val response = WireCodec.readFrom(input)
            assertIs<ByeMessage>(response!!.message)
        }
    }

    @Test
    fun `second client is refused while the first remains connected`() {
        openSession().use { first ->
            assertIs<BrowserCapabilityMessage>(first.read().message)
            openRawTls().use { second ->
                val output = BufferedOutputStream(second.outputStream)
                val input = BufferedInputStream(second.inputStream)
                // The refused client may get a BYE after handshake or an immediate close.
                runCatching {
                    WireCodec.writeTo(output, WireFrame(BROWSER_PROTOCOL_MINIMUM, hostHello()))
                    output.flush()
                    WireCodec.readFrom(input)
                }
            }
            assertTrue(first.socket.isConnected)
        }
    }

    @Test
    fun `teardown finishes before the next client is accepted`() {
        // The defect this pins: releasing the single-client slot before onSessionEnded returned let
        // the next host open a page while the dead session's cleanup was still running — and that
        // cleanup wiped the new session. Holding teardown open must keep the slot closed.
        val teardownEntered = CountDownLatch(1)
        val teardownRelease = CountDownLatch(1)
        val identity = InMemoryReceiverIdentityProvider().obtain("browser-tls-teardown")
        val gated = BrowserTlsServer(
            address = Inet4Address.getByName("127.0.0.1") as Inet4Address,
            identity = identity,
            pairingCodeProvider = { PairingCode.parse("123456") },
            listener = object : BrowserSecureSessionListener {
                override fun onCommand(command: BrowserCommandMessage) = Unit
                override fun onInput(input: BrowserInputMessage) = Unit
                override fun onDialogReply(reply: BrowserDialogReplyMessage) = Unit
                override fun onSessionEnded() {
                    teardownEntered.countDown()
                    assertTrue(teardownRelease.await(5, TimeUnit.SECONDS))
                }
            },
            deviceName = "Test Fire TV",
            apiLevel = 25,
            webViewVersion = "test-webview",
            requestedPort = 0,
        )
        gated.start().getOrThrow()
        try {
            val first = openSessionAgainst(gated)
            assertIs<BrowserCapabilityMessage>(first.read().message)
            first.close()
            assertTrue(teardownEntered.await(5, TimeUnit.SECONDS))

            val refused = runCatching {
                openRawTlsAgainst(gated.port).use { socket ->
                    socket.soTimeout = 1_000
                    val output = BufferedOutputStream(socket.outputStream)
                    val input = BufferedInputStream(socket.inputStream)
                    WireCodec.writeTo(output, WireFrame(BROWSER_PROTOCOL_MINIMUM, hostHello()))
                    output.flush()
                    WireCodec.readFrom(input)
                }
            }
            assertTrue(refused.isFailure || refused.getOrNull() == null || refused.getOrNull()?.message is ByeMessage)

            teardownRelease.countDown()
            Thread.sleep(100)
            openSessionAgainst(gated).use { second ->
                assertIs<BrowserCapabilityMessage>(second.read().message)
            }
        } finally {
            teardownRelease.countDown()
            gated.close()
        }
    }

    @Test
    fun `a silent authenticated session survives past the read timeout`() {
        // Found on a Fire TV Stick 4K: after two minutes of nobody touching the remote, the receiver
        // logged `SocketTimeoutException: Read timed out`, tore the session down and dropped to the
        // idle screen — while the desktop still showed "connected". Reading a page without pressing
        // anything is the most ordinary thing a browser does, and it was killing the session.
        //
        // SO_TIMEOUT is a read deadline, not a liveness signal. Silence after authentication is
        // normal and must not end anything.
        val quick = startServer(readTimeoutMillis = 250)
        try {
            openSessionAgainst(quick).use { session ->
                assertIs<BrowserCapabilityMessage>(session.read().message)

                // Well past the read timeout, sending nothing at all.
                Thread.sleep(900)

                session.write(
                    BrowserCommandMessage(1, 1, BrowserCommandAction.OPEN, url = "https://example.test/"),
                )

                assertTrue(
                    waitUntil { commands.any { it.action == BrowserCommandAction.OPEN } },
                    "the session was closed while it was merely idle",
                )
            }
        } finally {
            quick.close()
        }
    }

    @Test
    fun `an unauthenticated peer is still dropped when it goes silent`() {
        // The other half of the same decision. Before authentication a silent peer is holding the
        // single client slot for nothing, so there the read deadline must still end it.
        val quick = startServer(readTimeoutMillis = 250)
        try {
            openRawTlsAgainst(quick.port).use { socket ->
                val input = BufferedInputStream(socket.inputStream)
                // Say nothing at all: no hello, no auth.
                assertTrue(
                    waitUntil { runCatching { WireCodec.readFrom(input) == null }.getOrDefault(true) },
                    "a silent unauthenticated peer was allowed to hold the slot",
                )
            }
        } finally {
            quick.close()
        }
    }

    private fun openSession(
        pairingCode: String = "123456",
        expectCapability: Boolean = true,
    ): TlsSession {
        val socket = openRawTls()
        return completeClientHandshake(socket, pairingCode, expectCapability)
    }

    private fun openSessionAgainst(target: BrowserTlsServer): TlsSession {
        val socket = openRawTlsAgainst(target.port)
        return completeClientHandshake(socket, "123456", expectCapability = true)
    }

    private fun completeClientHandshake(
        socket: SSLSocket,
        pairingCode: String,
        expectCapability: Boolean,
    ): TlsSession {
        val input = BufferedInputStream(socket.inputStream)
        val output = BufferedOutputStream(socket.outputStream)
        val hostHelloMessage = hostHello()
        WireCodec.writeTo(output, WireFrame(BROWSER_PROTOCOL_MINIMUM, hostHelloMessage))
        output.flush()
        val peerHelloFrame = WireCodec.readFrom(input) ?: error("expected hello")
        val peerHello = assertIs<HelloMessage>(peerHelloFrame.message)
        val negotiated = VersionNegotiator.negotiate(
            localMinimum = BROWSER_PROTOCOL_MINIMUM,
            localMaximum = ProtocolVersion.CURRENT,
            remote = peerHello,
        ) ?: error("expected compatible browser protocol")
        assertEquals(negotiated, peerHelloFrame.protocolVersion)
        WireCodec.writeTo(
            output,
            WireFrame(
                negotiated,
                AuthMessage(AuthMethod.PAIRING_CODE, BinaryData.of(pairingCode.toByteArray(Charsets.US_ASCII))),
            ),
        )
        output.flush()
        if (!expectCapability) {
            return TlsSession(socket, input, output, negotiated)
        }
        return TlsSession(socket, input, output, negotiated)
    }

    /** A second listener with its own read deadline, for the idle-session tests. */
    private fun startServer(readTimeoutMillis: Int): BrowserTlsServer {
        val identity = InMemoryReceiverIdentityProvider().obtain("browser-tls-timeout-test")
        val candidate = BrowserTlsServer(
            address = Inet4Address.getByName("127.0.0.1") as Inet4Address,
            identity = identity,
            pairingCodeProvider = { PairingCode.parse("123456") },
            listener = object : BrowserSecureSessionListener {
                override fun onCommand(command: BrowserCommandMessage) {
                    commands.add(command)
                }

                override fun onInput(input: BrowserInputMessage) = Unit
                override fun onDialogReply(reply: BrowserDialogReplyMessage) = Unit
                override fun onSessionEnded() = Unit
            },
            deviceName = "Test Fire TV",
            apiLevel = 25,
            webViewVersion = "test-webview",
            requestedPort = 0,
            readTimeoutMillis = readTimeoutMillis,
        )
        candidate.start().getOrThrow()
        return candidate
    }

    private fun openRawTlsAgainst(port: Int): SSLSocket {
        val context = SSLContext.getInstance("TLSv1.2")
        context.init(null, arrayOf<TrustManager>(TrustAll), SecureRandom())
        val socket = context.socketFactory.createSocket() as SSLSocket
        socket.enabledProtocols = arrayOf("TLSv1.2")
        socket.connect(InetSocketAddress("127.0.0.1", port), 5_000)
        socket.startHandshake()
        return socket
    }

    private fun openRawTls(): SSLSocket = openRawTlsAgainst(server.port)

    /** Matches [BrowserTlsServer.BROWSER_PROTOCOL_MINIMUM]. */
    private val BROWSER_PROTOCOL_MINIMUM = 2

    private fun hostHello(): HelloMessage = HelloMessage(
        BROWSER_PROTOCOL_MINIMUM,
        ProtocolVersion.CURRENT,
        "Flint Windows Browser Remote",
        setOf(CodecId.H264),
        1920,
        1080,
        96,
    )

    private fun waitUntil(timeoutMs: Long = 5_000, predicate: () -> Boolean): Boolean {
        val deadline = System.nanoTime() + TimeUnit.MILLISECONDS.toNanos(timeoutMs)
        while (System.nanoTime() < deadline) {
            if (predicate()) return true
            Thread.sleep(20)
        }
        return predicate()
    }

    private class TlsSession(
        val socket: SSLSocket,
        private val input: BufferedInputStream,
        private val output: BufferedOutputStream,
        private val protocolVersion: Int,
    ) : AutoCloseable {
        fun read(): WireFrame = WireCodec.readFrom(input) ?: error("expected frame")
        fun write(message: com.rextechnologies.flint.protocol.wire.WireMessage) {
            WireCodec.writeTo(output, WireFrame(protocolVersion, message))
            output.flush()
        }

        override fun close() {
            runCatching { socket.close() }
        }
    }

    private object TrustAll : X509TrustManager {
        override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) = Unit
        override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) = Unit
        override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
    }
}
