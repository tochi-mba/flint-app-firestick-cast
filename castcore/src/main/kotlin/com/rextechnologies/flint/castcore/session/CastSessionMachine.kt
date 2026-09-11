package com.rextechnologies.flint.castcore.session

import com.rextechnologies.flint.protocol.BinaryData
import com.rextechnologies.flint.protocol.http.SessionToken
import com.rextechnologies.flint.protocol.session.CastSessionParameters
import com.rextechnologies.flint.protocol.session.DeviceProfile
import com.rextechnologies.flint.protocol.session.HandshakeOutcome
import com.rextechnologies.flint.protocol.session.SenderHandshake
import com.rextechnologies.flint.protocol.wire.AuthMessage
import com.rextechnologies.flint.protocol.wire.AuthMethod
import com.rextechnologies.flint.protocol.wire.ByeMessage
import com.rextechnologies.flint.protocol.wire.ByeReason
import com.rextechnologies.flint.protocol.wire.HelloMessage
import com.rextechnologies.flint.protocol.wire.ProtocolVersion
import com.rextechnologies.flint.protocol.wire.VersionNegotiator
import com.rextechnologies.flint.protocol.wire.WireFrame
import com.rextechnologies.flint.protocol.wire.WireMessage

/** Where a session has got to. */
enum class CastSessionPhase {
    /** Nothing has been sent. */
    IDLE,

    /** HELLO is out; the television's own HELLO is what comes back. */
    AWAITING_HELLO,

    /** AUTH is out; the ack carrying a session token is what comes back. */
    AWAITING_AUTH,

    /** Established. Media and control may flow. */
    READY,

    /** Over, for whatever reason. A machine never leaves this state. */
    ENDED,
}

/** Something the caller must do. The machine performs no I/O of its own. */
sealed interface SessionEffect {
    /** Write this frame, exactly as framed — the envelope version is part of the decision. */
    data class SendFrame(val frame: WireFrame) : SessionEffect

    /** The handshake completed. [token] is present when the television granted one. */
    data class Established(
        val parameters: CastSessionParameters,
        val token: SessionToken?,
        val browserTlsPort: Int?,
    ) : SessionEffect

    /** Close the socket. [failure] is `null` only when this phone asked to stop. */
    data class Close(val failure: SessionFailure?) : SessionEffect
}

/**
 * The phone's half of a cast session, as a state machine with no sockets in it.
 *
 * Two things here are easy to get wrong and expensive to discover on a television.
 *
 * The first is envelope versions. HELLO goes out on the oldest supported envelope even though its
 * payload advertises the whole range, because a receiver too old to parse a newer envelope has to be
 * able to read the first exchange in order to say so. Everything after negotiation uses the
 * negotiated version — never [ProtocolVersion.CURRENT], which is this build's ceiling and not the
 * agreement. The receiver applies exactly the same rule in the other direction.
 *
 * The second is that a BYE can arrive *after* authentication appears to have succeeded. The receiver
 * admits one phone at a time and refuses the second only once it has answered its HELLO and validated
 * its credential, so "authenticated, then refused" is the ordinary shape of somebody else already
 * watching rather than a protocol defect. [SessionFailure] carries that distinction out.
 */
class CastSessionMachine(
    private val profile: DeviceProfile,
    authMethod: AuthMethod,
    credential: BinaryData,
) {
    private val handshake = SenderHandshake(profile, authMethod, credential)

    var phase: CastSessionPhase = CastSessionPhase.IDLE
        private set

    /** The version every frame after the handshake rides on. */
    var negotiatedVersion: Int = ProtocolVersion.MIN_SUPPORTED
        private set

    var parameters: CastSessionParameters? = null
        private set

    var grantedToken: SessionToken? = null
        private set

    /**
     * The receiver's browser TLS port, when it advertised one.
     *
     * It arrives in the AUTH ack's `publicKeyFingerprint` field as a decimal string, which is an
     * overload rather than a fingerprint. The phone app opens no browser session; this is carried for
     * diagnostics so the field is not silently discarded.
     */
    var browserTlsPort: Int? = null
        private set

    fun start(): List<SessionEffect> {
        check(phase == CastSessionPhase.IDLE) { "This session has already been started" }
        phase = CastSessionPhase.AWAITING_HELLO
        return listOf(
            SessionEffect.SendFrame(WireFrame(ProtocolVersion.MIN_SUPPORTED, handshake.start())),
        )
    }

    fun onMessage(message: WireMessage): List<SessionEffect> {
        if (phase == CastSessionPhase.ENDED) return emptyList()

        if (message is ByeMessage) {
            phase = CastSessionPhase.ENDED
            return listOf(SessionEffect.Close(ByeCopy.of(message)))
        }

        return when (phase) {
            CastSessionPhase.IDLE -> fail(
                SessionFailure(
                    SessionFailureKind.PROTOCOL,
                    "The television spoke before this phone had said anything.",
                ),
            )

            CastSessionPhase.AWAITING_HELLO -> onHello(message)
            CastSessionPhase.AWAITING_AUTH -> onAuth(message)

            // Everything past the handshake belongs to the caller: stats, playback state, and any
            // message a future receiver sends that this build does not model. Swallowing them here
            // would put the routing table in two places.
            CastSessionPhase.READY -> emptyList()
            CastSessionPhase.ENDED -> emptyList()
        }
    }

    /** The socket died without either end saying anything. */
    fun onTransportFailure(detail: String): List<SessionEffect> {
        if (phase == CastSessionPhase.ENDED) return emptyList()
        phase = CastSessionPhase.ENDED
        return listOf(SessionEffect.Close(ByeCopy.transportFailure(detail)))
    }

    /** This phone is stopping. A deliberate stop is not a failure and carries no sentence. */
    fun stop(): List<SessionEffect> {
        if (phase == CastSessionPhase.ENDED) return emptyList()
        val sendBye = phase == CastSessionPhase.READY
        phase = CastSessionPhase.ENDED
        return buildList {
            if (sendBye) add(SessionEffect.SendFrame(frame(ByeMessage(ByeReason.NORMAL))))
            add(SessionEffect.Close(null))
        }
    }

    /**
     * Frames a message for this session.
     *
     * The single place an envelope version is chosen after the handshake, so no caller has to
     * remember the rule.
     */
    fun frame(message: WireMessage, flags: Int = 0): WireFrame =
        WireFrame(negotiatedVersion, message, flags)

    private fun onHello(message: WireMessage): List<SessionEffect> {
        if (message !is HelloMessage) {
            return fail(
                SessionFailure(
                    SessionFailureKind.PROTOCOL,
                    "The television answered with something other than its own greeting.",
                ),
            )
        }

        // SenderHandshake keeps the negotiated version to itself until the session is established, so
        // it is computed here too rather than guessed at. The two must agree; the alternative is
        // sending AUTH on an envelope the receiver did not agree to.
        val version = VersionNegotiator.negotiate(remote = message)
        return when (val outcome = handshake.onMessage(message)) {
            is HandshakeOutcome.Continue -> {
                negotiatedVersion = version ?: ProtocolVersion.MIN_SUPPORTED
                phase = CastSessionPhase.AWAITING_AUTH
                listOf(SessionEffect.SendFrame(frame(outcome.reply)))
            }

            is HandshakeOutcome.Rejected -> fail(ByeCopy.of(outcome.bye))

            // SenderHandshake only establishes on an AUTH ack, so this is unreachable through its own
            // API. Handled rather than asserted: a crash on a television is a worse outcome than a
            // sentence saying something unexpected happened.
            is HandshakeOutcome.Established -> fail(
                SessionFailure(
                    SessionFailureKind.PROTOCOL,
                    "The television claimed the session was established before this phone had " +
                        "authenticated.",
                ),
            )
        }
    }

    private fun onAuth(message: WireMessage): List<SessionEffect> {
        if (message !is AuthMessage) {
            return fail(
                SessionFailure(
                    SessionFailureKind.PROTOCOL,
                    "The television answered with something other than an authentication reply.",
                ),
            )
        }

        return when (val outcome = handshake.onMessage(message)) {
            is HandshakeOutcome.Established -> {
                phase = CastSessionPhase.READY
                parameters = outcome.parameters
                grantedToken = handshake.grantedToken
                browserTlsPort = message.publicKeyFingerprint?.toIntOrNull()?.takeIf { it in 1..65_535 }
                listOf(
                    SessionEffect.Established(outcome.parameters, grantedToken, browserTlsPort),
                )
            }

            is HandshakeOutcome.Rejected -> fail(ByeCopy.of(outcome.bye))

            is HandshakeOutcome.Continue -> fail(
                SessionFailure(
                    SessionFailureKind.PROTOCOL,
                    "The television asked for another exchange after authentication.",
                ),
            )
        }
    }

    private fun fail(failure: SessionFailure): List<SessionEffect> {
        phase = CastSessionPhase.ENDED
        return listOf(SessionEffect.Close(failure))
    }

    /** The screen dimensions this phone advertised, which the receiver sizes its surface from. */
    val advertisedProfile: DeviceProfile
        get() = profile
}
