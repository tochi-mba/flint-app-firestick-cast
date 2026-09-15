package com.rextechnologies.flint.receiver.browser

/** What someone typed turned out to be. */
sealed interface ResolvedQuery {
    /** An address to open. Still subject to [BrowserUrlPolicy]. */
    data class Navigate(val url: String) : ResolvedQuery

    /** A phrase to look up, and the address that looks it up. */
    data class Search(val terms: String, val url: String) : ResolvedQuery

    /** Nothing usable was typed. */
    data object Empty : ResolvedQuery
}

/**
 * Address or search — the decision every omnibox makes, and the one this browser could not.
 *
 * [BrowserUrlPolicy] rejects a bare word twice: once for not being absolute, once for having no dot.
 * That is right for a security policy and useless as a browser, which is why the television had no
 * way to look anything up. This sits above the policy and never replaces it: whatever comes out is
 * still evaluated by the same unchanged https-only rules.
 *
 * The order of the rules matters and mirrors the host's `BrowserAddressBarResolver`, so typing the
 * same thing on the television and on the desktop goes to the same place.
 */
class BrowserQueryResolver {
    fun resolve(raw: String, engine: BrowserSearchEngine): ResolvedQuery {
        val trimmed = raw.trim()
        if (trimmed.isEmpty()) {
            return ResolvedQuery.Empty
        }

        // Anything with whitespace inside is a phrase, not an address. Checked first because it is
        // the cheapest certain answer.
        if (trimmed.any(Char::isWhitespace)) {
            return search(trimmed, engine)
        }

        val scheme = trimmed.substringBefore("://", missingDelimiterValue = "")
            .takeIf { it.isNotEmpty() && trimmed.contains("://") }
            ?.lowercase()

        return when {
            scheme == "https" -> ResolvedQuery.Navigate(trimmed)

            // Upgraded rather than refused. A typed or pasted http address is an ordinary thing to
            // do, and the policy would otherwise reject it as if the browser were broken.
            scheme == "http" -> ResolvedQuery.Navigate("https://" + trimmed.removePrefix("http://"))

            // A scheme this browser will not run. Searched for, because that is the safe reading of
            // someone pasting one, and because dropping it silently looks like a fault.
            scheme != null -> search(trimmed, engine)

            trimmed.startsWith("javascript:", ignoreCase = true) ||
                trimmed.startsWith("data:", ignoreCase = true) ||
                trimmed.startsWith("file:", ignoreCase = true) -> search(trimmed, engine)

            looksLikeAddress(trimmed) -> ResolvedQuery.Navigate("https://$trimmed")

            else -> search(trimmed, engine)
        }
    }

    private fun search(terms: String, engine: BrowserSearchEngine): ResolvedQuery.Search =
        ResolvedQuery.Search(terms = terms, url = engine.queryUrl(terms))

    /**
     * Whether a bare token is a hostname rather than a phrase.
     *
     * Requires a dot with something either side of it. "weather" is a search; "example.test" is a
     * site. A trailing path is allowed, and a lone dot or slash is neither.
     */
    private fun looksLikeAddress(value: String): Boolean {
        val host = value.substringBefore('/').substringBefore('?').substringBefore('#')
        if (host.isEmpty()) return false
        val dot = host.indexOf('.')
        if (dot <= 0 || dot == host.length - 1) return false
        // A label made only of dots is not a host, however many there are.
        return host.split('.').none(String::isEmpty)
    }
}
