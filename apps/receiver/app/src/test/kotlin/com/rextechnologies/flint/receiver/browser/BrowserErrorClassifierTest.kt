package com.rextechnologies.flint.receiver.browser

import android.webkit.WebViewClient
import com.rextechnologies.flint.receiver.toViewerSentence
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotEquals
import kotlin.test.assertTrue

/**
 * Why a page did not load.
 *
 * Every main-frame error used to become `DRIVER_FAILURE`, so a mistyped address, a dropped Wi-Fi
 * connection and a server outage all told the viewer "the browser could not start" — which is both
 * wrong and unactionable. A television viewer cannot open a console; the classification is the only
 * diagnosis they get.
 */
class BrowserErrorClassifierTest {
    private val classifier = BrowserErrorClassifier()

    @Test
    fun `a name that does not resolve is reported as a missing site, not a broken browser`() {
        assertEquals(
            BrowserFailure.SITE_NOT_FOUND,
            classifier.fromResourceError(WebViewClient.ERROR_HOST_LOOKUP),
        )
    }

    @Test
    fun `losing the network is told apart from the site being at fault`() {
        val connectionCodes = listOf(
            WebViewClient.ERROR_CONNECT,
            WebViewClient.ERROR_TIMEOUT,
            WebViewClient.ERROR_IO,
            WebViewClient.ERROR_PROXY_AUTHENTICATION,
        )

        connectionCodes.forEach { code ->
            assertEquals(BrowserFailure.NO_CONNECTION, classifier.fromResourceError(code), "code $code")
        }
    }

    @Test
    fun `a certificate problem keeps its own classification`() {
        assertEquals(
            BrowserFailure.CERTIFICATE_REJECTED,
            classifier.fromResourceError(WebViewClient.ERROR_FAILED_SSL_HANDSHAKE),
        )
    }

    @Test
    fun `an unsupported scheme reads as a blocked address rather than a fault`() {
        assertEquals(
            BrowserFailure.BLOCKED_URL,
            classifier.fromResourceError(WebViewClient.ERROR_UNSUPPORTED_SCHEME),
        )
    }

    @Test
    fun `server responses are the site's problem and say so`() {
        assertEquals(BrowserFailure.SITE_ERROR, classifier.fromHttpStatus(404))
        assertEquals(BrowserFailure.SITE_ERROR, classifier.fromHttpStatus(500))
        assertEquals(BrowserFailure.SITE_ERROR, classifier.fromHttpStatus(503))
    }

    @Test
    fun `a successful status is not a failure at all`() {
        assertEquals(null, classifier.fromHttpStatus(200))
        assertEquals(null, classifier.fromHttpStatus(304))
    }

    @Test
    fun `an unrecognised code still produces something a viewer can act on`() {
        // The floor: never a raw number, never silence. WebView adds codes over time and an
        // unmapped one must still leave the viewer with a next step.
        val classified = classifier.fromResourceError(Int.MIN_VALUE)

        assertNotEquals(BrowserFailure.CERTIFICATE_REJECTED, classified)
        assertTrue(classified.toViewerSentence().isNotBlank())
    }

    @Test
    fun `every failure a viewer can reach has a sentence that is not a code`() {
        BrowserFailure.entries.forEach { failure ->
            val sentence = failure.toViewerSentence()
            assertTrue(sentence.isNotBlank(), "$failure has no sentence")
            assertTrue(sentence.first().isUpperCase(), "$failure does not read as a sentence")
            assertTrue(sentence.none { it == '_' }, "$failure leaks an enum name at the viewer")
        }
    }
}
