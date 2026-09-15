package com.rextechnologies.flint.protocol.session

import com.rextechnologies.flint.protocol.BinaryData
import com.rextechnologies.flint.protocol.discovery.PairingCode
import com.rextechnologies.flint.protocol.http.SessionToken
import com.rextechnologies.flint.protocol.wire.AuthMessage
import com.rextechnologies.flint.protocol.wire.AuthMethod
import com.rextechnologies.flint.protocol.wire.ByeMessage
import com.rextechnologies.flint.protocol.wire.ByeReason
import com.rextechnologies.flint.protocol.wire.CodecId
import com.rextechnologies.flint.protocol.wire.HelloMessage
import com.rextechnologies.flint.protocol.wire.ProtocolVersion
import com.rextechnologies.flint.protocol.wire.StatsMessage
import com.rextechnologies.flint.protocol.wire.WireMessage
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class CastHandshakeTest {
    private val code = PairingCode.parse("204815")
    private val token = SessionToken.generate()

    private fun profile(
        name: String,
        codecs: Set<CodecId> = setOf(CodecId.H264, CodecId.H265),
    ) = DeviceProfile(name, codecs, 1920, 1080, 320)

    private fun receiver(
        codecs: Set<CodecId> = setOf(CodecId.H264, CodecId.H265),
        trusted: Set<String> = emptySet(),
    ) = ReceiverHandshake(profile("Living room TV", codecs), code, token, trusted)

    private fun sender(
        method: AuthMethod = AuthMethod.PAIRING_CODE,
        credential: BinaryData = CastAuth.pairingCredential(code),
        fingerprint: String? = null,
        codecs: Set<CodecId> = setOf(CodecId.H264, CodecId.H265),
    ) = SenderHandshake(profile("Phone", codecs), method, credential, fingerprint)

    /** Drives both halves to completion and returns what each side ended up with. */
    private fun run(
        sender: SenderHandshake,
        receiver: ReceiverHandshake,
    ): Pair<HandshakeOutcome, HandshakeOutcome> {
        var fromSender: WireMessage? = sender.start()
        var senderOutcome: HandshakeOutcome? = null
        var receiverOutcome: HandshakeOutcome? = null

        while (fromSender != null) {
            val outcome = receiver.onMessage(fromSender)
            receiverOutcome = outcome
            val reply = when (outcome) {
                is HandshakeOutcome.Continue -> outcome.reply
                is HandshakeOutcome.Established -> outcome.reply
                is HandshakeOutcome.Rejected -> outcome.bye
            }
            if (reply == null) break
            val next = sender.onMessage(reply)
            senderOutcome = next
            fromSender = when (next) {
                is HandshakeOutcome.Continue -> next.reply
                else -> null
            }
        }
        return assertNotNull(senderOutcome) to assertNotNull(receiverOutcome)
    }

    @Test
    fun `a valid pairing code establishes a session on both sides`() {
        val receiver = receiver()
        val sender = sender()

        val (senderOutcome, receiverOutcome) = run(sender, receiver)

        val established = assertIs<HandshakeOutcome.Established>(receiverOutcome)
        assertEquals("Phone", established.parameters.peer.deviceName)
        assertEquals(CodecId.H265, established.parameters.videoCodec)
        // The highest version both ends support, not a literal. Written against the constant so a
        // protocol bump does not leave this asserting an old number that nothing else believes —
        // which is exactly how it came to expect 1 after the browser work moved CURRENT to 2.
        assertEquals(ProtocolVersion.CURRENT, established.parameters.protocolVersion)
        assertTrue(receiver.established)
        assertIs<HandshakeOutcome.Established>(senderOutcome)
        assertEquals(token, sender.grantedToken)
    }

    @Test
    fun `the session token is accepted on a later reconnection`() {
        val (_, receiverOutcome) = run(
            sender(AuthMethod.SESSION_TOKEN, CastAuth.tokenCredential(token)),
            receiver(),
        )

        assertIs<HandshakeOutcome.Established>(receiverOutcome)
    }

    @Test
    fun `a public-key proof needs both a trusted fingerprint and the token`() {
        val trusted = receiver(trusted = setOf("aa:bb"))

        val (_, accepted) = run(
            sender(AuthMethod.PUBLIC_KEY_PROOF, CastAuth.tokenCredential(token), "aa:bb"),
            trusted,
        )
        assertIs<HandshakeOutcome.Established>(accepted)

        val (_, refused) = run(
            sender(AuthMethod.PUBLIC_KEY_PROOF, CastAuth.tokenCredential(token), "zz:zz"),
            receiver(trusted = setOf("aa:bb")),
        )
        assertIs<HandshakeOutcome.Rejected>(refused)
    }

    @Test
    fun `a wrong pairing code is refused without saying why`() {
        val handshake = receiver()
        handshake.onMessage(profile("Phone").hello())

        val outcome = handshake.onMessage(
            AuthMessage(AuthMethod.PAIRING_CODE, CastAuth.pairingCredential(PairingCode.parse("000000"))),
        )

        val rejected = assertIs<HandshakeOutcome.Rejected>(outcome)
        assertEquals(ByeReason.AUTHENTICATION_FAILED, rejected.bye.reason)
        assertEquals("Authentication failed", rejected.bye.detail)
        assertFalse(handshake.established)
    }

    @Test
    fun `a non-ascii credential is refused with the same message as a wrong code`() {
        val handshake = receiver()
        handshake.onMessage(profile("Phone").hello())

        val outcome = handshake.onMessage(
            AuthMessage(AuthMethod.PAIRING_CODE, BinaryData.of(byteArrayOf(0x01, 0x02))),
        )

        assertEquals("Authentication failed", assertIs<HandshakeOutcome.Rejected>(outcome).bye.detail)
    }

    @Test
    fun `no shared codec is refused on both sides`() {
        val handshake = receiver(codecs = setOf(CodecId.H265))
        handshake.onMessage(profile("Phone", setOf(CodecId.H264)).hello())

        val outcome = handshake.onMessage(
            AuthMessage(AuthMethod.PAIRING_CODE, CastAuth.pairingCredential(code)),
        )

        assertEquals(ByeReason.PROTOCOL_ERROR, assertIs<HandshakeOutcome.Rejected>(outcome).bye.reason)

        val senderSide = sender(codecs = setOf(CodecId.H264))
        senderSide.start()
        val senderOutcome = senderSide.onMessage(profile("TV", setOf(CodecId.H265)).hello())
        assertEquals(
            ByeReason.PROTOCOL_ERROR,
            assertIs<HandshakeOutcome.Rejected>(senderOutcome).bye.reason,
        )
    }

    @Test
    fun `H264 is chosen when the peer cannot decode HEVC`() {
        val handshake = receiver()
        handshake.onMessage(profile("Phone", setOf(CodecId.H264)).hello())

        val outcome = handshake.onMessage(
            AuthMessage(AuthMethod.PAIRING_CODE, CastAuth.pairingCredential(code)),
        )

        assertEquals(CodecId.H264, assertIs<HandshakeOutcome.Established>(outcome).parameters.videoCodec)
    }

    @Test
    fun `an incompatible protocol version is refused on both sides`() {
        val future = HelloMessage(9, 9, "Future", setOf(CodecId.H264), 100, 100, 160)

        assertEquals(
            ByeReason.UNSUPPORTED_VERSION,
            assertIs<HandshakeOutcome.Rejected>(receiver().onMessage(future)).bye.reason,
        )

        val senderSide = sender()
        senderSide.start()
        assertEquals(
            ByeReason.UNSUPPORTED_VERSION,
            assertIs<HandshakeOutcome.Rejected>(senderSide.onMessage(future)).bye.reason,
        )
    }

    @Test
    fun `out-of-order and duplicate messages are protocol errors`() {
        val handshake = receiver()

        assertEquals(
            ByeReason.PROTOCOL_ERROR,
            assertIs<HandshakeOutcome.Rejected>(
                handshake.onMessage(AuthMessage(AuthMethod.PAIRING_CODE, CastAuth.pairingCredential(code))),
            ).bye.reason,
        )

        val second = receiver()
        second.onMessage(profile("Phone").hello())
        assertEquals(
            ByeReason.PROTOCOL_ERROR,
            assertIs<HandshakeOutcome.Rejected>(second.onMessage(profile("Phone").hello())).bye.reason,
        )

        assertEquals(
            ByeReason.PROTOCOL_ERROR,
            assertIs<HandshakeOutcome.Rejected>(receiver().onMessage(StatsMessage(0, 0, 0, 0))).bye.reason,
        )
    }

    @Test
    fun `the sender surfaces a receiver refusal and rejects unexpected traffic`() {
        val senderSide = sender()
        senderSide.start()

        val bye = ByeMessage(ByeReason.RECEIVER_STOPPED, "shutting down")
        assertEquals(bye, assertIs<HandshakeOutcome.Rejected>(senderSide.onMessage(bye)).bye)

        assertEquals(
            ByeReason.PROTOCOL_ERROR,
            assertIs<HandshakeOutcome.Rejected>(sender().onMessage(StatsMessage(0, 0, 0, 0))).bye.reason,
        )
        assertEquals(
            ByeReason.PROTOCOL_ERROR,
            assertIs<HandshakeOutcome.Rejected>(
                sender().onMessage(AuthMessage(AuthMethod.SESSION_TOKEN, CastAuth.tokenCredential(token))),
            ).bye.reason,
        )
    }

    @Test
    fun `the sender tolerates a receiver that grants no usable token`() {
        val senderSide = sender()
        senderSide.start()
        senderSide.onMessage(profile("TV").hello())

        val outcome = senderSide.onMessage(
            AuthMessage(AuthMethod.SESSION_TOKEN, BinaryData.of("short".toByteArray())),
        )

        assertIs<HandshakeOutcome.Established>(outcome)
        assertNull(senderSide.grantedToken)
    }

    @Test
    fun `start may only be called once`() {
        val senderSide = sender()
        senderSide.start()

        kotlin.test.assertFailsWith<IllegalStateException> { senderSide.start() }
    }

    @Test
    fun `credential encoding round-trips and rejects oversized input`() {
        assertEquals("204815", CastAuth.asAscii(CastAuth.pairingCredential(code)))
        assertEquals(token.toString(), CastAuth.asAscii(CastAuth.tokenCredential(token)))
        assertNull(CastAuth.asAscii(BinaryData.EMPTY))
        assertNull(CastAuth.asAscii(BinaryData.of(ByteArray(4_096) { 0x41 })))
    }

    @Test
    fun `codec preference puts HEVC ahead of H264`() {
        assertEquals(listOf(CodecId.H265, CodecId.H264), CastAuth.VIDEO_PREFERENCE)
        assertNull(CastAuth.selectVideoCodec(setOf(CodecId.H264), setOf(CodecId.H265)))
    }

    @Test
    fun `a device profile builds a hello that carries its own capabilities`() {
        val hello = profile("Phone", setOf(CodecId.H264)).hello()

        assertEquals("Phone", hello.deviceName)
        assertEquals(setOf(CodecId.H264), hello.codecCapabilities)
        assertEquals(1920, hello.screenWidth)
        assertEquals(1080, hello.screenHeight)
        assertEquals(320, hello.densityDpi)
    }
}
