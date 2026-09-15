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
import com.rextechnologies.flint.protocol.wire.VersionNegotiator
import com.rextechnologies.flint.protocol.wire.WireMessage
import java.nio.charset.StandardCharsets

data class DeviceProfile(
    val deviceName: String,
    val codecCapabilities: Set<CodecId>,
    val screenWidth: Int,
    val screenHeight: Int,
    val densityDpi: Int,
) {
    fun hello(
        minimumVersion: Int = ProtocolVersion.MIN_SUPPORTED,
        maximumVersion: Int = ProtocolVersion.CURRENT,
    ): HelloMessage = HelloMessage(
        minimumVersion = minimumVersion,
        maximumVersion = maximumVersion,
        deviceName = deviceName,
        codecCapabilities = codecCapabilities,
        screenWidth = screenWidth,
        screenHeight = screenHeight,
        densityDpi = densityDpi,
    )
}

/** The negotiated result both endpoints agree on before any media flows. */
data class CastSessionParameters(
    val protocolVersion: Int,
    val peer: HelloMessage,
    val videoCodec: CodecId,
)

sealed interface HandshakeOutcome {
    /** Send [reply] and keep waiting. */
    data class Continue(val reply: WireMessage) : HandshakeOutcome

    /** Send [reply] when present, then treat the session as established. */
    data class Established(
        val parameters: CastSessionParameters,
        val reply: WireMessage? = null,
    ) : HandshakeOutcome

    /** Send [bye] and close. */
    data class Rejected(val bye: ByeMessage) : HandshakeOutcome
}

object CastAuth {
    private const val MAX_CREDENTIAL_BYTES = 512

    // HEVC first: it costs roughly a third less bitrate on a shared SoftAP link.
    val VIDEO_PREFERENCE: List<CodecId> = listOf(CodecId.H265, CodecId.H264)

    fun pairingCredential(code: PairingCode): BinaryData =
        BinaryData.of(code.toString().toByteArray(StandardCharsets.US_ASCII))

    fun tokenCredential(token: SessionToken): BinaryData =
        BinaryData.of(token.toString().toByteArray(StandardCharsets.US_ASCII))

    fun asAscii(credential: BinaryData): String? {
        if (credential.isEmpty || credential.size > MAX_CREDENTIAL_BYTES) return null
        val bytes = credential.toByteArray()
        if (bytes.any { it < 0x20 || it > 0x7e }) return null
        return String(bytes, StandardCharsets.US_ASCII)
    }

    fun selectVideoCodec(local: Set<CodecId>, remote: Set<CodecId>): CodecId? =
        VIDEO_PREFERENCE.firstOrNull { it in local && it in remote }
}

/**
 * Receiver half of the handshake.
 *
 * Refusal detail is deliberately coarse. Reporting which of the version, the
 * codec set, or the credential failed would let any hotspot client enumerate
 * receiver state, so every authentication refusal returns the same text.
 */
class ReceiverHandshake(
    private val profile: DeviceProfile,
    private val pairingCode: PairingCode,
    private val sessionToken: SessionToken,
    private val trustedFingerprints: Set<String> = emptySet(),
    /**
     * Live browser TLS listener port, or 0 when the dedicated endpoint is down.
     *
     * Carried on the AUTH-ack optional fingerprint field as a decimal port string so a paired
     * Windows host can autofill Web without relying on multicast TXT (often blocked on Wi-Fi).
     * Old hosts ignore the field; it is never a real certificate fingerprint on SESSION_TOKEN acks.
     */
    private val browserPortProvider: () -> Int = { 0 },
) {
    private var peerHello: HelloMessage? = null
    private var negotiatedVersion: Int = 0

    var established: Boolean = false
        private set

    fun onMessage(message: WireMessage): HandshakeOutcome = when (message) {
        is HelloMessage -> onHello(message)
        is AuthMessage -> onAuth(message)
        else -> reject(ByeReason.PROTOCOL_ERROR, "Expected HELLO or AUTH")
    }

    private fun onHello(hello: HelloMessage): HandshakeOutcome {
        if (peerHello != null) return reject(ByeReason.PROTOCOL_ERROR, "Duplicate HELLO")
        val version = VersionNegotiator.negotiate(remote = hello)
            ?: return reject(ByeReason.UNSUPPORTED_VERSION, "No common protocol version")
        peerHello = hello
        negotiatedVersion = version
        return HandshakeOutcome.Continue(profile.hello())
    }

    private fun onAuth(auth: AuthMessage): HandshakeOutcome {
        val hello = peerHello ?: return reject(ByeReason.PROTOCOL_ERROR, "AUTH before HELLO")
        val presented = CastAuth.asAscii(auth.credential)
            ?: return reject(ByeReason.AUTHENTICATION_FAILED, AUTH_FAILURE)

        val accepted = when (auth.method) {
            AuthMethod.PAIRING_CODE -> pairingCode.constantTimeMatches(presented)
            AuthMethod.SESSION_TOKEN -> sessionToken.constantTimeMatches(presented)
            AuthMethod.PUBLIC_KEY_PROOF ->
                auth.publicKeyFingerprint in trustedFingerprints &&
                    sessionToken.constantTimeMatches(presented)
        }
        if (!accepted) return reject(ByeReason.AUTHENTICATION_FAILED, AUTH_FAILURE)

        val videoCodec = CastAuth.selectVideoCodec(profile.codecCapabilities, hello.codecCapabilities)
            ?: return reject(ByeReason.PROTOCOL_ERROR, "No common video codec")

        established = true
        val browserPort = browserPortProvider().takeIf { it in 1..65_535 }?.toString()
        return HandshakeOutcome.Established(
            parameters = CastSessionParameters(negotiatedVersion, hello, videoCodec),
            reply = AuthMessage(
                method = AuthMethod.SESSION_TOKEN,
                credential = CastAuth.tokenCredential(sessionToken),
                publicKeyFingerprint = browserPort,
            ),
        )
    }

    private fun reject(reason: ByeReason, detail: String): HandshakeOutcome =
        HandshakeOutcome.Rejected(ByeMessage(reason, detail))

    private companion object {
        const val AUTH_FAILURE = "Authentication failed"
    }
}

/** Phone half of the handshake. */
class SenderHandshake(
    private val profile: DeviceProfile,
    private val credentialMethod: AuthMethod,
    private val credential: BinaryData,
    private val publicKeyFingerprint: String? = null,
) {
    private var sentHello = false
    private var pendingParameters: CastSessionParameters? = null

    var grantedToken: SessionToken? = null
        private set

    /**
     * The version every frame after HELLO rides on, or `null` before the receiver has answered.
     *
     * Exposed because the caller needs it one message earlier than [HandshakeOutcome.Established]
     * carries it: the AUTH reply that `onHello` returns must already go out on the negotiated
     * envelope. Without this the caller had to run `VersionNegotiator.negotiate` a second time on
     * the same HELLO and hope the two agreed -- and carry a fallback for the case where its own
     * negotiation failed, which cannot happen, because a failed negotiation is why this returns
     * `Rejected` instead.
     */
    val negotiatedVersion: Int?
        get() = pendingParameters?.protocolVersion

    fun start(): WireMessage {
        check(!sentHello) { "Handshake already started" }
        sentHello = true
        return profile.hello()
    }

    fun onMessage(message: WireMessage): HandshakeOutcome = when (message) {
        is HelloMessage -> onHello(message)
        is AuthMessage -> onAuth(message)
        is ByeMessage -> HandshakeOutcome.Rejected(message)
        else -> HandshakeOutcome.Rejected(ByeMessage(ByeReason.PROTOCOL_ERROR, "Unexpected message"))
    }

    private fun onHello(hello: HelloMessage): HandshakeOutcome {
        val version = VersionNegotiator.negotiate(remote = hello)
            ?: return HandshakeOutcome.Rejected(
                ByeMessage(ByeReason.UNSUPPORTED_VERSION, "No common protocol version"),
            )
        val codec = CastAuth.selectVideoCodec(profile.codecCapabilities, hello.codecCapabilities)
            ?: return HandshakeOutcome.Rejected(
                ByeMessage(ByeReason.PROTOCOL_ERROR, "No common video codec"),
            )
        pendingParameters = CastSessionParameters(version, hello, codec)
        return HandshakeOutcome.Continue(AuthMessage(credentialMethod, credential, publicKeyFingerprint))
    }

    private fun onAuth(message: AuthMessage): HandshakeOutcome {
        val parameters = pendingParameters
            ?: return HandshakeOutcome.Rejected(ByeMessage(ByeReason.PROTOCOL_ERROR, "AUTH before HELLO"))
        grantedToken = CastAuth.asAscii(message.credential)?.let(SessionToken::parseOrNull)
        return HandshakeOutcome.Established(parameters)
    }
}
