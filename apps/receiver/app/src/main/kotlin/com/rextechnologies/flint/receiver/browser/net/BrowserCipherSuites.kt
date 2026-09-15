package com.rextechnologies.flint.receiver.browser.net

/**
 * Chooses the TLS cipher suites the browser listener will accept.
 *
 * # Why this is not left to the platform
 *
 * The receiver's private key lives in the Android Keystore and is generated for **signing only**.
 * That is deliberate — it is what keeps the key non-exportable — but it rules out the classic RSA
 * key-transport suites, where the server has to *decrypt* the client's premaster secret. Left to
 * pick for itself, Conscrypt on Fire OS negotiates exactly such a suite and then fails inside
 * OpenSSL with `RSA routines: internal error`, which says nothing about key purposes and aborts the
 * handshake after the socket is already open.
 *
 * Ephemeral Diffie-Hellman suites use the key only to *sign* the exchange, which the key permits.
 * They are also the better choice on their own merits: a session recorded today cannot be decrypted
 * later by someone who obtains the key, which key transport does not give.
 *
 * So the preference list is stated, and intersected with whatever the device actually offers.
 */
internal object BrowserCipherSuites {
    /**
     * Forward-secret suites, strongest first.
     *
     * Every one is ephemeral Diffie-Hellman with an RSA-signed exchange, which is what a
     * signing-only Keystore key can do. GCM before CBC because the CBC suites carry the older MAC
     * construction; both are kept because Fire OS 6 devices do not all offer the same set.
     */
    val PREFERRED: List<String> = listOf(
        "TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384",
        "TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256",
        "TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA384",
        "TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA256",
        "TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA",
        "TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA",
    )

    /**
     * The preferred suites this device actually supports, in preference order.
     *
     * Returns an empty list when none overlap, which the caller must treat as a refusal to listen
     * rather than as a reason to fall back: accepting whatever the platform would have chosen is
     * how the handshake failure above happens, and on a device with no forward-secret suite the
     * honest answer is that the browser cannot be offered securely.
     */
    fun select(supported: Array<String>?): List<String> {
        val available = supported?.toSet() ?: return emptyList()
        return PREFERRED.filter(available::contains)
    }
}
