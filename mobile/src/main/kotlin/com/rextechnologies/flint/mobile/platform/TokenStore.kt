package com.rextechnologies.flint.mobile.platform

import android.content.Context
import android.content.SharedPreferences
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import com.rextechnologies.flint.protocol.http.SessionToken
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * Where a session token lives between runs.
 *
 * Two things about the token decide the shape of this.
 *
 * It is worth encrypting at rest. A token is a standing proof that this phone is allowed to cast to
 * that television, and it outlives the session it was granted in, so a plain-text copy in the app's
 * preferences is a copy anybody with a file manager and a rooted phone can lift.
 *
 * And it is authorisation, not encryption. Encrypting it here protects the stored copy and nothing
 * else: the cast link it authorises carries video in the clear. No string in this app describes that
 * link as private, secure or encrypted, and this class is not a reason to start.
 *
 * `EncryptedSharedPreferences` would have done the storage half. It is not used: the
 * `androidx.security:security-crypto` library is deprecated, and taking a deprecated dependency to
 * avoid eighty lines of keystore work buys a maintenance problem rather than a security property.
 */
class TokenStore(context: Context) {
    private val preferences: SharedPreferences =
        context.applicationContext.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    /** The token granted by this receiver, or `null` when there is none or it no longer decrypts. */
    fun tokenFor(receiverAddress: String): SessionToken? {
        val stored = preferences.getString(keyFor(receiverAddress), null) ?: return null
        val plain = decrypt(stored) ?: return null
        return SessionToken.parseOrNull(plain)
    }

    fun remember(receiverAddress: String, token: SessionToken) {
        val encrypted = encrypt(token.toString()) ?: return
        preferences.edit().putString(keyFor(receiverAddress), encrypted).apply()
    }

    fun forget(receiverAddress: String) {
        preferences.edit().remove(keyFor(receiverAddress)).apply()
    }

    /** Used by the setting that unpairs everything, which exists so a lent phone can be handed back. */
    fun forgetEverything() {
        preferences.edit().clear().apply()
    }

    private fun keyFor(receiverAddress: String): String = "token:$receiverAddress"

    private fun encrypt(value: String): String? = runCatching {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, secretKey())
        val ciphertext = cipher.doFinal(value.toByteArray(Charsets.US_ASCII))
        // The initialisation vector is not a secret and has to survive alongside the ciphertext.
        // Length-prefixed rather than fixed at twelve bytes, because a provider is entitled to pick
        // a different nonce size and a hard-coded split would fail on the phone that did.
        val iv = cipher.iv
        val packed = ByteArray(1 + iv.size + ciphertext.size)
        packed[0] = iv.size.toByte()
        iv.copyInto(packed, 1)
        ciphertext.copyInto(packed, 1 + iv.size)
        Base64.encodeToString(packed, Base64.NO_WRAP)
    }.getOrNull()

    private fun decrypt(stored: String): String? = runCatching {
        val packed = Base64.decode(stored, Base64.NO_WRAP)
        if (packed.isEmpty()) return null
        val ivLength = packed[0].toInt() and 0xff
        if (ivLength <= 0 || packed.size <= 1 + ivLength) return null
        val iv = packed.copyOfRange(1, 1 + ivLength)
        val ciphertext = packed.copyOfRange(1 + ivLength, packed.size)
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.DECRYPT_MODE, secretKey(), GCMParameterSpec(GCM_TAG_BITS, iv))
        String(cipher.doFinal(ciphertext), Charsets.US_ASCII)
    }.getOrNull()

    /**
     * The key, created once and never leaving the keystore.
     *
     * No user authentication is required to use it. Requiring a fingerprint to reconnect to a
     * television would be security theatre: anybody holding the unlocked phone can start a cast from
     * the app anyway, so the gate would cost a tap and protect nothing.
     */
    private fun secretKey(): SecretKey {
        val keyStore = KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }
        (keyStore.getEntry(KEY_ALIAS, null) as? KeyStore.SecretKeyEntry)?.let { return it.secretKey }

        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, ANDROID_KEYSTORE)
        generator.init(
            KeyGenParameterSpec.Builder(
                KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
            )
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(KEY_BITS)
                .build(),
        )
        return generator.generateKey()
    }

    private companion object {
        const val PREFERENCES_NAME = "flint-mobile-tokens"
        const val ANDROID_KEYSTORE = "AndroidKeyStore"
        const val KEY_ALIAS = "flint-mobile-token"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val GCM_TAG_BITS = 128
        const val KEY_BITS = 256
    }
}
