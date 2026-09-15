package com.rextechnologies.flint.receiver.browser

import java.util.LinkedHashMap
import java.util.Locale
import kotlin.math.abs

/** The compatibility identity a page sees. TV preserves the WebView's platform identity. */
enum class BrowserUserAgentMode { TV, DESKTOP, MOBILE }

/** The bounded, human-facing state of find-in-page. */
data class BrowserFindState(
    val query: String = "",
    val currentMatch: Int = 0,
    val totalMatches: Int = 0,
    val doneCounting: Boolean = false,
) {
    val active: Boolean get() = query.isNotEmpty()

    companion object {
        const val MAX_QUERY_CODE_POINTS = 256

        fun started(rawQuery: String): BrowserFindState {
            val trimmed = rawQuery.trim()
            if (trimmed.isEmpty()) return BrowserFindState()
            return BrowserFindState(query = trimmed.takeCodePoints(MAX_QUERY_CODE_POINTS))
        }
    }

    /** WebView reports a zero-based ordinal; screens show a one-based count. */
    fun withResult(
        activeMatchOrdinal: Int,
        numberOfMatches: Int,
        doneCounting: Boolean,
    ): BrowserFindState {
        if (!active) return this
        val total = numberOfMatches.coerceAtLeast(0)
        val current = if (total == 0) 0 else activeMatchOrdinal.coerceIn(0, total - 1) + 1
        return copy(
            currentMatch = current,
            totalMatches = total,
            doneCounting = doneCounting,
        )
    }
}

/** Everything about the page presentation that can be changed without navigating. */
data class BrowserViewState(
    val zoomPercent: Int = BrowserViewSettings.DEFAULT_ZOOM,
    val userAgentMode: BrowserUserAgentMode = BrowserUserAgentMode.TV,
    val darkModeEnabled: Boolean = true,
    val inputMode: BrowserInteractionMode = BrowserInteractionMode.CURSOR,
    val searchEngine: BrowserSearchEngine = BrowserSearchEngine.DEFAULT,
    val find: BrowserFindState = BrowserFindState(),
)

/**
 * Owns the small set of page-view preferences.
 *
 * The caller supplies a registrable-domain key to [activateSite]. Keeping public-suffix policy out
 * of this class avoids silently treating `co.uk` as a site, while still making the memory rule easy
 * to test. Overrides are LRU-bounded so hostile navigation cannot grow them without limit.
 */
class BrowserViewSettings {
    companion object {
        val ZOOM_STEPS = listOf(75, 100, 125, 150, 175, 200)
        const val DEFAULT_ZOOM = 125
        const val MAX_SITE_OVERRIDES = 200

        fun normalizeZoom(requestedPercent: Int): Int =
            ZOOM_STEPS.minBy { abs(it.toLong() - requestedPercent.toLong()) }
    }

    var state: BrowserViewState = BrowserViewState()
        private set

    private var activeSite: String? = null
    private val userAgentsBySite = LinkedHashMap<String, BrowserUserAgentMode>(16, 0.75f, true)

    fun zoomIn(): BrowserViewState = setZoom(stepFrom(state.zoomPercent, 1))

    fun zoomOut(): BrowserViewState = setZoom(stepFrom(state.zoomPercent, -1))

    fun resetZoom(): BrowserViewState = setZoom(DEFAULT_ZOOM)

    fun setZoom(requestedPercent: Int): BrowserViewState {
        val normalized = normalizeZoom(requestedPercent)
        state = state.copy(zoomPercent = normalized)
        return state
    }

    /** Activates preferences for a caller-normalized registrable domain. */
    fun activateSite(registrableDomain: String?): BrowserViewState {
        activeSite = registrableDomain.normalizeSiteKey()
        val remembered = activeSite?.let(userAgentsBySite::get) ?: BrowserUserAgentMode.TV
        state = state.copy(userAgentMode = remembered)
        return state
    }

    fun setUserAgent(mode: BrowserUserAgentMode): BrowserViewState {
        state = state.copy(userAgentMode = mode)
        activeSite?.let { site ->
            if (mode == BrowserUserAgentMode.TV) {
                userAgentsBySite.remove(site)
            } else {
                userAgentsBySite[site] = mode
                trimUserAgentMemory()
            }
        }
        return state
    }

    fun setDarkMode(enabled: Boolean): BrowserViewState {
        state = state.copy(darkModeEnabled = enabled)
        return state
    }

    fun setInputMode(mode: BrowserInteractionMode): BrowserViewState {
        state = state.copy(inputMode = mode)
        return state
    }

    fun setSearchEngine(engine: BrowserSearchEngine): BrowserViewState {
        state = state.copy(searchEngine = engine)
        return state
    }

    fun startFind(query: String): BrowserViewState {
        state = state.copy(find = BrowserFindState.started(query))
        return state
    }

    fun updateFind(
        activeMatchOrdinal: Int,
        numberOfMatches: Int,
        doneCounting: Boolean,
    ): BrowserViewState {
        state = state.copy(
            find = state.find.withResult(activeMatchOrdinal, numberOfMatches, doneCounting),
        )
        return state
    }

    fun clearFind(): BrowserViewState {
        state = state.copy(find = BrowserFindState())
        return state
    }

    private fun stepFrom(current: Int, offset: Int): Int {
        val index = ZOOM_STEPS.indexOf(current).takeIf { it >= 0 }
            ?: ZOOM_STEPS.indices.minBy { abs(ZOOM_STEPS[it] - current) }
        return ZOOM_STEPS[(index + offset).coerceIn(ZOOM_STEPS.indices)]
    }

    private fun trimUserAgentMemory() {
        while (userAgentsBySite.size > MAX_SITE_OVERRIDES) {
            val eldest = userAgentsBySite.entries.iterator()
            eldest.next()
            eldest.remove()
        }
    }
}

/** Produces compatibility UAs without baking the installed Chromium version into the app. */
object BrowserUserAgentStrings {
    fun resolve(mode: BrowserUserAgentMode, platformDefault: String): String = when (mode) {
        BrowserUserAgentMode.TV -> platformDefault
        BrowserUserAgentMode.DESKTOP -> withPlatform(platformDefault, "X11; Linux x86_64", mobile = false)
        BrowserUserAgentMode.MOBILE -> withPlatform(platformDefault, "Linux; Android 10; Mobile", mobile = true)
    }

    private fun withPlatform(default: String, platform: String, mobile: Boolean): String {
        val engine = default.substringAfter("AppleWebKit/", missingDelimiterValue = "")
        if (engine.isEmpty()) return default
        val cleaned = engine
            .replace(Regex("\\s+Version/[^ ]+"), "")
            .replace(Regex("\\s+Mobile(?:/[^ ]+)?"), "")
            .replace("; wv", "")
            .trim()
        val mobileToken = if (mobile) " Mobile" else ""
        return "Mozilla/5.0 ($platform) AppleWebKit/$cleaned$mobileToken"
    }
}

private fun String?.normalizeSiteKey(): String? = this
    ?.trim()
    ?.trimEnd('.')
    ?.lowercase(Locale.ROOT)
    ?.takeIf { it.isNotEmpty() && it.length <= 253 }

private fun String.takeCodePoints(maximum: Int): String {
    val count = codePointCount(0, length)
    if (count <= maximum) return this
    return substring(0, offsetByCodePoints(0, maximum))
}
