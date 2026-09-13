package com.rextechnologies.flint.receiver.net

import android.os.Build
import android.util.Log
import com.rextechnologies.flint.protocol.discovery.PairingCode
import com.rextechnologies.flint.protocol.discovery.ReceiverProbe
import com.rextechnologies.flint.protocol.http.SessionToken
import com.rextechnologies.flint.protocol.session.CastSessionParameters
import com.rextechnologies.flint.protocol.session.DeviceProfile
import com.rextechnologies.flint.protocol.session.HandshakeOutcome
import com.rextechnologies.flint.protocol.session.ReceiverHandshake
import com.rextechnologies.flint.protocol.wire.ByeMessage
import com.rextechnologies.flint.protocol.wire.ByeReason
import com.rextechnologies.flint.protocol.wire.AudioConfigMessage
import com.rextechnologies.flint.protocol.wire.AudioPacket
import com.rextechnologies.flint.protocol.wire.BrowserWireRules
import com.rextechnologies.flint.protocol.wire.CodecId
import com.rextechnologies.flint.protocol.wire.ControlMessage
import com.rextechnologies.flint.protocol.wire.HelloMessage
import com.rextechnologies.flint.protocol.wire.MediaCommandMessage
import com.rextechnologies.flint.protocol.wire.MediaDataMessage
import com.rextechnologies.flint.protocol.wire.ProtocolVersion
import com.rextechnologies.flint.protocol.wire.SurfaceMessage
import com.rextechnologies.flint.protocol.wire.VideoConfigMessage
import com.rextechnologies.flint.protocol.wire.VideoPacket
import com.rextechnologies.flint.protocol.wire.WireCodec
import com.rextechnologies.flint.protocol.wire.WireFormatException
import com.rextechnologies.flint.protocol.wire.WireFrame
import com.rextechnologies.flint.protocol.wire.WireMessage
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.Closeable
import java.io.IOException
import java.io.PushbackInputStream
import java.net.Inet4Address
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.util.concurrent.atomic.AtomicInteger
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/** What the receiver is doing right now, as the ten-foot UI needs to show it. */
sealed interface ReceiverState {
    data object Starting : ReceiverState
    data class Listening(val address: String, val port: Int) : ReceiverState
    data class Connected(val peerName: String, val parameters: CastSessionParameters) : ReceiverState
    data class Failed(val reason: String) : ReceiverState
}

/**
 * Session traffic the UI layer acts on.
 *
 * Discovery probes never reach here: a client that says HELLO and disconnects
 * is answered inside the connection loop and produces no events at all, so a
 * phone scanning the subnet cannot make the TV flicker between screens.
 */
interface ReceiverSessionListener {
    fun onMedia(command: MediaCommandMessage)
    fun onMediaData(chunk: MediaDataMessage)
    fun onSurface(surface: SurfaceMessage)
    fun onControl(control: ControlMessage)
    fun onVideoConfig(config: VideoConfigMessage)
    fun onVideoPacket(packet: VideoPacket)
    fun onAudioConfig(config: AudioConfigMessage)
    fun onAudioPacket(packet: AudioPacket)
    fun onSessionEnded()
}

/**
 * Accepts phone connections on the hotspot LAN.
 *
 * One session is served at a time. A second phone is refused with a BYE rather
 * than being queued, because silently taking over a TV somebody else is casting
 * to is worse than a clear refusal.
 */
class ReceiverServer(
    private val address: Inet4Address,
    private val listener: ReceiverSessionListener,
    requestedPort: Int = DEFAULT_PORT,
    private val pairingCodeProvider: () -> PairingCode,
    private val sessionToken: SessionToken = SessionToken.generate(),
    private val displayMetrics: () -> Triple<Int, Int, Int> = { Triple(1920, 1080, 320) },
    private val browserPortProvider: () -> Int = { 0 },
) : Closeable {
    var port: Int = requestedPort
        private set

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val activeSessions = AtomicInteger(0)
    private var server: ServerSocket? = null
    @Volatile
    private var sender: FrameSender? = null

    private val _state = MutableStateFlow<ReceiverState>(ReceiverState.Starting)
    val state: StateFlow<ReceiverState> = _state.asStateFlow()

    fun start(): Result<Unit> = runCatching {
        check(server == null) { "Receiver server is already running" }
        val candidate = ServerSocket()
        val bound = try {
            candidate.apply {
                reuseAddress = true
                bind(InetSocketAddress(address, port), BACKLOG)
            }
        } catch (failure: Throwable) {
            runCatching { candidate.close() }
            throw failure
        }
        port = bound.localPort
        server = bound
        _state.value = ReceiverState.Listening(address.hostAddress.orEmpty(), port)
        scope.launch {
            while (isActive) {
                val client = try {
                    bound.accept()
                } catch (_: IOException) {
                    break
                }
                // A failure serving one phone is that phone's session ending, not this
                // television's listener ending. This launch is a child of the accept loop, so
                // anything thrown here used to cancel the loop and leave the TV unreachable
                // until the service restarted.
                launch { runCatching { serve(client) } }
            }
        }
        Unit
    }.onFailure { _state.value = ReceiverState.Failed(it.message ?: it.javaClass.simpleName) }

    /** Queues a message back to the connected phone without blocking an Android UI callback. */
    fun send(message: WireMessage): Boolean {
        val activeSender = sender ?: return false
        scope.launch {
            if (!activeSender.send(message)) {
                Log.w(TAG, "Could not send receiver state because the session ended")
            }
        }
        return true
    }

    private fun serve(client: Socket) {
        client.use { socket ->
            socket.tcpNoDelay = true
            socket.keepAlive = true
            // Until the first byte arrives this is an unknown peer holding a thread, and anyone
            // on the network can open sockets and say nothing. An established cast session is the
            // opposite case and must never time out -- a still screen sends no frames for as long
            // as it stays still -- so the limit covers the wait for that first byte and is lifted
            // the moment it lands.
            socket.soTimeout = FIRST_BYTE_TIMEOUT_MILLIS
            val input = PushbackInputStream(BufferedInputStream(socket.getInputStream(), BUFFER_BYTES), 1)
            val output = BufferedOutputStream(socket.getOutputStream(), BUFFER_BYTES)
            val first = input.read()
            if (first < 0) return
            input.unread(first)
            if (first == 'R'.code) {
                // The probe keeps the limit: it is one line, and a peer that stops halfway
                // through it is not owed a thread.
                serveDiscovery(input, output)
                return
            }
            socket.soTimeout = 0
            val frames = FrameSender(output)
            val handshake = ReceiverHandshake(
                profile(),
                pairingCodeProvider(),
                sessionToken,
                browserPortProvider = browserPortProvider,
            )
            var established = false

            try {
                while (true) {
                    val frame = WireCodec.readFrom(input) ?: break
                    if (!established) {
                        when (val outcome = handshake.onMessage(frame.message)) {
                            is HandshakeOutcome.Continue -> frames.send(outcome.reply)
                            is HandshakeOutcome.Rejected -> {
                                frames.send(outcome.bye)
                                return
                            }
                            is HandshakeOutcome.Established -> {
                                if (activeSessions.compareAndSet(0, 1)) {
                                    // HELLO/negative replies deliberately use the oldest supported
                                    // envelope. Once both peers have negotiated, every normal cast
                                    // frame uses that exact version so a v1 sender never sees a v2
                                    // envelope it cannot decode.
                                    frames.useProtocolVersion(outcome.parameters.protocolVersion)
                                    outcome.reply?.let(frames::send)
                                    established = true
                                    sender = frames
                                    _state.value = ReceiverState.Connected(
                                        outcome.parameters.peer.deviceName,
                                        outcome.parameters,
                                    )
                                } else {
                                    frames.send(ByeMessage(ByeReason.PROTOCOL_ERROR, IN_USE))
                                    return
                                }
                            }
                        }
                    } else {
                        // Browser frames are valid v2 syntax, but they are never valid on this
                        // plaintext cast listener. A separate TLS endpoint owns that session.
                        if (BrowserWireRules.isForbiddenOnOrdinaryChannel(frame.message)) {
                            frames.send(ByeMessage(ByeReason.PROTOCOL_ERROR, BROWSER_ROUTE_REQUIRED))
                            return
                        }
                        if (!dispatch(frame.message)) return
                    }
                }
            } catch (_: WireFormatException) {
                runCatching { frames.send(ByeMessage(ByeReason.PROTOCOL_ERROR, "Malformed frame")) }
            } catch (_: IOException) {
                // A phone that walks out of range is a normal end of session.
            } finally {
                if (established) {
                    sender = null
                    listener.onSessionEnded()
                    _state.value = ReceiverState.Listening(address.hostAddress.orEmpty(), port)
                    activeSessions.set(0)
                }
            }
        }
    }

    /** Returns false when the session should end. */
    private fun dispatch(message: WireMessage): Boolean {
        when (message) {
            is MediaCommandMessage -> {
                Log.i(TAG, "Received media command")
                listener.onMedia(message)
            }
            is MediaDataMessage -> listener.onMediaData(message)
            is SurfaceMessage -> listener.onSurface(message)
            is ControlMessage -> listener.onControl(message)
            is VideoConfigMessage -> listener.onVideoConfig(message)
            is VideoPacket -> listener.onVideoPacket(message)
            is AudioConfigMessage -> listener.onAudioConfig(message)
            is AudioPacket -> listener.onAudioPacket(message)
            is ByeMessage -> return false
            // Anything else is either a future message type or traffic that
            // belongs to the phone side; ignoring it keeps old receivers usable.
            else -> Unit
        }
        return true
    }

    private fun serveDiscovery(input: PushbackInputStream, output: BufferedOutputStream) {
        // Bounded, and reading the stream rather than wrapping it in a reader: a reader would
        // consume past the line feed, and the budget is what stops an unauthenticated peer making
        // the television allocate.
        val line = ReceiverProbe.readLine(input, ReceiverProbe.MAX_REQUEST_BYTES).orEmpty()
        if (!ReceiverProbe.isRequest(line)) return
        // Assembled by ReceiverProbe rather than here. Replacing the three splitting characters
        // by hand, as this did, left out the one thing only ReceiverProbe knows: the byte budget
        // a model name has to fit in. A television whose model name overran it answered every
        // probe with a line the phone discards, and stayed undiscoverable while looking fine.
        output.write(ReceiverProbe.responseBytes(profile().deviceName, port))
        output.flush()
    }

    private fun profile(): DeviceProfile {
        val (width, height, density) = displayMetrics()
        return DeviceProfile(
            deviceName = Build.MODEL?.takeIf { it.isNotBlank() } ?: "Fire TV",
            codecCapabilities = setOf(CodecId.H264, CodecId.H265),
            screenWidth = width,
            screenHeight = height,
            densityDpi = density,
        )
    }

    override fun close() {
        runCatching { server?.close() }
        server = null
        sender = null
        scope.cancel()
    }

    private class FrameSender(private val output: BufferedOutputStream) {
        private val lock = Any()
        private var protocolVersion: Int = ProtocolVersion.MIN_SUPPORTED

        fun useProtocolVersion(value: Int) = synchronized(lock) {
            require(value in ProtocolVersion.MIN_SUPPORTED..ProtocolVersion.CURRENT) {
                "Unsupported negotiated protocol version: $value"
            }
            protocolVersion = value
        }

        fun send(message: WireMessage): Boolean = synchronized(lock) {
            try {
                WireCodec.writeTo(output, WireFrame(protocolVersion, message))
                output.flush()
                true
            } catch (_: IOException) {
                false
            }
        }
    }

    companion object {
        // One definition of the probe's wire format, in the module both ends share, rather than a
        // second copy here that can drift from it silently.
        const val DEFAULT_PORT = ReceiverProbe.PORT
        const val DISCOVERY_REQUEST = ReceiverProbe.REQUEST_LINE
        const val DISCOVERY_RESPONSE = ReceiverProbe.RESPONSE_PREFIX
        private const val BACKLOG = 8

        /** How long an unknown peer may hold a thread before saying anything at all. */
        private const val FIRST_BYTE_TIMEOUT_MILLIS = 5_000
        private const val BUFFER_BYTES = 64 * 1024
        private const val IN_USE = "This TV is already casting from another phone"
        private const val BROWSER_ROUTE_REQUIRED = "Browser traffic requires the secure browser session"
        private const val TAG = "FlintReceiver"

        /**
         * Every address on this TV that a phone on the same network could reach it at.
         *
         * The TV is a hotspot client rather than its host, so these are ordinary site-local Wi-Fi
         * or Ethernet addresses rather than a tether interface. Point-to-point interfaces are left
         * out, because that is the shape of a VPN tunnel — including this receiver's own browser
         * tunnel. Binding the cast listener inside a tunnel puts it somewhere the phone in the same
         * room cannot reach, and [BrowserVpnRoutePolicy] already draws the same line.
         */
        fun localAddresses(): List<Inet4Address> = runCatching {
            java.net.NetworkInterface.getNetworkInterfaces()
                .asSequence()
                .filter {
                    runCatching {
                        it.isUp && !it.isLoopback && !it.isVirtual && !it.isPointToPoint
                    }.getOrDefault(false)
                }
                .flatMap { it.inetAddresses.asSequence() }
                .filterIsInstance<Inet4Address>()
                .filter { it.isSiteLocalAddress && !it.isLoopbackAddress }
                .toList()
        }.getOrDefault(emptyList())

        /** The address to bind when nothing is bound yet. */
        fun findLocalAddress(): Inet4Address? = localAddresses().firstOrNull()

        /** Peer name for a HELLO that arrived before authentication. */
        fun peerName(hello: HelloMessage): String = hello.deviceName
    }
}

