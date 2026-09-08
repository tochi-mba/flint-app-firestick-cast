package com.rextechnologies.flint.receiver.browser.identity

import java.io.File
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.PrivateKey
import java.security.SecureRandom
import java.security.cert.X509Certificate

/**
 * A receiver identity that survives restarts, stored in the app's private directory.
 *
 * # Why this exists rather than the Android Keystore
 *
 * The Keystore is the better home for a private key and this provider is not a preference — it is a
 * consequence of the hardware Flint targets. On Fire OS 6 (API 25) Conscrypt cannot complete a TLS
 * server handshake using a Keystore-resident RSA key: it fails inside OpenSSL with
 * `RSA routines: internal error`, after the socket is already open, regardless of the digests or
 * purposes the key was created with. The key is genuinely unusable for this job on that platform.
 *
 * So on those devices the key is generated in process and persisted here. It is therefore
 * **exportable by anything that can read the app's private storage**, which is a real reduction in
 * protection compared with a Keystore key, and it is stated plainly rather than glossed: the
 * boundary is Android's per-app storage isolation on a device that has not been rooted.
 *
 * # Why persistence matters beyond the key
 *
 * The identity *is* the thing a person verifies. A provider that generates a fresh key on every
 * launch produces a fresh fingerprint on every launch, so the desktop sees a receiver it has never
 * met each time and asks the user to compare codes again — turning a one-time setup into a chore
 * repeated forever. Persisting it means the comparison happens once and every later connection is
 * recognised silently.
 */
class PersistentReceiverIdentityProvider(
    private val directory: File,
    private val random: SecureRandom = SecureRandom(),
) : ReceiverIdentityProvider {
    override fun obtain(receiverIdentityKey: String): ReceiverIdentity {
        require(receiverIdentityKey.isNotBlank()) { "Receiver identity key must not be blank" }

        val file = File(directory, "$receiverIdentityKey.p12")
        load(file, receiverIdentityKey)?.let { return it }

        val generated = generate(receiverIdentityKey)
        // A failure to save is not a failure to serve: the session works, it simply will not be
        // recognised after a restart. Better a browser that asks to be verified again than one that
        // refuses to start because a file could not be written.
        runCatching { save(file, receiverIdentityKey, generated) }
        return generated
    }

    /** Reads a previously stored identity, or `null` when there is none this provider can use. */
    private fun load(file: File, alias: String): ReceiverIdentity? {
        if (!file.isFile) {
            return null
        }
        return runCatching {
            val store = KeyStore.getInstance(STORE_TYPE)
            file.inputStream().use { input -> store.load(input, STORE_PASSWORD) }
            val certificate = store.getCertificate(alias) as? X509Certificate ?: return@runCatching null
            val privateKey = store.getKey(alias, STORE_PASSWORD) as? PrivateKey ?: return@runCatching null
            ReceiverIdentity(
                receiverIdentityKey = alias,
                certificate = certificate,
                keyPair = KeyPair(certificate.publicKey, privateKey),
                fingerprint = BrowserFingerprint.fromCertificate(certificate),
            )
        }.getOrNull()
    }

    /** Writes the identity so the next launch is recognised rather than re-verified. */
    private fun save(file: File, alias: String, identity: ReceiverIdentity) {
        directory.mkdirs()
        val store = KeyStore.getInstance(STORE_TYPE).apply {
            load(null, STORE_PASSWORD)
            setKeyEntry(alias, identity.keyPair.private, STORE_PASSWORD, arrayOf(identity.certificate))
        }

        // Written beside the target and moved into place, so a process killed mid-write leaves the
        // previous identity intact rather than a truncated file that loads as "no identity" and
        // silently asks every host to verify again.
        val temporary = File(file.parentFile, "${file.name}.tmp")
        temporary.outputStream().use { output -> store.store(output, STORE_PASSWORD) }
        if (!temporary.renameTo(file)) {
            temporary.copyTo(file, overwrite = true)
            temporary.delete()
        }
    }

    /** Creates a fresh key pair and self-signed certificate. */
    private fun generate(alias: String): ReceiverIdentity {
        val keyPair = KeyPairGenerator.getInstance("RSA").apply {
            initialize(KEY_SIZE, random)
        }.generateKeyPair()
        val certificate = InMemoryReceiverIdentityProvider.selfSignedCertificate(alias, keyPair)
        return ReceiverIdentity(
            receiverIdentityKey = alias,
            certificate = certificate,
            keyPair = keyPair,
            fingerprint = BrowserFingerprint.fromCertificate(certificate),
        )
    }

    private companion object {
        const val KEY_SIZE = 2_048

        /**
         * PKCS#12 rather than the platform default.
         *
         * It is understood by both Android and a desktop JVM, so the same code path is exercised by
         * the unit tests and on the television. The platform default is BouncyCastle's BKS on
         * Android and something else everywhere else.
         */
        const val STORE_TYPE = "PKCS12"

        /**
         * The store's password, which is not a secret and is not pretending to be one.
         *
         * PKCS#12 requires a password; there is nowhere on the device to keep one that an attacker
         * with read access to the app's private storage could not also reach. The protection here is
         * Android's per-app isolation, not this constant, and writing it in the open is more honest
         * than deriving it and implying otherwise.
         */
        val STORE_PASSWORD: CharArray = "flint-browser-identity".toCharArray()
    }
}
