package com.rextechnologies.flint.castcore.setup

import com.rextechnologies.flint.protocol.adb.AdbIdentity
import java.security.KeyFactory
import java.security.interfaces.RSAPrivateCrtKey
import java.security.spec.PKCS8EncodedKeySpec
import java.security.spec.RSAPublicKeySpec

/**
 * The bytes an ADB identity is kept as between runs.
 *
 * The television remembers the phone by its public key, so the key pair has to outlive the process
 * or the authorisation prompt comes back on every connection. Only the private key is stored: an
 * RSA private key in its CRT form carries the modulus and the public exponent, so the public half
 * is derived rather than kept twice and cannot drift from the private one. What is stored is the
 * standard PKCS#8 encoding, which is what `KeyFactory` produces and consumes, and nothing here is
 * bespoke.
 */
object AdbIdentityCodec {
    const val ALGORITHM: String = "RSA"

    fun encode(identity: AdbIdentity): ByteArray =
        identity.privateKey.encoded ?: error("This private key cannot be exported")

    /** The identity those bytes described, or `null` when they are not one. */
    fun decode(bytes: ByteArray, comment: String = DEFAULT_COMMENT): AdbIdentity? = runCatching {
        val factory = KeyFactory.getInstance(ALGORITHM)
        val privateKey = factory.generatePrivate(PKCS8EncodedKeySpec(bytes)) as? RSAPrivateCrtKey
            ?: return null
        val publicKey = factory.generatePublic(RSAPublicKeySpec(privateKey.modulus, privateKey.publicExponent))
        AdbIdentity(privateKey, publicKey, comment)
    }.getOrNull()

    const val DEFAULT_COMMENT: String = "flint-mobile@android"
}
