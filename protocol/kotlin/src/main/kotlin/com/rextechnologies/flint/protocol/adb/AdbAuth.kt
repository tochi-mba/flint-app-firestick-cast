package com.rextechnologies.flint.protocol.adb

import com.rextechnologies.flint.protocol.BinaryData
import java.io.ByteArrayOutputStream
import java.math.BigInteger
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.MessageDigest
import java.security.PrivateKey
import java.security.PublicKey
import java.security.interfaces.RSAPublicKey
import javax.crypto.Cipher
import kotlin.io.encoding.Base64

data class AdbIdentity(
    val privateKey: PrivateKey,
    val publicKey: PublicKey,
    val comment: String = "rexcast@android",
) {
    init {
        require(comment.isNotBlank() && comment.none { it == '\u0000' || it == '\r' || it == '\n' })
        require(publicKey is RSAPublicKey && publicKey.modulus.bitLength() == AdbAuth.RSA_BITS)
    }
}

/** RSA helpers matching ADB's legacy authentication wire format. */
object AdbAuth {
    const val RSA_BITS: Int = 2048
    const val TOKEN_BYTES: Int = 20
    private const val MODULUS_BYTES = RSA_BITS / 8
    private const val MODULUS_WORDS = MODULUS_BYTES / 4
    private val SHA1_DIGEST_INFO_PREFIX = byteArrayOf(
        0x30, 0x21, 0x30, 0x09, 0x06, 0x05, 0x2b, 0x0e,
        0x03, 0x02, 0x1a, 0x05, 0x00, 0x04, 0x14,
    )

    fun generateIdentity(comment: String = "rexcast@android"): AdbIdentity {
        val generator = KeyPairGenerator.getInstance("RSA")
        generator.initialize(RSA_BITS)
        val pair = generator.generateKeyPair()
        return AdbIdentity(pair.private, pair.public, comment)
    }

    fun identity(keyPair: KeyPair, comment: String = "rexcast@android"): AdbIdentity =
        AdbIdentity(keyPair.private, keyPair.public, comment)

    /** Signs the 20-byte AUTH token without hashing that already-hashed token again. */
    fun signToken(identity: AdbIdentity, token: ByteArray): BinaryData {
        require(token.size == TOKEN_BYTES) { "ADB AUTH token must contain exactly 20 bytes" }
        val digestInfo = SHA1_DIGEST_INFO_PREFIX + token
        val cipher = Cipher.getInstance("RSA/ECB/PKCS1Padding")
        cipher.init(Cipher.ENCRYPT_MODE, identity.privateKey)
        return BinaryData.of(cipher.doFinal(digestInfo))
    }

    fun verifyTokenSignature(publicKey: PublicKey, token: ByteArray, signature: BinaryData): Boolean {
        if (token.size != TOKEN_BYTES || signature.size != MODULUS_BYTES) return false
        return try {
            val cipher = Cipher.getInstance("RSA/ECB/PKCS1Padding")
            cipher.init(Cipher.DECRYPT_MODE, publicKey)
            val recovered = signature.withBytes { cipher.doFinal(it) }
            MessageDigest.isEqual(SHA1_DIGEST_INFO_PREFIX + token, recovered)
        } catch (_: Exception) {
            false
        }
    }

    /** Produces Android's `RSAPublicKey` base64 line, including its terminating NUL. */
    fun publicKeyPayload(identity: AdbIdentity): BinaryData {
        val rsa = identity.publicKey as RSAPublicKey
        val modulus = rsa.modulus
        require(modulus.bitLength() == RSA_BITS && modulus.testBit(0))
        val two32 = BigInteger.ONE.shiftLeft(32)
        val lowWord = modulus.and(two32 - BigInteger.ONE)
        val n0Inverse = lowWord.modInverse(two32).negate().mod(two32)
        val rSquared = BigInteger.ONE.shiftLeft(RSA_BITS * 2).mod(modulus)
        val exponent = rsa.publicExponent
        require(exponent.signum() > 0 && exponent.bitLength() <= 32)

        val binary = ByteArrayOutputStream(4 + 4 + MODULUS_BYTES * 2 + 4)
        writeUint32Le(binary, MODULUS_WORDS.toLong())
        writeUint32Le(binary, n0Inverse.toLong())
        binary.write(toFixedLittleEndian(modulus, MODULUS_BYTES))
        binary.write(toFixedLittleEndian(rSquared, MODULUS_BYTES))
        writeUint32Le(binary, exponent.toLong())

        val line = Base64.encode(binary.toByteArray()) + " " + identity.comment + '\u0000'
        return BinaryData.of(line.toByteArray(Charsets.US_ASCII))
    }

    fun fingerprint(publicKey: PublicKey): String = MessageDigest.getInstance("SHA-256")
        .digest(publicKey.encoded)
        .joinToString(":") { "%02x".format(it.toInt() and 0xff) }

    fun signatureMessage(identity: AdbIdentity, token: ByteArray): AdbMessage = AdbMessage(
        command = AdbCommands.AUTH,
        argument0 = AdbAuthType.SIGNATURE,
        payload = signToken(identity, token),
    )

    fun publicKeyMessage(identity: AdbIdentity): AdbMessage = AdbMessage(
        command = AdbCommands.AUTH,
        argument0 = AdbAuthType.RSA_PUBLIC_KEY,
        payload = publicKeyPayload(identity),
    )

    private fun toFixedLittleEndian(value: BigInteger, size: Int): ByteArray {
        val bigEndianWithSign = value.toByteArray()
        val first = if (bigEndianWithSign.size > 1 && bigEndianWithSign[0] == 0.toByte()) 1 else 0
        val length = bigEndianWithSign.size - first
        require(length <= size) { "Integer does not fit in $size bytes" }
        val output = ByteArray(size)
        repeat(length) { index -> output[index] = bigEndianWithSign[bigEndianWithSign.lastIndex - index] }
        return output
    }

    private fun writeUint32Le(output: ByteArrayOutputStream, value: Long) {
        require(value in 0..0xffff_ffffL)
        output.write(value.toInt())
        output.write((value ushr 8).toInt())
        output.write((value ushr 16).toInt())
        output.write((value ushr 24).toInt())
    }
}

