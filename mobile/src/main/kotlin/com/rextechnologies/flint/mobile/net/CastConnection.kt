package com.rextechnologies.flint.mobile.net

import com.rextechnologies.flint.castcore.session.CastSessionMachine
import com.rextechnologies.flint.castcore.session.CastSessionPhase
import com.rextechnologies.flint.castcore.session.SessionEffect
import com.rextechnologies.flint.castcore.session.SessionFailure
import com.rextechnologies.flint.protocol.BinaryData
import com.rextechnologies.flint.protocol.http.SessionToken
import com.rextechnologies.flint.protocol.network.InterfaceSocketBinder
import com.rextechnologies.flint.protocol.session.CastSessionParameters
import com.rextechnologies.flint.protocol.session.DeviceProfile
import com.rextechnologies.flint.protocol.wire.AuthMethod
import com.rextechnologies.flint.protocol.wire.FrameWriter
import com.rextechnologies.flint.protocol.wire.WireCodec
import com.rextechnologies.flint.protocol.wire.WireMessage
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.Closeable
import java.io.OutputStream
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.ByteBuffer
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong

/** What the app hears from a live session. */
interface CastConnectionListener {
    fun onEstablished(parameters: CastSessionParameters, token: SessionToken?)

    /** Everything past the handshake: stats, playback state, and anything a future receiver sends. */
    fun onMessage(message: WireMessage)

    fun onClosed(failure: SessionFailure?)
}

/**
 * One socket, and the state machine that decides what goes down it.
 *
 * The split is deliberate and is the whole reason every refusal path in this product can be tested
 * without a network: `CastSessionMachine` holds the protocol and performs no I/O, and this class
 * holds the I/O and makes no decisions. It reads frames, hands them to the machine, and performs the
 * effects the machine returns.
 *
 * The socket is bound to the address the discovery ladder chose before it connects anywhere. That is
 * not belt and braces — on a phone tethering from mobile data, the process default network is the
 * cellular one, so a socket that binds nothing leaves over the wide-area network, connects to
 * nothing, and fails in a way that looks exactly like the television being switched off.
 */
class CastConnection(
    private val localAddress: Inet4Address,
    private val remoteAddress: String,
    private val remotePort: Int,
    private val profile: DeviceProfile,
    authMethod: AuthMethod,
    credential: BinaryData,
    private val listener: CastConnectionListener,
) : Closeable {
    private val machine = CastSessionMachine(profile, authMethod, credential)
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val closed = AtomicBoolean()

    /**
     * The frame writer, reused for the whole session.
     *
     * One per connection rather than one per frame: its header array is what keeps the steady-state
     * media path from allocating, and a fresh one each time would defeat the point of having it.
     */
    private val frameWriter = FrameWriter()

    /**
     * Written by the connect coroutine and read by whichever thread is sending.
     *
     * The encoder thread sends video, the control path sends everything else, and the connect
     * coroutine is what publishes both of these -- so neither is a safe plain `var`.
     */
    @Volatile
    private var socket: Socket? = null

    @Volatile
    private var output: OutputStream? = null

    /**
     * Bytes handed to the socket that the kernel has not yet taken.
     *
     * Sender-local, and the fourth field of the bitrate controller's `LinkSample`. The receiver
     * cannot see it, which is exactly why it is worth measuring here: a socket that is backing up is
     * the earliest signal the link is not keeping up.
     */
    private val pendingSendBytes = AtomicLong()

    val phase: CastSessionPhase
        get() = machine.phase

    val negotiatedVersion: Int
        get() = machine.negotiatedVersion

    fun connect() {
        check(socket == null) { "This connection has already been used" }
        scope.launch {
            val opened = runCatching { open() }.getOrElse { failure ->
                perform(machine.onTransportFailure(failure.message.orEmpty()))
                return@launch
            }
            perform(machine.start())
            readUntilClosed(opened)
        }
    }

    private fun open(): Socket {
        val binder = InterfaceSocketBinder(localAddress)
        val target = InetAddress.getByName(remoteAddress)
        val opened = binder.newOutgoingSocket().apply {
            tcpNoDelay = true
            keepAlive = true
            connect(InetSocketAddress(target, remotePort), CONNECT_TIMEOUT_MILLIS)
        }
        socket = opened
        output = BufferedOutputStream(opened.getOutputStream(), BUFFER_BYTES)
        return opened
    }

    private fun readUntilClosed(opened: Socket) {
        val input = BufferedInputStream(opened.getInputStream(), BUFFER_BYTES)
        while (scope.isActive && !closed.get()) {
            val frame = runCatching { WireCodec.readFrom(input) }.getOrElse { failure ->
                perform(machine.onTransportFailure(failure.message.orEmpty()))
                return
            }
            if (frame == null) {
                // A clean end of stream is still the socket going away without either end saying
                // anything, so it is reported as a transport failure rather than as a normal close.
                perform(machine.onTransportFailure(""))
                return
            }

            val wasReady = machine.phase == CastSessionPhase.READY
            perform(machine.onMessage(frame.message))
            if (wasReady && machine.phase == CastSessionPhase.READY) {
                listener.onMessage(frame.message)
            }
        }
    }

    /**
     * Sends a control-plane message.
     *
     * Everything that is not a media packet goes through here, framed at the negotiated version by
     * the machine so no caller has to remember the rule.
     */
    fun send(message: WireMessage): Boolean {
        val stream = output ?: return false
        return runCatching {
            synchronized(stream) {
                WireCodec.writeTo(stream, machine.frame(message))
                stream.flush()
            }
        }.isSuccess
    }

    /**
     * Sends one encoded video frame.
     *
     * The steady-state path, and the only one that must not allocate. The buffer belongs to the
     * encoder and is neither retained nor copied.
     */
    fun sendVideoPacket(presentationTimeUs: Long, keyFrame: Boolean, data: ByteBuffer): Boolean {
        val stream = output ?: return false
        val length = data.remaining()
        return runCatching {
            synchronized(stream) {
                pendingSendBytes.addAndGet(length.toLong())
                frameWriter.writeVideoPacket(
                    stream,
                    machine.negotiatedVersion,
                    presentationTimeUs,
                    keyFrame,
                    data,
                )
                stream.flush()
                pendingSendBytes.addAndGet(-length.toLong())
            }
        }.isSuccess
    }

    /** The same for audio, which carries no key-frame flag. */
    fun sendAudioPacket(presentationTimeUs: Long, data: ByteArray, offset: Int, length: Int): Boolean {
        val stream = output ?: return false
        return runCatching {
            synchronized(stream) {
                frameWriter.writeAudioPacket(
                    stream,
                    machine.negotiatedVersion,
                    presentationTimeUs,
                    data,
                    offset,
                    length,
                )
                stream.flush()
            }
        }.isSuccess
    }

    /** Bytes written but not yet acknowledged by the socket, for the bitrate controller. */
    fun pendingSendBytes(): Long = pendingSendBytes.get()

    /**
     * Ends the session, politely if the socket still allows it.
     *
     * The work happens on this connection's own dispatcher rather than on the caller's thread,
     * because the caller is the UI: somebody pressing Stop. `machine.stop()` produces a BYE, and a
     * BYE is a socket write, which on the main thread is a `NetworkOnMainThreadException` rather
     * than a goodbye. Tearing down first and writing afterwards would be worse still -- the message
     * would simply never leave, and the television would sit waiting for a peer that had gone.
     */
    override fun close() {
        if (!closed.compareAndSet(false, true)) return
        scope.launch {
            perform(machine.stop())
            teardown()
        }
    }

    private fun perform(effects: List<SessionEffect>) {
        effects.forEach { effect ->
            when (effect) {
                is SessionEffect.SendFrame -> {
                    val stream = output ?: return@forEach
                    runCatching {
                        synchronized(stream) {
                            WireCodec.writeTo(stream, effect.frame)
                            stream.flush()
                        }
                    }
                }

                is SessionEffect.Established ->
                    listener.onEstablished(effect.parameters, effect.token)

                is SessionEffect.Close -> {
                    listener.onClosed(effect.failure)
                    closed.set(true)
                    teardown()
                }
            }
        }
    }

    /**
     * Releases the socket, and then this connection's dispatcher.
     *
     * The order matters: cancelling the scope first would leave whatever is still being written
     * half-sent, and a cancelled scope cannot be used to finish the job. A `CastConnection` is
     * single-use by construction -- [connect] refuses a second call -- so cancelling for good here
     * costs nothing that could be wanted later.
     */
    private fun teardown() {
        runCatching { output?.flush() }
        runCatching { socket?.close() }
        output = null
        socket = null
        scope.cancel()
    }

    /** The screen dimensions this phone advertised, which the receiver sizes its surface from. */
    val advertisedProfile: DeviceProfile
        get() = profile

    private companion object {
        const val CONNECT_TIMEOUT_MILLIS = 4_000

        /** The same 64 KiB the receiver uses on its side of the socket. */
        const val BUFFER_BYTES = 64 * 1024
    }
}
