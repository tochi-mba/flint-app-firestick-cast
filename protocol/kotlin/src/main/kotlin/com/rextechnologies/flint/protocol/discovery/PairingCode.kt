package com.rextechnologies.flint.protocol.discovery

import java.security.MessageDigest
import java.security.SecureRandom
import java.util.Locale

/** A fixed-width six-digit code intended for human confirmation on the TV. */
class PairingCode private constructor(private val value: String) {
    fun constantTimeMatches(candidate: String): Boolean = MessageDigest.isEqual(
        value.toByteArray(Charsets.US_ASCII),
        candidate.toByteArray(Charsets.US_ASCII),
    )

    override fun equals(other: Any?): Boolean = other is PairingCode && value == other.value
    override fun hashCode(): Int = value.hashCode()
    override fun toString(): String = value

    companion object {
        private val FORMAT = Regex("[0-9]{6}")

        fun generate(random: SecureRandom = SecureRandom()): PairingCode =
            PairingCode(String.format(Locale.ROOT, "%06d", random.nextInt(1_000_000)))

        fun parse(value: String): PairingCode {
            require(FORMAT.matches(value)) { "Pairing code must contain exactly six ASCII digits" }
            return PairingCode(value)
        }

        fun parseOrNull(value: String): PairingCode? =
            if (FORMAT.matches(value)) PairingCode(value) else null
    }
}


