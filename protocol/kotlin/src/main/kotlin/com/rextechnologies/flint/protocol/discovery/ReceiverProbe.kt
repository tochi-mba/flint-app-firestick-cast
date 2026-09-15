package com.rextechnologies.flint.protocol.discovery

import com.rextechnologies.flint.protocol.text.SafeText
import java.io.ByteArrayOutputStream
import java.io.InputStream
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

    /**
     * The longest request worth reading.
     *
     * [REQUEST_LINE] and its line feed are nineteen bytes. This leaves room for a longer request
     * from some future version without leaving room for a stranger to send a megabyte.
     */
    const val MAX_REQUEST_BYTES: Int = 256

    /** What the receiver calls itself when the platform gives no model name at all. */
    const val FALLBACK_MODEL_NAME: String = "Fire TV"

    private const val LINE_FEED: Int = '\n'.code

    /**
     * The longest model name that still leaves room for the rest of the response.
     *
     * [RESPONSE_PREFIX] is 18 bytes, the two tabs are 2, the longest port is 5 and the line feed is
     * 1 -- so 26 bytes are spoken for before the name gets any.
     */
    const val MAX_MODEL_NAME_BYTES: Int = MAX_RESPONSE_BYTES - 26

    /**
     * Reads one line from a stranger, allocating no more than [maximumBytes] for it.
     *
     * `BufferedReader.readLine` is the obvious way to do this and the wrong one: its buffer-size
     * argument bounds the buffer, not the line, so it accumulates without limit until a line feed
     * arrives. Both ends of this exchange read a line from something they have not authenticated —
     * a phone sweeping a subnet talks to whatever has the port open, and a television answers
     * anyone who connects — so the side doing the allocating has to be the side that stops. That is
     * what [MAX_RESPONSE_BYTES] has always claimed and what nothing was doing.
     *
     * A line over the limit returns `null`, which every caller already treats as "not a Flint
     * receiver". A byte at a time on purpose: callers pass a buffered stream, so the syscalls are
     * already amortised, and stopping exactly on the line feed leaves the rest of the socket for
     * whoever reads it next.
     */
    fun readLine(input: InputStream, maximumBytes: Int): String? {
        require(maximumBytes > 0) { "A line budget must leave room for at least one byte" }
        val line = ByteArrayOutputStream()
        while (true) {
            val byte = input.read()
            if (byte < 0) return if (line.size() == 0) null else decodeLine(line)
            if (byte == LINE_FEED) return decodeLine(line)
            if (line.size() >= maximumBytes) return null
            line.write(byte)
        }
    }

    private fun decodeLine(line: ByteArrayOutputStream): String =
        String(line.toByteArray(), StandardCharsets.UTF_8).removeSuffix("\r")

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

    /**
     * Replaces every character that would split the record, and falls back to a usable name.
     *
     * The budget is what is left of a response once the prefix, the two tabs, the largest port and
     * the line feed have taken their share, so a sanitised name is always a name that fits.
     */
    fun sanitizeModelName(modelName: String): String = SafeText.forDisplay(
        raw = modelName,
        maximumBytes = MAX_MODEL_NAME_BYTES,
        fallback = FALLBACK_MODEL_NAME,
        replacement = ' ',
    )

    /**
     * Reads one response line, or `null` when it is not a receiver announcement.
     *
     * Returns `null` rather than throwing on every malformed input. A subnet sweep talks to whatever
     * happens to have a port open, so a wrong answer is the ordinary case and not an error worth
     * unwinding a coroutine for.
     */
    fun parseResponse(line: String): ReceiverAnnouncement? {
        val trimmed = line.trimEnd('\r', '\n')
        // Bytes, not characters. A model name is explicitly allowed to be non-ASCII, so 512 CJK
        // characters is 1,536 bytes -- which the char-length comparison this replaced let through.
        if (SafeText.byteLength(trimmed) > MAX_RESPONSE_BYTES) return null
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
