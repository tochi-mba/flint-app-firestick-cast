package com.rextechnologies.flint.receiver.browser.identity

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNotEquals
import kotlin.test.assertTrue

class ReceiverIdentityProviderTest {
    @Test
    fun `obtain creates a stable fingerprint for the same identity key`() {
        val provider = InMemoryReceiverIdentityProvider()

        val first = provider.obtain("receiver-a")
        val second = provider.obtain("receiver-a")

        assertEquals(first.fingerprint.fullPin, second.fingerprint.fullPin)
        assertEquals(first.fingerprint.displayCode, second.fingerprint.displayCode)
        assertTrue(DISPLAY_CODE.matches(first.fingerprint.displayCode))
        assertTrue(first.fingerprint.fullPin.startsWith("sha256/"))
    }

    @Test
    fun `different identity keys produce different fingerprints`() {
        val provider = InMemoryReceiverIdentityProvider()

        val left = provider.obtain("receiver-a")
        val right = provider.obtain("receiver-b")

        assertNotEquals(left.fingerprint.fullPin, right.fingerprint.fullPin)
    }

    @Test
    fun `blank identity keys are rejected`() {
        assertFailsWith<IllegalArgumentException> {
            InMemoryReceiverIdentityProvider().obtain(" ")
        }
    }

    @Test
    fun `fingerprint toString never prints the full pin`() {
        val identity = InMemoryReceiverIdentityProvider().obtain()
        val rendered = identity.fingerprint.toString()
        assertTrue(rendered.contains(identity.fingerprint.displayCode))
        assertTrue(!rendered.contains(identity.fingerprint.fullPin))
    }

    private companion object {
        val DISPLAY_CODE = Regex("[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}")
    }
}
