package com.rextechnologies.flint.receiver.browser

import android.webkit.WebViewClient

/**
 * Turns a WebView error code into something a viewer across a room can act on.
 *
 * Pure and exhaustive on purpose. The driver previously mapped every main-frame error to
 * `DRIVER_FAILURE`, so "you typed the address wrong", "the Wi-Fi dropped" and "that server is down"
 * all rendered as *the browser could not start* — three different next steps collapsed into one
 * that was wrong for all of them.
 *
 * The codes are compile-time constants, so this classifies without a device.
 */
class BrowserErrorClassifier {
    /** Classifies a main-frame [WebViewClient] resource error. */
    fun fromResourceError(errorCode: Int): BrowserFailure = when (errorCode) {
        WebViewClient.ERROR_HOST_LOOKUP,
        WebViewClient.ERROR_BAD_URL,
        -> BrowserFailure.SITE_NOT_FOUND

        WebViewClient.ERROR_CONNECT,
        WebViewClient.ERROR_TIMEOUT,
        WebViewClient.ERROR_IO,
        WebViewClient.ERROR_PROXY_AUTHENTICATION,
        WebViewClient.ERROR_REDIRECT_LOOP,
        -> BrowserFailure.NO_CONNECTION

        WebViewClient.ERROR_FAILED_SSL_HANDSHAKE -> BrowserFailure.CERTIFICATE_REJECTED

        WebViewClient.ERROR_UNSUPPORTED_SCHEME,
        WebViewClient.ERROR_UNSUPPORTED_AUTH_SCHEME,
        -> BrowserFailure.BLOCKED_URL

        WebViewClient.ERROR_AUTHENTICATION,
        WebViewClient.ERROR_FILE,
        WebViewClient.ERROR_FILE_NOT_FOUND,
        WebViewClient.ERROR_TOO_MANY_REQUESTS,
        -> BrowserFailure.SITE_ERROR

        // WebView gains codes between releases. An unmapped one is still a page that did not load,
        // and "could not be reached" is true of all of them and tells the viewer to try again.
        else -> BrowserFailure.NO_CONNECTION
    }

    /**
     * Classifies a main-frame HTTP status.
     *
     * Null for anything that is not an error, because a 2xx or 3xx reaching here is a redirect or a
     * subresource and must not paint an error over a page that loaded.
     */
    fun fromHttpStatus(statusCode: Int): BrowserFailure? =
        if (statusCode >= 400) BrowserFailure.SITE_ERROR else null
}
