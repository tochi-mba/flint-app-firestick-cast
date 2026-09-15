package com.rextechnologies.flint.receiver.browser.identity

import org.bouncycastle.asn1.x509.BasicConstraints
import org.bouncycastle.asn1.x509.Extension
import org.bouncycastle.asn1.x509.KeyUsage
import org.bouncycastle.cert.jcajce.JcaX509CertificateConverter
import org.bouncycastle.cert.jcajce.JcaX509v3CertificateBuilder
import org.bouncycastle.operator.jcajce.JcaContentSignerBuilder
import java.math.BigInteger
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.SecureRandom
import java.security.cert.X509Certificate
import java.util.Date
import java.util.concurrent.ConcurrentHashMap
import javax.security.auth.x500.X500Principal

/**
 * Process-local identity provider used by JVM tests and as a Keystore fallback.
 *
 * It never logs certificate or private-key material. The same [receiverIdentityKey] always returns
 * the same fingerprint within one process.
 */
class InMemoryReceiverIdentityProvider(
    private val random: SecureRandom = SecureRandom(),
) : ReceiverIdentityProvider {
    private val identities = ConcurrentHashMap<String, ReceiverIdentity>()

    override fun obtain(receiverIdentityKey: String): ReceiverIdentity {
        require(receiverIdentityKey.isNotBlank()) { "Receiver identity key must not be blank" }
        return identities.computeIfAbsent(receiverIdentityKey) { key ->
            val keyPair = KeyPairGenerator.getInstance("RSA").apply {
                initialize(2_048, random)
            }.generateKeyPair()
            val certificate = selfSignedCertificate(key, keyPair)
            ReceiverIdentity(
                receiverIdentityKey = key,
                certificate = certificate,
                keyPair = keyPair,
                fingerprint = BrowserFingerprint.fromCertificate(certificate),
            )
        }
    }

    companion object {
        fun selfSignedCertificate(commonName: String, keyPair: KeyPair): X509Certificate {
            val now = System.currentTimeMillis()
            val notBefore = Date(now - 60_000)
            val notAfter = Date(now + 365L * 24 * 60 * 60 * 1_000)
            val subject = X500Principal("CN=$commonName")
            val serial = BigInteger(128, SecureRandom())
            val builder = JcaX509v3CertificateBuilder(
                subject,
                serial,
                notBefore,
                notAfter,
                subject,
                keyPair.public,
            )
            builder.addExtension(Extension.basicConstraints, true, BasicConstraints(false))
            builder.addExtension(
                Extension.keyUsage,
                true,
                KeyUsage(KeyUsage.digitalSignature or KeyUsage.keyEncipherment),
            )
            val signer = JcaContentSignerBuilder("SHA256WithRSA").build(keyPair.private)
            return JcaX509CertificateConverter().getCertificate(builder.build(signer))
        }
    }
}
