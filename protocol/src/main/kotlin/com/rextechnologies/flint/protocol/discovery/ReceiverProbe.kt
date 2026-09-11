package com.rextechnologies.flint.protocol.discovery

import java.nio.charset.StandardCharsets

/**
 * One receiver, as announced by a probe.
 *
 * @property modelName what the television calls itself. It may contain spaces but never a tab, which
 *   is what makes the wire format unambiguous without quoting or escaping.
 * @property port the port the receiver is actually listening on, which is not always the default: the
 *   receiver binds whatever it was asked for and reports the bound port back.
 */
data class ReceiverAnnouncement(
    val modelName: String,
    val port: Int,
) {
    init {
        require(port in 1..65_535) { "Receiver port must be a valid port" }
        require('\t' !in modelName) { "A tab in the model name would split the announcement" }
    }
}

/**
 * The plaintext probe a phone uses to find a receiver without multicast.
 *
 * Multicast is the thing that fails on a SoftAP link, so this exists as the designed fallback: a
 * single request line, a single response line, one exchange per connection or datagram. It rides the
 * receiver's ordinary TCP port, multiplexed by the first byte, and — for the broadcast rung — the same
 * port number on UDP, which is a separate namespace and so costs nothing.
 *
 * The request is compared for exact equality on the receiver, so there is no tolerance here for
 * casing, padding or a different version suffix. That is deliberate: a probe that accepted near
 * misses would answer things that are not Flint.
 */
object ReceiverProbe {
    /** The receiver's port. The same number on TCP and on UDP; they do not collide. */
    const val PORT: Int = 47_855

    /** Sent by the phone, terminated by a single line feed. */
    const val REQUEST_LINE: String = "REXCAST DISCOVER/1"

    /** The first field of the response, followed by the model name and the port. */
    const val RESPONSE_PREFIX: String = "REXCAST RECEIVER/1"

    /**
     * The longest response worth reading.
     *
     * A receiver's answer is a prefix, a model name and a port. Anything appreciably longer is either
     * not a receiver or is trying to make a scanning phone allocate, so the readers stop there.
     */
    const val MAX_RESPONSE_BYTES: Int = 512

    /** What the receiver calls itself when the platform gives no model name at all. */
    const val FALLBACK_MODEL_NAME: String = "Fire TV"

    /** The request bytes, ready to write to a socket. ASCII, because the line is pure ASCII. */
    fun requestBytes(): ByteArray = "$REQUEST_LINE\n".toByteArray(StandardCharsets.US_ASCII)

    /** Whether a line read from a client is the probe request, compared exactly. */
    fun isRequest(line: String): Boolean = line == REQUEST_LINE

    /**
     * The response bytes for a receiver announcing itself.
     *
     * Tabs, carriage returns and line feeds in the model name each become a single space, because any
     * of the three would otherwise split the record. UTF-8, because a model name is not always ASCII.
     */
    fun responseBytes(modelName: String, port: Int): ByteArray {
        require(port in 1..65_535) { "Receiver port must be a valid port" }
        return "$RESPONSE_PREFIX\t${sanitizeModelName(modelName)}\t$port\n".toByteArray(StandardCharsets.UTF_8)
    }

    /** Replaces every character that would split the record, and falls back to a usable name. */
    fun sanitizeModelName(modelName: String): String {
        val flattened = modelName
            .replace('\t', ' ')
            .replace('\r', ' ')
            .replace('\n', ' ')
            .trim()
        return flattened.ifBlank { FALLBACK_MODEL_NAME }
    }

    /**
     * Reads one response line, or `null` when it is not a receiver announcement.
     *
     * Returns `null` rather than throwing on every malformed input. A subnet sweep talks to whatever
     * happens to have a port open, so a wrong answer is the ordinary case and not an error worth
     * unwinding a coroutine for.
     */
    fun parseResponse(line: String): ReceiverAnnouncement? {
        val trimmed = line.trimEnd('\r', '\n')
        if (trimmed.length > MAX_RESPONSE_BYTES) return null
        val fields = trimmed.split('\t')
        if (fields.size != 3) return null
        if (fields[0] != RESPONSE_PREFIX) return null
        val port = fields[2].toIntOrNull() ?: return null
        if (port !in 1..65_535) return null
        // A response whose name is blank is still a receiver; it simply has not told us what it is.
        val modelName = fields[1].trim().ifBlank { FALLBACK_MODEL_NAME }
        return ReceiverAnnouncement(modelName, port)
    }
}
