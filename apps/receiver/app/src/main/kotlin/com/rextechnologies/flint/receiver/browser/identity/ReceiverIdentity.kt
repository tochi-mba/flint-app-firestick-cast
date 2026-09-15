package com.rextechnologies.flint.receiver.browser.identity

import java.security.KeyPair
import java.security.MessageDigest
import java.security.cert.X509Certificate
import java.util.Locale

/**
 * Full SPKI SHA-256 pin plus the short TV/Windows comparison code.
 *
 * The display code helps a person compare two physical screens. Only [fullPin] participates in
 * trust decisions.
 */
data class BrowserFingerprint(
    val fullPin: String,
    val displayCode: String,
) {
    init {
        require(fullPin.startsWith(PREFIX)) { "Browser pin must use the sha256/ prefix" }
        require(DISPLAY_CODE.matches(displayCode)) { "Browser display code has an invalid format" }
    }

    override fun toString(): String = "Browser fingerprint $displayCode"

    companion object {
        private const val PREFIX = "sha256/"
        private val DISPLAY_CODE = Regex("[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}")

        fun fromSubjectPublicKeyInfo(spki: ByteArray): BrowserFingerprint {
            require(spki.isNotEmpty()) { "SubjectPublicKeyInfo must not be empty" }
            val hash = MessageDigest.getInstance("SHA-256").digest(spki)
            // Neither platform encoder is usable here; see [Base64Pin] for why. In short,
            // `java.util.Base64` is API 26 and this receiver runs on API 25 devices, where it threw
            // at runtime and silently disabled the whole browser feature.
            val fullPin = PREFIX + Base64Pin.encode(hash)
            val hex = hash.copyOfRange(0, 6).joinToString("") { byte ->
                String.format(Locale.ROOT, "%02X", byte)
            }
            val displayCode = "${hex.substring(0, 4)}-${hex.substring(4, 8)}-${hex.substring(8, 12)}"
            return BrowserFingerprint(fullPin, displayCode)
        }

        fun fromCertificate(certificate: X509Certificate): BrowserFingerprint =
            fromSubjectPublicKeyInfo(certificate.publicKey.encoded)
    }
}

/**
 * Persistent receiver identity used only by the dedicated browser TLS listener.
 *
 * @property androidKeyStoreAlias the alias under which the private key lives in the Android
 *   Keystore, or `null` when the key is an ordinary in-process one.
 *
 *   This distinction is not cosmetic. A Keystore-backed private key is deliberately
 *   **non-exportable**: `getEncoded()` returns null, and copying it into a software keystore — the
 *   obvious way to build an [javax.net.ssl.SSLContext] — fails deep inside the provider with a
 *   `KeyStoreException` wrapping a `NullPointerException` that names nothing. The key has to be
 *   used *where it lives*, so the listener needs to know where that is.
 */
data class ReceiverIdentity(
    val receiverIdentityKey: String,
    val certificate: X509Certificate,
    val keyPair: KeyPair,
    val fingerprint: BrowserFingerprint,
    val androidKeyStoreAlias: String? = null,
) {
    override fun toString(): String =
        "ReceiverIdentity(key=$receiverIdentityKey, fingerprint=${fingerprint.displayCode})"
}

/** Creates or reopens the receiver's browser TLS identity without exporting private key material. */
interface ReceiverIdentityProvider {
    fun obtain(receiverIdentityKey: String = DEFAULT_IDENTITY_KEY): ReceiverIdentity

    companion object {
        /**
         * The Keystore alias the receiver's browser identity lives under.
         *
         * Version-suffixed on purpose. A Keystore entry is created once and reused forever, so a
         * key generated under a spec that turns out to be unusable — as `v1` was, being restricted
         * to SHA-256 and therefore unable to sign a TLS handshake — would otherwise be picked up
         * again on every launch, and the fix would never take effect on a device that had already
         * run the broken build. Changing this string retires the old key and generates a correct
         * one; the receiver's fingerprint changes with it, so hosts verify again once.
         */
        const val DEFAULT_IDENTITY_KEY: String = "flint-browser-receiver-v2"
    }
}
