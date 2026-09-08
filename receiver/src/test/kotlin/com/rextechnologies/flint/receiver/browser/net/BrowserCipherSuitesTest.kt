package com.rextechnologies.flint.receiver.browser.net

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/**
 * The cipher suites the browser listener will accept.
 *
 * This choice is load-bearing rather than cosmetic. The receiver's key is signing-only, so a suite
 * that needs the server to decrypt cannot work — and when the platform picks one anyway, the
 * handshake fails inside OpenSSL with a message about RSA internals that names nothing useful.
 */
class BrowserCipherSuitesTest {
    @Test
    fun `every preferred suite is forward secret`() {
        // A signing-only key cannot do RSA key transport, and forward secrecy is the property that
        // makes a recorded session useless to someone who later obtains the key.
        for (suite in BrowserCipherSuites.PREFERRED) {
            assertTrue(suite.contains("ECDHE"), "$suite is not an ephemeral exchange")
        }
    }

    @Test
    fun `no preferred suite requires the server to decrypt`() {
        // The classic RSA key-transport suites are named TLS_RSA_WITH_*. One of those in this list
        // reintroduces exactly the handshake failure it exists to avoid.
        for (suite in BrowserCipherSuites.PREFERRED) {
            assertTrue(!suite.startsWith("TLS_RSA_WITH"), "$suite uses RSA key transport")
        }
    }

    @Test
    fun `selection keeps the preference order rather than the platform's`() {
        // The device reports its list in its own order; ours expresses a security preference, and
        // the first match is what gets negotiated.
        val supported = arrayOf(
            "TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA",
            "TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384",
        )

        val selected = BrowserCipherSuites.select(supported)

        assertEquals(
            listOf(
                "TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384",
                "TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA",
            ),
            selected,
        )
    }

    @Test
    fun `selection drops suites the device does not offer`() {
        val supported = arrayOf("TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256", "TLS_RSA_WITH_AES_128_CBC_SHA")

        val selected = BrowserCipherSuites.select(supported)

        assertEquals(listOf("TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256"), selected)
    }

    @Test
    fun `a device offering nothing forward secret selects nothing`() {
        // Empty rather than a fallback. The caller refuses to listen, because offering the browser
        // over a suite this key cannot serve produces a socket that accepts and then fails.
        val selected = BrowserCipherSuites.select(arrayOf("TLS_RSA_WITH_AES_128_CBC_SHA"))

        assertTrue(selected.isEmpty())
    }

    @Test
    fun `a null supported list selects nothing rather than throwing`() {
        assertTrue(BrowserCipherSuites.select(null).isEmpty())
    }

    @Test
    fun `the preference list has no duplicates`() {
        assertEquals(
            BrowserCipherSuites.PREFERRED.size,
            BrowserCipherSuites.PREFERRED.toSet().size,
        )
    }
}
