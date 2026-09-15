package com.rextechnologies.flint.castcore.session

import com.rextechnologies.flint.protocol.BinaryData
import com.rextechnologies.flint.protocol.discovery.PairingCode
import com.rextechnologies.flint.protocol.http.SessionToken
import com.rextechnologies.flint.protocol.session.CastAuth
import com.rextechnologies.flint.protocol.session.DeviceProfile
import com.rextechnologies.flint.protocol.session.HandshakeOutcome
import com.rextechnologies.flint.protocol.session.ReceiverHandshake
import com.rextechnologies.flint.protocol.wire.AuthMessage
import com.rextechnologies.flint.protocol.wire.AuthMethod
import com.rextechnologies.flint.protocol.wire.ByeMessage
import com.rextechnologies.flint.protocol.wire.ByeReason
import com.rextechnologies.flint.protocol.wire.CodecId
import com.rextechnologies.flint.protocol.wire.HelloMessage
import com.rextechnologies.flint.protocol.wire.ProtocolVersion
import com.rextechnologies.flint.protocol.wire.StatsMessage
import com.rextechnologies.flint.protocol.wire.SurfaceMessage
import com.rextechnologies.flint.protocol.wire.SurfaceMode
import com.rextechnologies.flint.protocol.wire.WireMessage
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertIs
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

private val phoneProfile = DeviceProfile(
    deviceName = "Pixel",
    codecCapabilities = setOf(CodecId.H264, CodecId.H265),
    screenWidth = 1080,
    screenHeight = 2400,
    densityDpi = 420,
)

private val televisionProfile = DeviceProfile(
    deviceName = "Fire TV Stick",
    codecCapabilities = setOf(CodecId.H264, CodecId.H265),
    screenWidth = 1920,
    screenHeight = 1080,
    densityDpi = 320,
)

private val pairingCode = PairingCode.parse("004283")
private val grantedToken = SessionToken.generate()

private fun machine(code: PairingCode = pairingCode) = CastSessionMachine(
    profile = phoneProfile,
    authMethod = AuthMethod.PAIRING_CODE,
    credential = CastAuth.pairingCredential(code),
)

private fun receiver(browserPort: Int = 0) = ReceiverHandshake(
    profile = televisionProfile,
    pairingCode = pairingCode,
    sessionToken = grantedToken,
    browserPortProvider = { browserPort },
)

private fun sentMessages(effects: List<SessionEffect>): List<WireMessage> =
    effects.filterIsInstance<SessionEffect.SendFrame>().map { it.frame.message }

class CastSessionMachineTest {
    @Test
    fun `hello rides the oldest envelope even though it advertises the newest version`() {
        // A receiver too old to parse a newer envelope still has to be able to read the first
        // exchange in order to say so.
        val effects = machine().start()
        val frame = assertIs<SessionEffect.SendFrame>(effects.single()).frame
        assertEquals(ProtocolVersion.MIN_SUPPORTED, frame.protocolVersion)

        val hello = assertIs<HelloMessage>(frame.message)
        assertEquals(ProtocolVersion.MIN_SUPPORTED, hello.minimumVersion)
        assertEquals(ProtocolVersion.CURRENT, hello.maximumVersion)
    }

    @Test
    fun `a session cannot be started twice`() {
        val session = machine()
        session.start()
        assertFailsWith<IllegalStateException> { session.start() }
    }

    @Test
    fun `a full exchange against the real receiver half reaches ready and keeps the token`() {
        val session = machine()
        val television = receiver(browserPort = 8443)

        session.start()
        val televisionHello = assertIs<HandshakeOutcome.Continue>(
            television.onMessage(phoneProfile.hello()),
        ).reply

        val authEffects = session.onMessage(televisionHello)
        assertEquals(CastSessionPhase.AWAITING_AUTH, session.phase)
        val auth = assertIs<AuthMessage>(sentMessages(authEffects).single())

        val established = assertIs<HandshakeOutcome.Established>(television.onMessage(auth))
        val readyEffects = session.onMessage(assertNotNull(established.reply))

        assertEquals(CastSessionPhase.READY, session.phase)
        val ready = assertIs<SessionEffect.Established>(readyEffects.single())
        assertEquals(ProtocolVersion.CURRENT, ready.parameters.protocolVersion)
        assertEquals(grantedToken, ready.token)
        assertEquals(8443, ready.browserTlsPort)
        assertEquals(CodecId.H265, ready.parameters.videoCodec)
    }

    @Test
    fun `auth rides the negotiated version rather than this build's ceiling`() {
        val session = machine()
        session.start()
        val olderTelevision = HelloMessage(
            minimumVersion = 1,
            maximumVersion = 2,
            deviceName = "Older Fire TV",
            codecCapabilities = setOf(CodecId.H264),
            screenWidth = 1920,
            screenHeight = 1080,
            densityDpi = 320,
        )
        val effects = session.onMessage(olderTelevision)
        val frame = assertIs<SessionEffect.SendFrame>(effects.single()).frame

        assertEquals(2, session.negotiatedVersion)
        assertEquals(2, frame.protocolVersion)
        assertTrue(frame.protocolVersion != ProtocolVersion.CURRENT)
        assertTrue(frame.protocolVersion != ProtocolVersion.MIN_SUPPORTED)
        assertEquals(2, session.frame(SurfaceMessage(SurfaceMode.PRESENTATION)).protocolVersion)
    }

    @Test
    fun `a browser port that is not a port is ignored rather than carried`() {
        val session = machine()
        val television = receiver(browserPort = 0)
        session.start()
        val televisionHello = assertIs<HandshakeOutcome.Continue>(
            television.onMessage(phoneProfile.hello()),
        ).reply
        val auth = assertIs<AuthMessage>(sentMessages(session.onMessage(televisionHello)).single())
        val established = assertIs<HandshakeOutcome.Established>(television.onMessage(auth))
        session.onMessage(assertNotNull(established.reply))
        assertNull(session.browserTlsPort)
    }

    @Test
    fun `a refusal after authentication looked successful is the busy case, not a protocol fault`() {
        // The receiver admits one phone at a time and refuses the second only once it has answered
        // the HELLO and validated the credential, so this is the ordinary shape of somebody else
        // already watching.
        val session = machine()
        val television = receiver()
        session.start()
        val televisionHello = assertIs<HandshakeOutcome.Continue>(
            television.onMessage(phoneProfile.hello()),
        ).reply
        session.onMessage(televisionHello)

        val effects = session.onMessage(
            ByeMessage(ByeReason.PROTOCOL_ERROR, ByeCopy.RECEIVER_BUSY_DETAIL),
        )
        val close = assertIs<SessionEffect.Close>(effects.single())
        assertEquals(SessionFailureKind.RECEIVER_BUSY, assertNotNull(close.failure).kind)
        assertEquals(CastSessionPhase.ENDED, session.phase)
    }

    @Test
    fun `a bye ends the session from any phase`() {
        listOf<(CastSessionMachine) -> Unit>(
            { },
            { it.start() },
        ).forEach { prepare ->
            val session = machine()
            prepare(session)
            val effects = session.onMessage(ByeMessage(ByeReason.RECEIVER_STOPPED))
            assertIs<SessionEffect.Close>(effects.single())
            assertEquals(CastSessionPhase.ENDED, session.phase)
        }
    }

    @Test
    fun `the television speaking before this phone did is refused`() {
        val session = machine()
        val close = assertIs<SessionEffect.Close>(session.onMessage(StatsMessage(0, 0, 0, 0)).single())
        assertEquals(SessionFailureKind.PROTOCOL, assertNotNull(close.failure).kind)
    }

    @Test
    fun `a greeting that is not a greeting is refused`() {
        val session = machine()
        session.start()
        val close = assertIs<SessionEffect.Close>(session.onMessage(StatsMessage(0, 0, 0, 0)).single())
        assertTrue(assertNotNull(close.failure).sentence.contains("other than its own greeting"))
    }

    @Test
    fun `an authentication reply that is not one is refused`() {
        val session = machine()
        session.start()
        session.onMessage(televisionProfile.hello())
        val close = assertIs<SessionEffect.Close>(session.onMessage(StatsMessage(0, 0, 0, 0)).single())
        assertTrue(
            assertNotNull(close.failure).sentence.contains("other than an authentication reply"),
        )
    }

    @Test
    fun `a television with no version in common is reported as a version problem`() {
        val session = machine()
        session.start()
        val fromTheFuture = HelloMessage(
            minimumVersion = 900,
            maximumVersion = 999,
            deviceName = "Future TV",
            codecCapabilities = setOf(CodecId.H264),
            screenWidth = 1920,
            screenHeight = 1080,
            densityDpi = 320,
        )
        val close = assertIs<SessionEffect.Close>(session.onMessage(fromTheFuture).single())
        assertEquals(SessionFailureKind.VERSION, assertNotNull(close.failure).kind)
    }

    @Test
    fun `a television with no codec in common is reported rather than attempted`() {
        val session = machine()
        session.start()
        val noCommonCodec = HelloMessage(
            minimumVersion = 1,
            maximumVersion = 4,
            deviceName = "Odd TV",
            codecCapabilities = setOf(CodecId.AV1),
            screenWidth = 1920,
            screenHeight = 1080,
            densityDpi = 320,
        )
        val close = assertIs<SessionEffect.Close>(session.onMessage(noCommonCodec).single())
        assertEquals(SessionFailureKind.PROTOCOL, assertNotNull(close.failure).kind)
    }

    @Test
    fun `an authentication refusal comes back as one`() {
        val session = CastSessionMachine(
            phoneProfile,
            AuthMethod.PAIRING_CODE,
            CastAuth.pairingCredential(PairingCode.parse("999999")),
        )
        val television = receiver()
        session.start()
        val televisionHello = assertIs<HandshakeOutcome.Continue>(
            television.onMessage(phoneProfile.hello()),
        ).reply
        val auth = assertIs<AuthMessage>(sentMessages(session.onMessage(televisionHello)).single())
        val rejected = assertIs<HandshakeOutcome.Rejected>(television.onMessage(auth))

        val close = assertIs<SessionEffect.Close>(session.onMessage(rejected.bye).single())
        assertEquals(SessionFailureKind.AUTHENTICATION, assertNotNull(close.failure).kind)
    }

    @Test
    fun `the socket dying is reported without pretending the television said anything`() {
        val session = machine()
        session.start()
        val close = assertIs<SessionEffect.Close>(session.onTransportFailure("Connection reset").single())
        assertEquals(SessionFailureKind.TRANSPORT, assertNotNull(close.failure).kind)
        assertTrue(session.onTransportFailure("again").isEmpty())
    }

    @Test
    fun `stopping from ready says goodbye first, and stopping before it does not`() {
        val session = machine()
        val television = receiver()
        session.start()
        val televisionHello = assertIs<HandshakeOutcome.Continue>(
            television.onMessage(phoneProfile.hello()),
        ).reply
        val auth = assertIs<AuthMessage>(sentMessages(session.onMessage(televisionHello)).single())
        val established = assertIs<HandshakeOutcome.Established>(television.onMessage(auth))
        session.onMessage(assertNotNull(established.reply))

        val effects = session.stop()
        val bye = assertIs<ByeMessage>(sentMessages(effects).single())
        assertEquals(ByeReason.NORMAL, bye.reason)
        val close = assertIs<SessionEffect.Close>(effects.last())
        assertNull(close.failure, "a deliberate stop is not a failure and carries no sentence")
        assertTrue(session.stop().isEmpty())
    }

    @Test
    fun `stopping before anything was sent closes without a goodbye`() {
        val session = machine()
        val effects = session.stop()
        assertTrue(sentMessages(effects).isEmpty())
        assertIs<SessionEffect.Close>(effects.single())
    }

    @Test
    fun `everything past the handshake belongs to the caller`() {
        val session = machine()
        val television = receiver()
        session.start()
        val televisionHello = assertIs<HandshakeOutcome.Continue>(
            television.onMessage(phoneProfile.hello()),
        ).reply
        val auth = assertIs<AuthMessage>(sentMessages(session.onMessage(televisionHello)).single())
        val established = assertIs<HandshakeOutcome.Established>(television.onMessage(auth))
        session.onMessage(assertNotNull(established.reply))

        assertTrue(session.onMessage(StatsMessage(1, 2, 3, 4)).isEmpty())
        assertEquals(CastSessionPhase.READY, session.phase)
    }

    @Test
    fun `a message after the end is ignored rather than reopening anything`() {
        val session = machine()
        session.start()
        session.onMessage(ByeMessage(ByeReason.NORMAL))
        assertTrue(session.onMessage(televisionProfile.hello()).isEmpty())
    }

    @Test
    fun `the profile it advertised is the one it was given`() {
        assertEquals(phoneProfile, machine().advertisedProfile)
    }

    @Test
    fun `a session token credential is just as acceptable as a code`() {
        val session = CastSessionMachine(
            phoneProfile,
            AuthMethod.SESSION_TOKEN,
            CastAuth.tokenCredential(grantedToken),
        )
        val television = receiver()
        session.start()
        val televisionHello = assertIs<HandshakeOutcome.Continue>(
            television.onMessage(phoneProfile.hello()),
        ).reply
        val auth = assertIs<AuthMessage>(sentMessages(session.onMessage(televisionHello)).single())
        assertEquals(AuthMethod.SESSION_TOKEN, auth.method)
        val established = assertIs<HandshakeOutcome.Established>(television.onMessage(auth))
        session.onMessage(assertNotNull(established.reply))
        assertEquals(CastSessionPhase.READY, session.phase)
    }

    @Test
    fun `an empty credential is refused at the moment it would have been sent`() {
        // HELLO carries no credential, so nothing can object until the AUTH is built -- which is the
        // first point at which an empty one would have reached the television.
        val session = CastSessionMachine(phoneProfile, AuthMethod.PAIRING_CODE, BinaryData.EMPTY)
        session.start()
        assertFailsWith<IllegalArgumentException> { session.onMessage(televisionProfile.hello()) }
    }
}
