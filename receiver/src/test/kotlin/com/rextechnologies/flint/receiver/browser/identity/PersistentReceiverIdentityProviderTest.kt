package com.rextechnologies.flint.receiver.browser.identity

import java.io.File
import kotlin.test.AfterTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotEquals
import kotlin.test.assertTrue

/**
 * The identity that has to survive a restart.
 *
 * The fingerprint is what a person compares between two screens when they set the browser up. If it
 * changes on every launch, that one-time comparison becomes a chore repeated forever, and users
 * learn to accept whatever code appears — which is worse than no verification at all, because it
 * looks like security.
 */
class PersistentReceiverIdentityProviderTest {
    private val directory = File(
        System.getProperty("java.io.tmpdir"),
        "flint-identity-${System.nanoTime()}",
    )

    @AfterTest
    fun cleanUp() {
        directory.deleteRecursively()
    }

    @Test
    fun `the same identity comes back after a restart`() {
        // A fresh provider stands in for a relaunched app: nothing is shared but the directory.
        val first = PersistentReceiverIdentityProvider(directory).obtain("receiver")
        val second = PersistentReceiverIdentityProvider(directory).obtain("receiver")

        assertEquals(first.fingerprint.fullPin, second.fingerprint.fullPin)
        assertEquals(first.fingerprint.displayCode, second.fingerprint.displayCode)
    }

    @Test
    fun `the stored certificate and key still work together after reloading`() {
        // A store that round-trips the certificate but loses the key produces an identity that
        // looks right and cannot complete a handshake.
        val first = PersistentReceiverIdentityProvider(directory).obtain("receiver")
        val second = PersistentReceiverIdentityProvider(directory).obtain("receiver")

        assertEquals(first.certificate, second.certificate)
        assertEquals(first.keyPair.private, second.keyPair.private)
        assertEquals(first.certificate.publicKey, second.keyPair.public)
    }

    @Test
    fun `different identity keys do not share a fingerprint`() {
        val provider = PersistentReceiverIdentityProvider(directory)

        val one = provider.obtain("receiver-one")
        val two = provider.obtain("receiver-two")

        assertNotEquals(one.fingerprint.fullPin, two.fingerprint.fullPin)
    }

    @Test
    fun `a first run creates the directory rather than failing`() {
        // The app's private storage exists, but a subdirectory for this need may not.
        val nested = File(directory, "nested/deeper")

        val identity = PersistentReceiverIdentityProvider(nested).obtain("receiver")

        assertTrue(identity.fingerprint.fullPin.startsWith("sha256/"))
    }

    @Test
    fun `a corrupt store is replaced rather than left blocking the browser`() {
        // Half-written files happen: a process killed mid-save, a full disk. A receiver that
        // refuses to start its browser forever because of one bad file is worse than one that
        // regenerates and asks to be verified again.
        val provider = PersistentReceiverIdentityProvider(directory)
        provider.obtain("receiver")
        File(directory, "receiver.p12").writeText("not a keystore")

        val recovered = PersistentReceiverIdentityProvider(directory).obtain("receiver")

        assertTrue(recovered.fingerprint.fullPin.startsWith("sha256/"))
    }

    @Test
    fun `an identity regenerated after corruption persists in its turn`() {
        // Recovery has to actually recover: if the replacement is not written, every later launch
        // regenerates too and the fingerprint never settles.
        val provider = PersistentReceiverIdentityProvider(directory)
        provider.obtain("receiver")
        File(directory, "receiver.p12").writeText("not a keystore")

        val recovered = PersistentReceiverIdentityProvider(directory).obtain("receiver")
        val afterRestart = PersistentReceiverIdentityProvider(directory).obtain("receiver")

        assertEquals(recovered.fingerprint.fullPin, afterRestart.fingerprint.fullPin)
    }

    @Test
    fun `the identity is not backed by the android keystore`() {
        // This provider exists precisely because the Keystore cannot serve TLS on the devices it
        // runs on. Claiming a Keystore alias would send the listener down a path that fails.
        val identity = PersistentReceiverIdentityProvider(directory).obtain("receiver")

        assertEquals(null, identity.androidKeyStoreAlias)
    }

    @Test
    fun `a blank identity key is refused`() {
        val provider = PersistentReceiverIdentityProvider(directory)

        val failure = runCatching { provider.obtain("  ") }.exceptionOrNull()

        assertTrue(failure is IllegalArgumentException)
    }
}
