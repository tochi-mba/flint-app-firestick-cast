package com.rextechnologies.flint.protocol.text

import java.nio.charset.StandardCharsets

/**
 * Makes a string somebody else supplied safe to put on the wire and on a screen.
 *
 * Two places needed this and each grew its own answer: a media title arriving from a content
 * provider, and a television's model name arriving from `Build.MODEL`. Both have to lose the
 * characters that would split a record, both have to fit a byte budget, and both need something to
 * fall back to when nothing usable survives.
 *
 * The byte budget is the part that was wrong in both. A budget counted in bytes cannot be applied by
 * removing UTF-16 code units: a string ending in an emoji loses its low surrogate first, and Java
 * then encodes the orphaned high surrogate as a question mark — so the string that goes on the wire
 * is not the string that was measured, and a receiver decoding strict UTF-8 sees something different
 * again.
 */
object SafeText {
    /**
     * Characters that end a record in at least one of this protocol's formats.
     *
     * The tab separates the fields of a discovery announcement and the line feed ends it; a carriage
     * return does neither on its own but arrives with one often enough to be worth the same
     * treatment.
     */
    private val RECORD_SEPARATORS = charArrayOf('\t', '\r', '\n')

    /**
     * @param replacement what a removed character becomes. A space keeps a model name readable when
     *   the separator was doing the work of one; `null` removes it outright, which is what a title
     *   wants, because a control character in the middle of a filename was never meant to be there.
     */
    fun forDisplay(
        raw: String,
        maximumBytes: Int,
        fallback: String,
        replacement: Char? = null,
    ): String {
        require(maximumBytes > 0) { "A budget of no bytes cannot hold anything" }
        val cleaned = buildString(raw.length) {
            raw.forEach { character ->
                when {
                    character in RECORD_SEPARATORS || character.isISOControl() ->
                        replacement?.let(::append)

                    else -> append(character)
                }
            }
        }.trim()
        if (cleaned.isEmpty()) return fallback
        return truncateToBytes(cleaned, maximumBytes).ifEmpty { fallback }
    }

    /**
     * The longest prefix whose UTF-8 encoding fits in [maximumBytes].
     *
     * Cuts on a code-point boundary by walking back over continuation bytes, which are the only
     * bytes in UTF-8 whose top two bits are `10`. UTF-8 has no representation for a lone surrogate,
     * so a prefix taken this way is always something that decodes back to exactly what it says.
     */
    fun truncateToBytes(value: String, maximumBytes: Int): String {
        require(maximumBytes >= 0)
        val encoded = value.toByteArray(StandardCharsets.UTF_8)
        if (encoded.size <= maximumBytes) return value

        var end = maximumBytes
        while (end > 0 && (encoded[end].toInt() and 0xC0) == 0x80) {
            end--
        }
        return String(encoded, 0, end, StandardCharsets.UTF_8)
    }

    /** How many bytes this string costs on the wire. */
    fun byteLength(value: String): Int = value.toByteArray(StandardCharsets.UTF_8).size
}
