package com.rextechnologies.flint.protocol.http

import java.security.MessageDigest
import java.security.SecureRandom

/** A canonical 256-bit URL-safe bearer token, without Base64 padding. */
class SessionToken private constructor(private val encoded: String) {
    fun constantTimeMatches(candidate: String): Boolean = MessageDigest.isEqual(
        encoded.toByteArray(Charsets.US_ASCII),
        candidate.toByteArray(Charsets.US_ASCII),
    )

    override fun equals(other: Any?): Boolean = other is SessionToken && encoded == other.encoded
    override fun hashCode(): Int = encoded.hashCode()
    override fun toString(): String = encoded

    companion object {
        const val ENTROPY_BYTES: Int = 32
        private val TOKEN_FORMAT = Regex("[A-Za-z0-9_-]{43}")
        private const val BASE64_URL_ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"

        fun generate(random: SecureRandom = SecureRandom()): SessionToken {
            val entropy = ByteArray(ENTROPY_BYTES)
            random.nextBytes(entropy)
            return SessionToken(encodeBase64Url(entropy))
        }

        fun parse(value: String): SessionToken {
            require(TOKEN_FORMAT.matches(value)) { "Session token is not canonical Base64URL" }
            val decoded = try {
                decodeBase64Url(value)
            } catch (exception: IllegalArgumentException) {
                throw IllegalArgumentException("Session token is not valid Base64URL", exception)
            }
            require(decoded.size == ENTROPY_BYTES && encodeBase64Url(decoded) == value) {
                "Session token must encode exactly 256 bits"
            }
            return SessionToken(value)
        }

        fun parseOrNull(value: String): SessionToken? = try {
            parse(value)
        } catch (_: IllegalArgumentException) {
            null
        }

        private fun encodeBase64Url(bytes: ByteArray): String {
            val encoded = StringBuilder((bytes.size * 8 + 5) / 6)
            var buffer = 0
            var bitCount = 0
            for (byte in bytes) {
                buffer = (buffer shl 8) or (byte.toInt() and 0xff)
                bitCount += 8
                while (bitCount >= 6) {
                    bitCount -= 6
                    encoded.append(BASE64_URL_ALPHABET[(buffer shr bitCount) and 0x3f])
                    buffer = buffer and ((1 shl bitCount) - 1)
                }
            }

            if (bitCount > 0) {
                encoded.append(BASE64_URL_ALPHABET[(buffer shl (6 - bitCount)) and 0x3f])
            }
            return encoded.toString()
        }

        private fun decodeBase64Url(value: String): ByteArray {
            val decoded = ByteArray(value.length * 6 / 8)
            var outputIndex = 0
            var buffer = 0
            var bitCount = 0
            for (character in value) {
                val valueIndex = BASE64_URL_ALPHABET.indexOf(character)
                require(valueIndex >= 0) { "Session token contains a non-Base64URL character" }
                buffer = (buffer shl 6) or valueIndex
                bitCount += 6
                while (bitCount >= 8) {
                    bitCount -= 8
                    decoded[outputIndex++] = ((buffer shr bitCount) and 0xff).toByte()
                    buffer = buffer and ((1 shl bitCount) - 1)
                }
            }

            require(bitCount == 2 && buffer == 0) { "Session token has invalid Base64URL padding bits" }
            return decoded.copyOf(outputIndex)
        }
    }
}
