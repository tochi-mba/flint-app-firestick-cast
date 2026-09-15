package com.rextechnologies.flint.receiver.browser

/**
 * Where a typed phrase goes when it is not an address.
 *
 * A template rather than a hardcoded provider, because the right answer differs by person and a
 * television is the worst place to be stuck with someone else's choice. The engine is picked on
 * first use and remembered.
 *
 * Every template is https by construction, so a search cannot become the one navigation that walks
 * around [BrowserUrlPolicy].
 */
class BrowserSearchEngine private constructor(
    val id: String,
    val name: String,
    val template: String,
) {
    /** The address that searches for [terms]. */
    fun queryUrl(terms: String): String = template.replace(PLACEHOLDER, encode(terms))

    override fun equals(other: Any?): Boolean = other is BrowserSearchEngine && other.id == id &&
        other.template == template

    override fun hashCode(): Int = 31 * id.hashCode() + template.hashCode()

    override fun toString(): String = "BrowserSearchEngine($id)"

    companion object {
        const val PLACEHOLDER = "{q}"

        /**
         * Listed first because it does not meet an unusual WebView user agent with a consent wall,
         * and clearing one of those with a five-button remote is genuinely painful.
         */
        val DUCKDUCKGO = BrowserSearchEngine(
            id = "duckduckgo",
            name = "DuckDuckGo",
            template = "https://duckduckgo.com/?q=$PLACEHOLDER",
        )

        val GOOGLE = BrowserSearchEngine(
            id = "google",
            name = "Google",
            template = "https://www.google.com/search?q=$PLACEHOLDER",
        )

        val BING = BrowserSearchEngine(
            id = "bing",
            name = "Bing",
            template = "https://www.bing.com/search?q=$PLACEHOLDER",
        )

        val PRESETS = listOf(DUCKDUCKGO, GOOGLE, BING)

        val DEFAULT = DUCKDUCKGO

        /**
         * A template someone supplied.
         *
         * Null when it is unusable, rather than silently falling back: a template that is not https
         * or has nowhere to put the terms would produce a navigation the viewer did not ask for.
         */
        fun custom(template: String): BrowserSearchEngine? {
            val trimmed = template.trim()
            if (!trimmed.startsWith("https://")) return null
            if (!trimmed.contains(PLACEHOLDER)) return null
            return BrowserSearchEngine(id = "custom", name = "Custom", template = trimmed)
        }

        fun byId(id: String): BrowserSearchEngine? = PRESETS.firstOrNull { it.id == id }

        /**
         * Percent-encodes for a query string.
         *
         * Hand-rolled rather than `URLEncoder`, which is a JDK class the receiver would otherwise
         * only pull in here, and which encodes a space as `+` only because of a legacy form rule
         * this deliberately keeps.
         */
        private fun encode(value: String): String {
            val out = StringBuilder(value.length * 3)
            value.toByteArray(Charsets.UTF_8).forEach { byte ->
                val code = byte.toInt() and 0xFF
                val char = code.toChar()
                when {
                    char in 'a'..'z' || char in 'A'..'Z' || char in '0'..'9' -> out.append(char)
                    char == '-' || char == '_' || char == '.' || char == '~' -> out.append(char)
                    char == ' ' -> out.append('+')
                    else -> out.append('%').append(HEX[code shr 4]).append(HEX[code and 0x0F])
                }
            }
            return out.toString()
        }

        private val HEX = "0123456789ABCDEF".toCharArray()
    }
}
