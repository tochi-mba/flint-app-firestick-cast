package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertTrue

class BrowserUrlPolicyTest {
    private val policy = BrowserUrlPolicy()

    @Test
    fun `accepts canonical public HTTPS URL and redacts query and fragment from display`() {
        val result = assertIs<BrowserUrlResult.Accepted>(
            policy.evaluate("  HTTPS://ExAmPlE.com:443/a/../path?token=secret#private  "),
        )

        assertEquals("https://example.com/path?token=secret#private", result.url.canonicalUrl)
        assertEquals("https://example.com/path", result.url.displayUrl)
        assertTrue(!result.url.displayUrl.contains("secret"))
    }

    @Test
    fun `accepts unicode domain through deterministic ASCII IDN canonicalization`() {
        val result = assertIs<BrowserUrlResult.Accepted>(policy.evaluate("https://bücher.example/a"))

        assertEquals("https://xn--bcher-kva.example/a", result.url.canonicalUrl)
        assertEquals("https://xn--bcher-kva.example/a", result.url.displayUrl)
    }

    @Test
    fun `rejects all non HTTPS and relative addresses without coercion`() {
        val values = listOf(
            "", "example.com", "/relative", "//example.com", "http://example.com",
            "file:///tmp/a", "content://provider/a", "data:text/plain,x", "javascript:alert(1)",
            "intent://example", "mailto:person@example.com", "tel:+441234",
        )

        values.forEach { value ->
            assertTrue(policy.evaluate(value) is BrowserUrlResult.Rejected, "Expected rejection for $value")
        }
    }

    @Test
    fun `rejects user info controls malformed escapes and invalid ports`() {
        val values = listOf(
            "https://alice:secret@example.com/",
            "https://example.com/%zz",
            "https://example.com/hello\u0000world",
            "https://example.com:0/",
            "https://example.com:65536/",
            "https://example.com:port/",
        )

        values.forEach { value ->
            assertTrue(policy.evaluate(value) is BrowserUrlResult.Rejected, "Expected rejection for $value")
        }
    }

    @Test
    fun `rejects IP literals and local naming forms without DNS lookups`() {
        val values = listOf(
            "https://127.0.0.1/", "https://127.1/", "https://2130706433/",
            "https://0x7f000001/", "https://[::1]/", "https://localhost/",
            "https://app.local/", "https://machine.lan/", "https://server.internal/",
        )

        values.forEach { value ->
            val result = assertIs<BrowserUrlResult.Rejected>(policy.evaluate(value))
            assertEquals(BrowserUrlRejection.BLOCKED_LOCAL_RESOURCE, result.reason)
        }
    }

    @Test
    fun `enforces UTF8 input byte bound before parsing`() {
        val exact = "https://example.com/" + "a".repeat(BrowserUrlPolicy.MAX_INPUT_BYTES - "https://example.com/".length)
        assertIs<BrowserUrlResult.Accepted>(policy.evaluate(exact))

        val tooLong = exact + "a"
        val result = assertIs<BrowserUrlResult.Rejected>(policy.evaluate(tooLong))
        assertEquals(BrowserUrlRejection.INPUT_TOO_LONG, result.reason)
    }

    @Test
    fun `appassets is a debug-only exact asset fixture route`() {
        val fixture = "https://appassets.androidplatform.net/assets/flint-browser-fixture.html"
        val denied = assertIs<BrowserUrlResult.Rejected>(policy.evaluate(fixture))
        assertEquals(BrowserUrlRejection.BLOCKED_LOCAL_RESOURCE, denied.reason)

        val debug = BrowserUrlPolicy(allowDebugAppAssets = true)
        val accepted = assertIs<BrowserUrlResult.Accepted>(debug.evaluate(fixture))
        assertTrue(accepted.url.isDebugFixture)
        assertTrue(debug.evaluate("https://appassets.androidplatform.net/not-assets/a") is BrowserUrlResult.Rejected)
        assertTrue(debug.evaluate("$fixture?not=allowed") is BrowserUrlResult.Rejected)
    }
}
