package com.rextechnologies.flint.castcore.session

import com.rextechnologies.flint.protocol.wire.ByeMessage
import com.rextechnologies.flint.protocol.wire.ByeReason

/** Why a session ended, in a shape the UI can branch on. */
enum class SessionFailureKind {
    /** The television is already casting from another phone. */
    RECEIVER_BUSY,

    /** The code or the stored token was not accepted. */
    AUTHENTICATION,

    /** No protocol version in common. One end is older than the other. */
    VERSION,

    /** Either end sent something the other could not make sense of. */
    PROTOCOL,

    /** The socket died. Nothing was said; it simply stopped. */
    TRANSPORT,

    /** The television ended it deliberately — the app was closed, or the receiver stopped. */
    RECEIVER_STOPPED,
}

/**
 * A session ending, with the sentence to show for it.
 *
 * @property sentence a complete, user-facing sentence. Where the receiver supplied its own wording,
 *   that wording is used verbatim: it is the television's account of what happened, and paraphrasing
 *   it would put two different explanations of one event into the world.
 * @property remedy what to do about it, or `null` when there is nothing.
 */
data class SessionFailure(
    val kind: SessionFailureKind,
    val sentence: String,
    val remedy: String? = null,
) {
    init {
        require(sentence.isNotBlank())
        require(remedy == null || remedy.isNotBlank())
    }
}

/**
 * Turns the receiver's BYE into something a person can read.
 *
 * The busy case is the one worth care. The receiver refuses a second phone with
 * `ByeReason.PROTOCOL_ERROR` rather than a reason of its own, and it does so *after* answering HELLO
 * and validating AUTH — so a phone that treats any post-authentication PROTOCOL_ERROR as a bug will
 * report a protocol failure for the most ordinary situation there is, which is somebody else already
 * watching. The detail string is what distinguishes it, so the detail is what is matched.
 */
object ByeCopy {
    /** The receiver's own words, matched exactly. Changing either side alone breaks the match. */
    const val RECEIVER_BUSY_DETAIL: String = "This TV is already casting from another phone"

    fun of(bye: ByeMessage): SessionFailure {
        if (bye.detail == RECEIVER_BUSY_DETAIL) {
            return SessionFailure(
                SessionFailureKind.RECEIVER_BUSY,
                bye.detail + ".",
                "Stop casting on the other phone, or pick a different television.",
            )
        }

        return when (bye.reason) {
            ByeReason.AUTHENTICATION_FAILED -> SessionFailure(
                SessionFailureKind.AUTHENTICATION,
                "The television did not accept this phone.",
                "Check the six-digit code on the TV and enter it again. A code is only good for one " +
                    "pairing, so a code from an earlier attempt will not work.",
            )

            ByeReason.UNSUPPORTED_VERSION -> SessionFailure(
                SessionFailureKind.VERSION,
                "This phone and this television have no version of the Flint protocol in common.",
                "Update whichever of the two is older. Flint can install a matching receiver from " +
                    "this phone over ADB.",
            )

            ByeReason.RECEIVER_STOPPED -> SessionFailure(
                SessionFailureKind.RECEIVER_STOPPED,
                "The television ended the session.",
                "Open Flint on the TV and try again.",
            )

            ByeReason.NORMAL -> SessionFailure(
                SessionFailureKind.RECEIVER_STOPPED,
                "The television closed the session.",
            )

            ByeReason.PROTOCOL_ERROR -> SessionFailure(
                SessionFailureKind.PROTOCOL,
                // The receiver's detail is the only account of what it objected to, so it is quoted
                // rather than replaced. Where it sent nothing, say that rather than inventing a cause.
                bye.detail.ifBlank { "The television rejected the connection without saying why." }
                    .ensureSentence(),
                null,
            )
        }
    }

    /** The transport dying is not a BYE, and must not be reported as though the TV said something. */
    fun transportFailure(detail: String): SessionFailure = SessionFailure(
        SessionFailureKind.TRANSPORT,
        "The connection to the television dropped." +
            detail.takeIf { it.isNotBlank() }?.let { " $it" }.orEmpty().ensureSentence(),
        "Check that the TV is still awake and on this phone's hotspot, then connect again.",
    )

    private fun String.ensureSentence(): String =
        if (isEmpty() || last() in SENTENCE_ENDINGS) this else "$this."

    private val SENTENCE_ENDINGS = charArrayOf('.', '?', '!', '…')
}
