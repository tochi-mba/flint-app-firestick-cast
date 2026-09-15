package com.rextechnologies.flint.mobile.platform

import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * Encryption at rest with one keystore-held AES key, for the two secrets this phone keeps.
 *
 * A session token and an ADB private key are the same kind of thing: a standing proof that this
 * phone may do something to a television, which outlives the run it was granted in and which a
 * plain-text copy in the app's preferences would hand to anybody with a rooted phone. They are
 * sealed by the same key so there is one place that talks to the keystore and one test for it.
 *
 * The key never leaves the keystore and needs no user authentication to use. Requiring a fingerprint
 * to reconnect to a television would be theatre: anybody holding the unlocked phone can start a
 * cast from the app anyway, so the gate would cost a tap and protect nothing.
 *
 * `EncryptedSharedPreferences` would have done this. It is not used: the `security-crypto` library
 * is deprecated, and a deprecated dependency taken to avoid sixty lines is a maintenance problem
 * rather than a security property.
 *
 * None of this is transport security. The cast link the secrets authorise carries video in the
 * clear, and no string in this app describes that link as private, secure or encrypted.
 */
class SecretBox(private val alias: String = DEFAULT_ALIAS) {
    /** The sealed bytes, or `null` when the keystore refused. */
    fun seal(plain: ByteArray): ByteArray? = runCatching {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, secretKey())
        TokenEnvelope.pack(cipher.iv, cipher.doFinal(plain))
    }.getOrNull()

    /** The plain bytes, or `null` when these were not sealed by this phone's key. */
    fun open(sealed: ByteArray): ByteArray? = runCatching {
        val envelope = TokenEnvelope.unpack(sealed) ?: return null
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.DECRYPT_MODE, secretKey(), GCMParameterSpec(GCM_TAG_BITS, envelope.iv))
        cipher.doFinal(envelope.ciphertext)
    }.getOrNull()

    private fun secretKey(): SecretKey {
        val keyStore = KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }
        (keyStore.getEntry(alias, null) as? KeyStore.SecretKeyEntry)?.let { return it.secretKey }

        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, ANDROID_KEYSTORE)
        generator.init(
            KeyGenParameterSpec.Builder(alias, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(KEY_BITS)
                .build(),
        )
        return generator.generateKey()
    }

    private companion object {
        /** The alias the token store has always used, kept so an update does not orphan stored tokens. */
        const val DEFAULT_ALIAS = "flint-mobile-token"
        const val ANDROID_KEYSTORE = "AndroidKeyStore"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val GCM_TAG_BITS = 128
        const val KEY_BITS = 256
    }
}
