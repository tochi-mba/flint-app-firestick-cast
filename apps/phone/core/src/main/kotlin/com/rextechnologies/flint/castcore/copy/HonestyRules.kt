package com.rextechnologies.flint.castcore.copy

/**
 * The words this product does not get to use, and the shape a sentence has to have.
 *
 * Two constraints are enforced here rather than left to review.
 *
 * The cast link is authorised, not encrypted. A session token proves which phone is talking; it does
 * not hide what is said. Until an authenticated encrypted transport exists and has been verified, no
 * string anywhere may describe cast traffic as private, secure or encrypted — and the reason that is
 * a lint rule rather than a guideline is that the words are the easy ones to reach for, and a reader
 * who believes them behaves differently on a shared network.
 *
 * The other is shape. Copy is read aloud by a screen reader and rendered into cards that assume
 * sentences, so a fragment ending in no punctuation reads as a truncation and an exclamation mark
 * reads as a different product.
 */
object HonestyRules {
    /**
     * The longest a single piece of copy may be.
     *
     * Chosen to be a little above the longest verdict the assessor produces, so a card stays readable
     * at a large font scale without the reason being cut off.
     */
    const val MAXIMUM_COPY_LENGTH: Int = 420

    /**
     * Words that make a claim about confidentiality.
     *
     * Matched on word boundaries and case-insensitively. The list is deliberately blunt: an exception
     * for "a secure place to keep it" would be the first step towards an exception for the sentence
     * that matters.
     */
    val CONFIDENTIALITY_WORDS: List<String> = listOf(
        "private",
        "privately",
        "privacy",
        "confidential",
        "confidentially",
        "encrypted",
        "encrypt",
        "encryption",
        "end-to-end",
        "secure",
        "securely",
        "secured",
    )

    /**
     * Every confidentiality claim in a piece of copy, lower-cased, in the order they appear in it.
     *
     * Each entry is searched for on its own boundaries and the hits are sorted by position. The
     * earlier form checked the list against the text and returned claims in the order they were
     * declared above while saying it did not, and it treated a hyphen as part of a word -- so
     * "privacy-preserving" was one word that matched nothing. A hyphen is a boundary here, which is
     * what lets "end-to-end" in the list still match as a phrase while "privacy" inside
     * "privacy-preserving" is found.
     */
    fun confidentialityClaims(text: String): List<String> {
        val lower = text.lowercase()
        return CONFIDENTIALITY_WORDS
            .flatMap { word -> boundedPattern(word).findAll(lower).map { it.range.first to word } }
            .sortedBy { (position, _) -> position }
            .map { (_, word) -> word }
    }

    /** [word] with a letter or digit on neither side. The hyphen is deliberately not a word character. */
    private fun boundedPattern(word: String): Regex =
        Regex("(?<![\\p{L}\\p{N}])" + Regex.escape(word) + "(?![\\p{L}\\p{N}])")

    /** Whether a piece of prose ends the way a sentence does. */
    fun isCompleteSentence(text: String): Boolean {
        val trimmed = text.trim()
        return trimmed.isNotEmpty() && trimmed.last() in SENTENCE_ENDINGS
    }

    /** No exclamation marks anywhere. The product does not raise its voice. */
    fun raisesItsVoice(text: String): Boolean = '!' in text

    private val SENTENCE_ENDINGS = charArrayOf('.', '?', '…', ':')
}
