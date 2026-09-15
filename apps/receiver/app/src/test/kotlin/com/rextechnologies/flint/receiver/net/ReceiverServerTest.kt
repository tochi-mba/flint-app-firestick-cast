package com.rextechnologies.flint.receiver.net

import com.rextechnologies.flint.protocol.BinaryData
import com.rextechnologies.flint.protocol.discovery.PairingCode
import com.rextechnologies.flint.protocol.session.CastAuth
import com.rextechnologies.flint.protocol.session.DeviceProfile
import com.rextechnologies.flint.protocol.session.HandshakeOutcome
import com.rextechnologies.flint.protocol.session.SenderHandshake
import com.rextechnologies.flint.protocol.wire.AudioConfigMessage
import com.rextechnologies.flint.protocol.wire.AudioPacket
import com.rextechnologies.flint.protocol.wire.AuthMessage
import com.rextechnologies.flint.protocol.wire.AuthMethod
import com.rextechnologies.flint.protocol.wire.BrowserCommandAction
import com.rextechnologies.flint.protocol.wire.BrowserCommandMessage
import com.rextechnologies.flint.protocol.wire.ByeMessage
import com.rextechnologies.flint.protocol.wire.CodecId
import com.rextechnologies.flint.protocol.wire.ControlMessage
import com.rextechnologies.flint.protocol.wire.MediaAction
import com.rextechnologies.flint.protocol.wire.MediaCommandMessage
import com.rextechnologies.flint.protocol.wire.PlaybackState
import com.rextechnologies.flint.protocol.wire.PlaybackStateMessage
import com.rextechnologies.flint.protocol.wire.ProtocolVersion
import com.rextechnologies.flint.protocol.wire.SurfaceMessage
import com.rextechnologies.flint.protocol.wire.SurfaceMode
import com.rextechnologies.flint.protocol.wire.TransportAction
import com.rextechnologies.flint.protocol.wire.TransportControl
import com.rextechnologies.flint.protocol.wire.VideoConfigMessage
import com.rextechnologies.flint.protocol.wire.VideoPacket
import com.rextechnologies.flint.protocol.wire.WireCodec
import com.rextechnologies.flint.protocol.wire.WireFrame
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import java.net.Inet4Address
import java.net.InetSocketAddress
import java.net.Socket
import java.util.Collections
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import kotlin.test.Test
import kotlin.test.assertContains
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertTrue

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35])
class ReceiverServerTest {
    private val loopback = java.net.InetAddress.getByName("127.0.0.1") as Inet4Address
    private val code = PairingCode.parse("123456")

    @Test
    fun `text probe remains separate from authenticated session traffic`() {
        ReceiverServer(loopback, RecordingListener(), 0, { code }).use { server ->
            assertTrue(server.start().isSuccess)
            Socket().use { socket ->
                socket.connect(InetSocketAddress(loopback, server.port), 2_000)
                socket.soTimeout = 2_000
                socket.getOutputStream().write("REXCAST DISCOVER/1\n".toByteArray(Charsets.US_ASCII))
                socket.getOutputStream().flush()
                val response = socket.getInputStream().bufferedReader().readLine()
                assertContains(response, "REXCAST RECEIVER/1")
                assertTrue(response.endsWith("\t${server.port}"))
            }
        }
    }

    @Test
    fun `paired client can deliver every live session message and end cleanly`() {
        val listener = RecordingListener(expectedMessages = 7)
        ReceiverServer(loopback, listener, 0, { code }).use { server ->
            assertTrue(server.start().isSuccess)
            Socket().use { socket ->
                socket.connect(InetSocketAddress(loopback, server.port), 2_000)
                socket.soTimeout = 2_000
                val input = socket.getInputStream().buffered()
                val output = socket.getOutputStream().buffered()
                establish(input, output)

                write(output, SurfaceMessage(SurfaceMode.MIRROR, "Phone screen"))
                write(output, MediaCommandMessage(MediaAction.LOAD, "http://127.0.0.1/item", "Clip", "video/mp4"))
                write(output, ControlMessage(1, TransportControl(TransportAction.PAUSE)))
                write(output, VideoConfigMessage(CodecId.H264, 1280, 720, emptyList()))
                write(output, VideoPacket(10, true, BinaryData.of(byteArrayOf(1, 2))))
                write(output, AudioConfigMessage(CodecId.AAC_LC, 48_000, 2))
                write(output, AudioPacket(12, BinaryData.of(byteArrayOf(3, 4))))
                assertTrue(listener.messages.await(2, TimeUnit.SECONDS))
                write(output, ByeMessage(com.rextechnologies.flint.protocol.wire.ByeReason.NORMAL, "done"))
                assertTrue(listener.ended.await(2, TimeUnit.SECONDS))
            }

            assertEquals(1, listener.surfaces.size)
            assertEquals(1, listener.media.size)
            assertEquals(1, listener.controls.size)
            assertEquals(1, listener.videoConfigs.size)
            assertEquals(1, listener.videoPackets.size)
            assertEquals(1, listener.audioConfigs.size)
            assertEquals(1, listener.audioPackets.size)
        }
    }

    @Test
    fun `wrong pairing code is rejected before listener sees traffic`() {
        val listener = RecordingListener()
        ReceiverServer(loopback, listener, 0, { code }).use { server ->
            assertTrue(server.start().isSuccess)
            Socket().use { socket ->
                socket.connect(InetSocketAddress(loopback, server.port), 2_000)
                socket.soTimeout = 2_000
                val input = socket.getInputStream().buffered()
                val output = socket.getOutputStream().buffered()
                write(output, profile().hello())
                assertIs<com.rextechnologies.flint.protocol.wire.HelloMessage>(WireCodec.readFrom(input)?.message)
                write(
                    output,
                    AuthMessage(AuthMethod.PAIRING_CODE, CastAuth.pairingCredential(PairingCode.parse("654321"))),
                )
                assertIs<ByeMessage>(WireCodec.readFrom(input)?.message)
            }
            assertTrue(listener.surfaces.isEmpty())
        }
    }

    @Test
    fun `receiver state is sent asynchronously to an established client`() {
        ReceiverServer(loopback, RecordingListener(), 0, { code }).use { server ->
            assertTrue(server.start().isSuccess)
            Socket().use { socket ->
                socket.connect(InetSocketAddress(loopback, server.port), 2_000)
                socket.soTimeout = 2_000
                val input = socket.getInputStream().buffered()
                val output = socket.getOutputStream().buffered()
                establish(input, output)
                awaitConnectedClient(server)

                assertTrue(server.send(PlaybackStateMessage(PlaybackState.PLAYING, detail = "Playing on this TV")))

                val state = assertIs<PlaybackStateMessage>(WireCodec.readFrom(input)?.message)
                assertEquals(PlaybackState.PLAYING, state.state)
            }
        }
    }

    @Test
    fun `receiver emits the negotiated wire version after its v1 compatible hello`() {
        ReceiverServer(loopback, RecordingListener(), 0, { code }).use { server ->
            assertTrue(server.start().isSuccess)
            Socket().use { socket ->
                socket.connect(InetSocketAddress(loopback, server.port), 2_000)
                socket.soTimeout = 2_000
                val input = socket.getInputStream().buffered()
                val output = socket.getOutputStream().buffered()
                val legacyProfile = profile().hello(
                    minimumVersion = ProtocolVersion.MIN_SUPPORTED,
                    maximumVersion = ProtocolVersion.MIN_SUPPORTED,
                )

                write(output, legacyProfile, ProtocolVersion.MIN_SUPPORTED)
                val hello = requireNotNull(WireCodec.readFrom(input))
                assertEquals(ProtocolVersion.MIN_SUPPORTED, hello.protocolVersion)

                write(
                    output,
                    AuthMessage(AuthMethod.PAIRING_CODE, CastAuth.pairingCredential(code)),
                    ProtocolVersion.MIN_SUPPORTED,
                )
                val granted = requireNotNull(WireCodec.readFrom(input))
                assertEquals(ProtocolVersion.MIN_SUPPORTED, granted.protocolVersion)
                assertIs<AuthMessage>(granted.message)
            }
        }
    }

    @Test
    fun `ordinary receiver rejects a browser frame before service dispatch`() {
        val listener = RecordingListener()
        ReceiverServer(loopback, listener, 0, { code }).use { server ->
            assertTrue(server.start().isSuccess)
            Socket().use { socket ->
                socket.connect(InetSocketAddress(loopback, server.port), 2_000)
                socket.soTimeout = 2_000
                val input = socket.getInputStream().buffered()
                val output = socket.getOutputStream().buffered()
                establish(input, output)

                write(output, BrowserCommandMessage(1, 1, BrowserCommandAction.CLOSE))
                val rejection = assertIs<ByeMessage>(WireCodec.readFrom(input)?.message)
                assertEquals(com.rextechnologies.flint.protocol.wire.ByeReason.PROTOCOL_ERROR, rejection.reason)
                assertTrue(listener.surfaces.isEmpty())
                assertTrue(listener.controls.isEmpty())
            }
        }
    }

    private fun establish(input: java.io.InputStream, output: java.io.OutputStream) {
        val handshake = SenderHandshake(
            profile(),
            AuthMethod.PAIRING_CODE,
            CastAuth.pairingCredential(code),
        )
        write(output, handshake.start())
        val hello = requireNotNull(WireCodec.readFrom(input)).message
        val auth = assertIs<HandshakeOutcome.Continue>(handshake.onMessage(hello)).reply
        write(output, auth)
        val granted = requireNotNull(WireCodec.readFrom(input)).message
        assertIs<HandshakeOutcome.Established>(handshake.onMessage(granted))
    }

    // The client reaches Established the moment it reads the grant, but the server publishes the
    // sender it sends through a few instructions later. Waiting on the server's own signal keeps
    // this test off that window; without it the send races the handshake and intermittently fails.
    private fun awaitConnectedClient(server: ReceiverServer) {
        val deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(2)
        while (System.nanoTime() < deadline) {
            if (server.state.value is ReceiverState.Connected) {
                return
            }
            Thread.sleep(5)
        }
        throw AssertionError("The server never reported a connected client")
    }

    private fun profile() = DeviceProfile("Test phone", setOf(CodecId.H264), 1080, 1920, 420)

    private fun write(output: java.io.OutputStream, message: com.rextechnologies.flint.protocol.wire.WireMessage) {
        write(output, message, ProtocolVersion.CURRENT)
    }

    private fun write(
        output: java.io.OutputStream,
        message: com.rextechnologies.flint.protocol.wire.WireMessage,
        version: Int,
    ) {
        WireCodec.writeTo(output, WireFrame(version, message))
        output.flush()
    }

    private class RecordingListener(expectedMessages: Int = 0) : ReceiverSessionListener {
        val messages = CountDownLatch(expectedMessages)
        val ended = CountDownLatch(1)
        val media = Collections.synchronizedList(mutableListOf<MediaCommandMessage>())
        val mediaData = Collections.synchronizedList(
            mutableListOf<com.rextechnologies.flint.protocol.wire.MediaDataMessage>(),
        )
        val surfaces = Collections.synchronizedList(mutableListOf<SurfaceMessage>())
        val controls = Collections.synchronizedList(mutableListOf<ControlMessage>())
        val videoConfigs = Collections.synchronizedList(mutableListOf<VideoConfigMessage>())
        val videoPackets = Collections.synchronizedList(mutableListOf<VideoPacket>())
        val audioConfigs = Collections.synchronizedList(mutableListOf<AudioConfigMessage>())
        val audioPackets = Collections.synchronizedList(mutableListOf<AudioPacket>())

        override fun onMedia(command: MediaCommandMessage) {
            media += command
            messages.countDown()
        }
        override fun onMediaData(chunk: com.rextechnologies.flint.protocol.wire.MediaDataMessage) {
            mediaData += chunk
            messages.countDown()
        }
        override fun onSurface(surface: SurfaceMessage) {
            surfaces += surface
            messages.countDown()
        }
        override fun onControl(control: ControlMessage) {
            controls += control
            messages.countDown()
        }
        override fun onVideoConfig(config: VideoConfigMessage) {
            videoConfigs += config
            messages.countDown()
        }
        override fun onVideoPacket(packet: VideoPacket) {
            videoPackets += packet
            messages.countDown()
        }
        override fun onAudioConfig(config: AudioConfigMessage) {
            audioConfigs += config
            messages.countDown()
        }
        override fun onAudioPacket(packet: AudioPacket) {
            audioPackets += packet
            messages.countDown()
        }
        override fun onSessionEnded() {
            ended.countDown()
        }
    }
}
