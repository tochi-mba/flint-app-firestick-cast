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

/**
 * A session's state, with everything that state actually knows attached to it.
 *
 * The four facts a ready session carries -- the negotiated parameters, the granted token, the
 * receiver's browser port and the envelope version -- used to be four independent `var`s beside a
 * phase enum, so "READY but parameters == null" was a value the type allowed and only convention
 * ruled out. Here it is not a value at all.
 */
sealed interface CastSessionState {
    /** The envelope version frames ride on right now. Before negotiation, the oldest supported. */
    val envelopeVersion: Int
        get() = ProtocolVersion.MIN_SUPPORTED

    val phase: CastSessionPhase

    data object Idle : CastSessionState {
        override val phase: CastSessionPhase get() = CastSessionPhase.IDLE
    }

    data object AwaitingHello : CastSessionState {
        override val phase: CastSessionPhase get() = CastSessionPhase.AWAITING_HELLO
    }

    /**
     * HELLO has been answered, so a version is agreed even though nothing is established yet.
     *
     * This is the state the version matters in earliest: the AUTH reply that leaves here must
     * already be framed at the negotiated version, not at the oldest supported one.
     */
    data class AwaitingAuth(override val envelopeVersion: Int) : CastSessionState {
        override val phase: CastSessionPhase get() = CastSessionPhase.AWAITING_AUTH
    }

    data class Ready(
        val parameters: CastSessionParameters,
        val grantedToken: SessionToken?,
        /**
         * The receiver's browser TLS port, when it advertised one.
         *
         * It arrives in the AUTH ack's `publicKeyFingerprint` field as a decimal string, which is an
         * overload rather than a fingerprint. The phone app opens no browser session; this is
         * carried for diagnostics so the field is not silently discarded.
         */
        val browserTlsPort: Int?,
    ) : CastSessionState {
        override val phase: CastSessionPhase get() = CastSessionPhase.READY
        override val envelopeVersion: Int get() = parameters.protocolVersion
    }

    /** Over. A machine never leaves this state. */
    data class Ended(val lastEnvelopeVersion: Int) : CastSessionState {
        override val phase: CastSessionPhase get() = CastSessionPhase.ENDED
        override val envelopeVersion: Int get() = lastEnvelopeVersion
    }
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

    var state: CastSessionState = CastSessionState.Idle
        private set

    val phase: CastSessionPhase
        get() = state.phase

    /** The version every frame after the handshake rides on. */
    val negotiatedVersion: Int
        get() = state.envelopeVersion

    val parameters: CastSessionParameters?
        get() = (state as? CastSessionState.Ready)?.parameters

    val grantedToken: SessionToken?
        get() = (state as? CastSessionState.Ready)?.grantedToken

    val browserTlsPort: Int?
        get() = (state as? CastSessionState.Ready)?.browserTlsPort

    fun start(): List<SessionEffect> {
        check(state is CastSessionState.Idle) { "This session has already been started" }
        state = CastSessionState.AwaitingHello
        return listOf(
            SessionEffect.SendFrame(WireFrame(ProtocolVersion.MIN_SUPPORTED, handshake.start())),
        )
    }

    fun onMessage(message: WireMessage): List<SessionEffect> {
        if (state is CastSessionState.Ended) return emptyList()

        if (message is ByeMessage) {
            end()
            return listOf(SessionEffect.Close(ByeCopy.of(message)))
        }

        return when (state) {
            is CastSessionState.Idle -> fail(
                SessionFailure(
                    SessionFailureKind.PROTOCOL,
                    "The television spoke before this phone had said anything.",
                ),
            )

            is CastSessionState.AwaitingHello -> onHello(message)
            is CastSessionState.AwaitingAuth -> onAuth(message)

            // Everything past the handshake belongs to the caller: stats, playback state, and any
            // message a future receiver sends that this build does not model. Swallowing them here
            // would put the routing table in two places.
            is CastSessionState.Ready -> emptyList()
            is CastSessionState.Ended -> emptyList()
        }
    }

    /** The socket died without either end saying anything. */
    fun onTransportFailure(detail: String): List<SessionEffect> {
        if (state is CastSessionState.Ended) return emptyList()
        end()
        return listOf(SessionEffect.Close(ByeCopy.transportFailure(detail)))
    }

    /** This phone is stopping. A deliberate stop is not a failure and carries no sentence. */
    fun stop(): List<SessionEffect> {
        if (state is CastSessionState.Ended) return emptyList()
        val sendBye = state is CastSessionState.Ready
        val goodbye = frame(ByeMessage(ByeReason.NORMAL))
        end()
        return buildList {
            if (sendBye) add(SessionEffect.SendFrame(goodbye))
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

        return when (val outcome = handshake.onMessage(message)) {
            is HandshakeOutcome.Continue -> {
                // Taken from the handshake rather than negotiated a second time here. Running the
                // same negotiation twice on the same HELLO meant carrying a fallback for a failure
                // that cannot reach this branch -- Continue is only returned once a version has been
                // agreed -- and a fallback that cannot run is a fallback nobody can check.
                val version = handshake.negotiatedVersion
                    ?: return fail(
                        SessionFailure(
                            SessionFailureKind.PROTOCOL,
                            "The television's greeting was accepted without agreeing a protocol " +
                                "version, which this phone has no way to continue from.",
                        ),
                    )
                state = CastSessionState.AwaitingAuth(version)
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
                val ready = CastSessionState.Ready(
                    parameters = outcome.parameters,
                    grantedToken = handshake.grantedToken,
                    browserTlsPort = message.publicKeyFingerprint
                        ?.toIntOrNull()
                        ?.takeIf { it in 1..65_535 },
                )
                state = ready
                listOf(
                    SessionEffect.Established(ready.parameters, ready.grantedToken, ready.browserTlsPort),
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
        end()
        return listOf(SessionEffect.Close(failure))
    }

    /**
     * Ends the session, keeping the envelope version it was using.
     *
     * Kept rather than reset so that a diagnostic read after the fact says what the session actually
     * spoke, instead of the oldest version anything supports.
     */
    private fun end() {
        state = CastSessionState.Ended(state.envelopeVersion)
    }

    /** The screen dimensions this phone advertised, which the receiver sizes its surface from. */
    val advertisedProfile: DeviceProfile
        get() = profile
}
