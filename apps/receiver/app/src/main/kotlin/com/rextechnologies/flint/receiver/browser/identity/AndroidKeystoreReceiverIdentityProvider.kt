package com.rextechnologies.flint.receiver.browser.identity

import android.os.Build
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.PrivateKey
import java.security.cert.X509Certificate
import javax.security.auth.x500.X500Principal

/**
 * Android Keystore-backed identity, where the platform can actually serve TLS from one.
 *
 * # Why this is conditional
 *
 * A Keystore key is the right home for a private key: it never leaves secure storage. But on
 * Fire OS 6 (API 25) Conscrypt cannot complete a TLS **server** handshake with a Keystore-resident
 * RSA key. It fails inside OpenSSL with `RSA routines: internal error` after the socket is already
 * open, whatever digests, paddings or purposes the key was created with. The key is not misused —
 * it is unusable for this job on that platform.
 *
 * So the Keystore is used from API 28, where it works, and below that the [fallback] provides a
 * persisted in-process identity. That trade is stated where it is made rather than discovered as a
 * handshake failure, and the fallback's own documentation is explicit about what it gives up.
 */
class AndroidKeystoreReceiverIdentityProvider(
    private val fallback: ReceiverIdentityProvider,
    private val apiLevel: Int = Build.VERSION.SDK_INT,
) : ReceiverIdentityProvider {
    override fun obtain(receiverIdentityKey: String): ReceiverIdentity {
        require(receiverIdentityKey.isNotBlank()) { "Receiver identity key must not be blank" }
        if (apiLevel < MIN_TLS_CAPABLE_API) {
            return fallback.obtain(receiverIdentityKey)
        }
        return try {
            obtainFromKeystore(receiverIdentityKey)
        } catch (_: Throwable) {
            fallback.obtain(receiverIdentityKey)
        }
    }

    private fun obtainFromKeystore(alias: String): ReceiverIdentity {
        val keyStore = KeyStore.getInstance(ANDROID_KEY_STORE).apply { load(null) }
        if (!keyStore.containsAlias(alias)) {
            createKeystoreEntry(alias)
        }
        val certificate = keyStore.getCertificate(alias) as? X509Certificate
            ?: error("Android Keystore did not return an X.509 certificate")
        val privateKey = keyStore.getKey(alias, null) as? PrivateKey
            ?: error("Android Keystore did not return a private key")
        val publicKey = certificate.publicKey
        return ReceiverIdentity(
            receiverIdentityKey = alias,
            certificate = certificate,
            keyPair = KeyPair(publicKey, privateKey),
            fingerprint = BrowserFingerprint.fromCertificate(certificate),
            // Recorded so the TLS listener uses the key where it lives. This key cannot be
            // exported, and a listener that tries to copy it into its own keystore fails.
            androidKeyStoreAlias = alias,
        )
    }

    private fun createKeystoreEntry(alias: String) {
        val generator = KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_RSA, ANDROID_KEY_STORE)
        val purposes = KeyProperties.PURPOSE_SIGN or KeyProperties.PURPOSE_VERIFY
        val builder = KeyGenParameterSpec.Builder(alias, purposes)
            // `DIGEST_NONE` is the one that matters, and it is not obvious. TLS 1.2 signs its
            // key-exchange struct with a *raw* PKCS#1 signature over a digest the protocol has
            // already computed, so a key restricted to SHA-256 refuses the operation. Conscrypt
            // reports that refusal as `RSA routines: internal error` from inside OpenSSL, with
            // nothing about digests, purposes or keys — and the handshake dies after the socket is
            // open. SHA-1 and SHA-256 are kept alongside it for ordinary signing.
            .setDigests(
                KeyProperties.DIGEST_NONE,
                KeyProperties.DIGEST_SHA1,
                KeyProperties.DIGEST_SHA256,
            )
            .setSignaturePaddings(KeyProperties.SIGNATURE_PADDING_RSA_PKCS1)
            .setKeySize(2_048)
            .setCertificateSubject(X500Principal("CN=$alias"))
            .setCertificateNotBefore(java.util.Date(System.currentTimeMillis() - 60_000))
            .setCertificateNotAfter(java.util.Date(System.currentTimeMillis() + 365L * 24 * 60 * 60 * 1_000))
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            builder.setIsStrongBoxBacked(false)
        }
        generator.initialize(builder.build())
        generator.generateKeyPair()
    }

    private companion object {
        const val ANDROID_KEY_STORE = "AndroidKeyStore"

        /**
         * The first API level whose Conscrypt can serve TLS from a Keystore key.
         *
         * Below this the handshake fails in a way no configuration fixes, so the Keystore is not
         * attempted at all rather than tried and silently abandoned.
         */
        const val MIN_TLS_CAPABLE_API = 28
    }
}
