package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotEquals
import kotlin.test.assertTrue

class BrowserViewSettingsTest {
    @Test
    fun `television defaults are readable and conservative`() {
        val settings = BrowserViewSettings()

        assertEquals(125, settings.state.zoomPercent)
        assertEquals(BrowserUserAgentMode.TV, settings.state.userAgentMode)
        assertTrue(settings.state.darkModeEnabled)
        assertEquals(BrowserInteractionMode.CURSOR, settings.state.inputMode)
        assertEquals(BrowserSearchEngine.DEFAULT, settings.state.searchEngine)
        assertFalse(settings.state.find.active)
    }

    @Test
    fun `zoom walks only supported steps and clamps at both ends`() {
        val settings = BrowserViewSettings()

        repeat(20) { settings.zoomIn() }
        assertEquals(200, settings.state.zoomPercent)

        repeat(20) { settings.zoomOut() }
        assertEquals(75, settings.state.zoomPercent)

        settings.resetZoom()
        assertEquals(125, settings.state.zoomPercent)
    }

    @Test
    fun `an arbitrary zoom request snaps to a supported bounded step`() {
        val settings = BrowserViewSettings()

        settings.setZoom(10)
        assertEquals(75, settings.state.zoomPercent)

        settings.setZoom(138)
        assertEquals(150, settings.state.zoomPercent)

        settings.setZoom(9_999)
        assertEquals(200, settings.state.zoomPercent)
    }

    @Test
    fun `user agent choice is remembered independently for each supplied site key`() {
        val settings = BrowserViewSettings()
        settings.activateSite("example.test")
        settings.setUserAgent(BrowserUserAgentMode.DESKTOP)

        settings.activateSite("another.test")
        assertEquals(BrowserUserAgentMode.TV, settings.state.userAgentMode)
        settings.setUserAgent(BrowserUserAgentMode.MOBILE)

        settings.activateSite("EXAMPLE.TEST.")
        assertEquals(BrowserUserAgentMode.DESKTOP, settings.state.userAgentMode)
        settings.activateSite("another.test")
        assertEquals(BrowserUserAgentMode.MOBILE, settings.state.userAgentMode)
    }

    @Test
    fun `dark input and search choices update without disturbing each other`() {
        val settings = BrowserViewSettings()

        settings.setDarkMode(false)
        settings.setInputMode(BrowserInteractionMode.FOCUS)
        settings.setSearchEngine(BrowserSearchEngine.BING)

        assertFalse(settings.state.darkModeEnabled)
        assertEquals(BrowserInteractionMode.FOCUS, settings.state.inputMode)
        assertEquals(BrowserSearchEngine.BING, settings.state.searchEngine)
        assertEquals(125, settings.state.zoomPercent)
    }

    @Test
    fun `find results are one based for people and never escape their total`() {
        val settings = BrowserViewSettings()
        settings.startFind("needle")

        settings.updateFind(activeMatchOrdinal = -4, numberOfMatches = -8, doneCounting = false)
        assertEquals(0, settings.state.find.currentMatch)
        assertEquals(0, settings.state.find.totalMatches)

        settings.updateFind(activeMatchOrdinal = 99, numberOfMatches = 3, doneCounting = true)
        assertEquals(3, settings.state.find.currentMatch)
        assertEquals(3, settings.state.find.totalMatches)
        assertTrue(settings.state.find.doneCounting)

        settings.clearFind()
        assertEquals(BrowserFindState(), settings.state.find)
    }

    @Test
    fun `find query is bounded by Unicode code points rather than UTF 16 halves`() {
        val settings = BrowserViewSettings()
        val query = "🧪".repeat(BrowserFindState.MAX_QUERY_CODE_POINTS + 20)

        settings.startFind(query)

        assertEquals(BrowserFindState.MAX_QUERY_CODE_POINTS, settings.state.find.query.codePointCount(0, settings.state.find.query.length))
    }

    @Test
    fun `site user agent memory is LRU bounded and never reuses an evicted preference`() {
        val settings = BrowserViewSettings()
        settings.activateSite("eldest.test")
        settings.setUserAgent(BrowserUserAgentMode.DESKTOP)

        repeat(BrowserViewSettings.MAX_SITE_OVERRIDES) { index ->
            settings.activateSite("site-$index.test")
            settings.setUserAgent(BrowserUserAgentMode.MOBILE)
        }

        settings.activateSite("eldest.test")
        assertEquals(BrowserUserAgentMode.TV, settings.state.userAgentMode)
        settings.activateSite("site-0.test")
        assertEquals(BrowserUserAgentMode.MOBILE, settings.state.userAgentMode)
    }

    @Test
    fun `returning a site to TV mode removes its stored override`() {
        val settings = BrowserViewSettings()
        settings.activateSite("example.test")
        settings.setUserAgent(BrowserUserAgentMode.DESKTOP)
        settings.setUserAgent(BrowserUserAgentMode.TV)

        repeat(BrowserViewSettings.MAX_SITE_OVERRIDES) { index ->
            settings.activateSite("site-$index.test")
            settings.setUserAgent(BrowserUserAgentMode.MOBILE)
        }
        settings.activateSite("example.test")

        assertEquals(BrowserUserAgentMode.TV, settings.state.userAgentMode)
    }

    @Test
    fun `invalid site keys cannot consume the bounded preference memory`() {
        val settings = BrowserViewSettings()

        settings.activateSite("x".repeat(254))
        settings.setUserAgent(BrowserUserAgentMode.DESKTOP)
        settings.activateSite(null)
        settings.setUserAgent(BrowserUserAgentMode.MOBILE)
        settings.activateSite("x".repeat(254))

        assertEquals(BrowserUserAgentMode.TV, settings.state.userAgentMode)
    }

    @Test
    fun `user agent variants retain the installed Chromium engine identity`() {
        val platform = "Mozilla/5.0 (Linux; Android 9; AFTMM; wv) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 " +
            "Chrome/124.0.0.0 Safari/537.36"

        val desktop = BrowserUserAgentStrings.resolve(BrowserUserAgentMode.DESKTOP, platform)
        val mobile = BrowserUserAgentStrings.resolve(BrowserUserAgentMode.MOBILE, platform)

        assertEquals(platform, BrowserUserAgentStrings.resolve(BrowserUserAgentMode.TV, platform))
        assertTrue(desktop.contains("X11; Linux x86_64"))
        assertTrue(desktop.contains("Chrome/124.0.0.0"))
        assertFalse(desktop.contains("Version/4.0"))
        assertTrue(mobile.contains("Linux; Android 10; Mobile"))
        assertTrue(mobile.endsWith("Mobile"))
        assertNotEquals(desktop, mobile)
    }

    @Test
    fun `unknown platform user agent is preserved instead of fabricated`() {
        val unusual = "VendorBrowser/7"

        assertEquals(
            unusual,
            BrowserUserAgentStrings.resolve(BrowserUserAgentMode.DESKTOP, unusual),
        )
        assertEquals(
            unusual,
            BrowserUserAgentStrings.resolve(BrowserUserAgentMode.MOBILE, unusual),
        )
    }
}
