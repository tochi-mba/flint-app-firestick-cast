package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertTrue

/**
 * Turning what someone typed into something the browser can open.
 *
 * The television had no way to search at all: [BrowserUrlPolicy] rejects a bare word twice over,
 * once for not being absolute and again for having no dot. That is correct for a policy and useless
 * as a browser, so this layer sits above it and decides address-or-search first.
 *
 * Everything it produces still goes through the unchanged https-only policy. Search is not a way
 * around the security profile.
 */
class BrowserQueryResolverTest {
    private val resolver = BrowserQueryResolver()
    private val policy = BrowserUrlPolicy()

    @Test
    fun `a complete address is opened as typed`() {
        val resolved = resolver.resolve("https://example.test/news", BrowserSearchEngine.DUCKDUCKGO)

        val navigate = assertIs<ResolvedQuery.Navigate>(resolved)
        assertEquals("https://example.test/news", navigate.url)
    }

    @Test
    fun `a bare host becomes an https address rather than a search`() {
        val resolved = resolver.resolve("example.test", BrowserSearchEngine.DUCKDUCKGO)

        assertEquals("https://example.test", assertIs<ResolvedQuery.Navigate>(resolved).url)
    }

    @Test
    fun `a host with a path is still an address`() {
        val resolved = resolver.resolve("example.test/some/page", BrowserSearchEngine.DUCKDUCKGO)

        assertEquals("https://example.test/some/page", assertIs<ResolvedQuery.Navigate>(resolved).url)
    }

    @Test
    fun `an insecure address is upgraded rather than refused`() {
        // The policy would reject http outright. Upgrading first is what makes an old bookmark or a
        // typed "http://" work instead of reading as a broken browser.
        val resolved = resolver.resolve("http://example.test/a", BrowserSearchEngine.DUCKDUCKGO)

        assertEquals("https://example.test/a", assertIs<ResolvedQuery.Navigate>(resolved).url)
    }

    @Test
    fun `anything with a space is a search`() {
        val resolved = resolver.resolve("cheap flights to lisbon", BrowserSearchEngine.DUCKDUCKGO)

        val search = assertIs<ResolvedQuery.Search>(resolved)
        assertEquals("cheap flights to lisbon", search.terms)
        assertTrue(search.url.startsWith("https://duckduckgo.com/?q="))
        assertTrue(search.url.contains("cheap+flights+to+lisbon"))
    }

    @Test
    fun `a single word with no dot is a search, not a hostname`() {
        val resolved = resolver.resolve("weather", BrowserSearchEngine.DUCKDUCKGO)

        assertIs<ResolvedQuery.Search>(resolved)
    }

    @Test
    fun `search terms are escaped so they cannot alter the query`() {
        val resolved = resolver.resolve("a&b=c d/e", BrowserSearchEngine.DUCKDUCKGO)

        val search = assertIs<ResolvedQuery.Search>(resolved)
        assertTrue(!search.url.substringAfter("?q=").contains("&"), "an ampersand escaped the query")
        assertTrue(search.url.contains("%26"))
        assertTrue(search.url.contains("%3D"))
    }

    @Test
    fun `a dangerous scheme is searched for rather than opened`() {
        // Never navigated to, never silently dropped. Searching is the safe reading of someone
        // pasting something the browser will not run.
        listOf("javascript:alert(1)", "file:///etc/passwd", "intent://evil", "data:text/html,x").forEach {
            assertIs<ResolvedQuery.Search>(
                resolver.resolve(it, BrowserSearchEngine.DUCKDUCKGO),
                "$it was not treated as a search",
            )
        }
    }

    @Test
    fun `nothing typed resolves to nothing`() {
        assertIs<ResolvedQuery.Empty>(resolver.resolve("   ", BrowserSearchEngine.DUCKDUCKGO))
        assertIs<ResolvedQuery.Empty>(resolver.resolve("", BrowserSearchEngine.DUCKDUCKGO))
    }

    @Test
    fun `every engine produces an address the url policy will accept`() {
        // The contract that keeps search inside the security profile rather than beside it.
        BrowserSearchEngine.PRESETS.forEach { engine ->
            val resolved = resolver.resolve("test query", engine)
            val url = when (resolved) {
                is ResolvedQuery.Search -> resolved.url
                is ResolvedQuery.Navigate -> resolved.url
                ResolvedQuery.Empty -> error("${engine.name} produced nothing")
            }
            assertIs<BrowserUrlResult.Accepted>(policy.evaluate(url), "${engine.name} produced $url")
        }
    }

    @Test
    fun `a custom engine template puts the terms where the placeholder is`() {
        val custom = BrowserSearchEngine.custom("https://search.test/find?query={q}&safe=1")!!

        val search = assertIs<ResolvedQuery.Search>(resolver.resolve("hello world", custom))

        assertEquals("https://search.test/find?query=hello+world&safe=1", search.url)
    }

    @Test
    fun `a custom template without a placeholder is refused rather than silently ignored`() {
        val custom = BrowserSearchEngine.custom("https://search.test/find")

        assertEquals(null, custom)
    }

    @Test
    fun `a custom template that is not https is refused`() {
        assertEquals(null, BrowserSearchEngine.custom("http://search.test/find?q={q}"))
    }

    @Test
    fun `an address is trimmed before it is judged`() {
        val resolved = resolver.resolve("  https://example.test/a  ", BrowserSearchEngine.DUCKDUCKGO)

        assertEquals("https://example.test/a", assertIs<ResolvedQuery.Navigate>(resolved).url)
    }

    @Test
    fun `a lone dot or slash is a search rather than a broken address`() {
        listOf(".", "..", "/", "://").forEach {
            assertIs<ResolvedQuery.Search>(resolver.resolve(it, BrowserSearchEngine.DUCKDUCKGO), "$it")
        }
    }
}
