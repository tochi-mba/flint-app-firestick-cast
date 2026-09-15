package com.rextechnologies.flint.mobile.platform

/**
 * How an encrypted token and the nonce that encrypted it share one stored string.
 *
 * Its own object, and free of every Android type, so the format has somewhere a test can reach. It
 * is the half of [TokenStore] where a mistake is silent: a keystore failure throws, while a
 * mis-parsed envelope produces a token that decrypts to something else or a phone that quietly
 * forgets a pairing it had.
 *
 * The initialisation vector is not a secret and has to survive alongside the ciphertext.
 * Length-prefixed rather than fixed at twelve bytes, because a provider is entitled to pick a
 * different nonce size and a hard-coded split would fail on the phone that did.
 */
object TokenEnvelope {
    /** The longest nonce a one-byte length prefix can describe. */
    const val MAXIMUM_IV_BYTES: Int = 255

    data class Unpacked(val iv: ByteArray, val ciphertext: ByteArray) {
        // Generated equals on a data class compares arrays by identity, which for a value made of
        // two arrays is never what anybody means.
        override fun equals(other: Any?): Boolean =
            this === other ||
                (
                    other is Unpacked &&
                        iv.contentEquals(other.iv) &&
                        ciphertext.contentEquals(other.ciphertext)
                    )

        override fun hashCode(): Int = 31 * iv.contentHashCode() + ciphertext.contentHashCode()
    }

    fun pack(iv: ByteArray, ciphertext: ByteArray): ByteArray {
        require(iv.isNotEmpty()) { "A nonce of no bytes is not a nonce" }
        require(iv.size <= MAXIMUM_IV_BYTES) { "A nonce this long cannot be length-prefixed" }
        require(ciphertext.isNotEmpty()) { "There is nothing to store" }
        val packed = ByteArray(1 + iv.size + ciphertext.size)
        packed[0] = iv.size.toByte()
        iv.copyInto(packed, 1)
        ciphertext.copyInto(packed, 1 + iv.size)
        return packed
    }

    /**
     * Splits a stored envelope, or `null` when it is not one.
     *
     * `null` rather than an exception for every malformed input. A stored value that no longer parses
     * is a pairing this phone has lost, which is an ordinary thing to recover from by pairing again
     * -- and not something to unwind a coroutine over.
     */
    fun unpack(packed: ByteArray): Unpacked? {
        if (packed.size < MINIMUM_BYTES) return null
        val ivLength = packed[0].toInt() and 0xff
        // Strictly greater: an envelope with a nonce and no ciphertext has nothing in it.
        if (ivLength <= 0 || packed.size <= 1 + ivLength) return null
        return Unpacked(
            iv = packed.copyOfRange(1, 1 + ivLength),
            ciphertext = packed.copyOfRange(1 + ivLength, packed.size),
        )
    }

    /** One length byte, at least one nonce byte, at least one ciphertext byte. */
    private const val MINIMUM_BYTES = 3
}
