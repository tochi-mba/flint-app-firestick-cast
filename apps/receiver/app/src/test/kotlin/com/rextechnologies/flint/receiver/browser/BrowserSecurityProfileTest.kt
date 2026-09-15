package com.rextechnologies.flint.receiver.browser

import android.app.Activity
import android.webkit.WebSettings
import android.webkit.WebView
import org.junit.After
import org.junit.Before
import org.junit.runner.RunWith
import org.robolectric.Robolectric
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class BrowserSecurityProfileTest {
    private lateinit var activity: Activity
    private lateinit var webView: WebView

    @Before
    fun setUp() {
        activity = Robolectric.buildActivity(Activity::class.java).setup().get()
        webView = WebView(activity)
    }

    @After
    fun tearDown() {
        webView.destroy()
        activity.finish()
    }

    @Test
    fun `base profile applies hardening and ten foot reading defaults`() {
        val darkening = RecordingDarkening(supported = false)

        val capabilities = BrowserSecurityProfile.apply(
            settings = webView.settings,
            darkening = darkening,
        )

        BrowserSecurityProfile.assertHardened(webView.settings)
        assertEquals(BrowserViewSettings.DEFAULT_ZOOM, webView.settings.textZoom)
        assertTrue(webView.settings.useWideViewPort)
        assertTrue(webView.settings.loadWithOverviewMode)
        assertFalse(webView.settings.supportZoom())
        assertFalse(capabilities.algorithmicDarkeningAvailable)
        assertTrue(darkening.values.isEmpty())
    }

    @Test
    fun `presentation applies zoom and UA while darkening remains feature gated`() {
        val platformUserAgent = webView.settings.userAgentString
        val darkening = RecordingDarkening(supported = true)
        val state = BrowserViewState(
            zoomPercent = 175,
            userAgentMode = BrowserUserAgentMode.DESKTOP,
            darkModeEnabled = false,
        )

        val capabilities = BrowserSecurityProfile.applyViewSettings(
            settings = webView.settings,
            state = state,
            platformDefaultUserAgent = platformUserAgent,
            darkening = darkening,
        )

        assertEquals(175, webView.settings.textZoom)
        assertEquals(
            BrowserUserAgentStrings.resolve(BrowserUserAgentMode.DESKTOP, platformUserAgent),
            webView.settings.userAgentString,
        )
        assertEquals(listOf(false), darkening.values)
        assertTrue(capabilities.algorithmicDarkeningAvailable)
    }

    @Test
    fun `unsupported darkening never calls the compatibility API`() {
        val darkening = RecordingDarkening(supported = false)

        val capabilities = BrowserSecurityProfile.applyViewSettings(
            settings = webView.settings,
            state = BrowserViewState(darkModeEnabled = true),
            platformDefaultUserAgent = webView.settings.userAgentString,
            darkening = darkening,
        )

        assertFalse(capabilities.algorithmicDarkeningAvailable)
        assertTrue(darkening.values.isEmpty())
    }

    @Test
    fun `presentation snaps an untrusted zoom value to an owned step`() {
        BrowserSecurityProfile.applyViewSettings(
            settings = webView.settings,
            state = BrowserViewState(zoomPercent = 137),
            platformDefaultUserAgent = webView.settings.userAgentString,
            darkening = RecordingDarkening(supported = false),
        )

        assertEquals(125, webView.settings.textZoom)
    }

    @Test
    @Config(sdk = [25])
    fun `on Fire OS 6 the profile stops the WebView remembering form input`() {
        assertTrue(webView.settings.savesFormData, "saved form data is on by default below API 26")

        BrowserSecurityProfile.apply(settings = webView.settings, darkening = RecordingDarkening(supported = false))

        assertFalse(webView.settings.savesFormData)
    }

    private class RecordingDarkening(
        override val supported: Boolean,
    ) : BrowserAlgorithmicDarkening {
        val values = mutableListOf<Boolean>()

        override fun setAllowed(settings: WebSettings, allowed: Boolean) {
            values += allowed
        }
    }
}

// Deprecated because Android 8 made it do nothing; API 25, where it still works, is what this reads.
@Suppress("DEPRECATION")
private val WebSettings.savesFormData: Boolean
    get() = saveFormData
