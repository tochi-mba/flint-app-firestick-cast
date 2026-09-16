package com.rextechnologies.flint.receiver.browser

import android.os.Build
import android.webkit.WebSettings
import android.webkit.WebView
import androidx.webkit.WebSettingsCompat
import androidx.webkit.WebViewFeature

/** Runtime WebView capabilities that the UI must represent honestly. */
data class BrowserWebViewCapabilities(
    val algorithmicDarkeningAvailable: Boolean,
)

/**
 * Narrow adapter around the static AndroidX WebView compatibility API.
 *
 * Keeping the probe and mutation together makes it impossible for a caller to invoke an optional
 * API without first checking the matching [WebViewFeature]. It also keeps the gate deterministic
 * in local tests, where the installed device WebView is intentionally absent.
 */
interface BrowserAlgorithmicDarkening {
    val supported: Boolean

    fun setAllowed(settings: WebSettings, allowed: Boolean)
}

private object AndroidBrowserAlgorithmicDarkening : BrowserAlgorithmicDarkening {
    override val supported: Boolean
        get() = WebViewFeature.isFeatureSupported(WebViewFeature.ALGORITHMIC_DARKENING)

    override fun setAllowed(settings: WebSettings, allowed: Boolean) {
        check(supported) { "algorithmic darkening is not supported by this WebView" }
        WebSettingsCompat.setAlgorithmicDarkeningAllowed(settings, allowed)
    }
}

/**
 * Applies the locked-down WebView security profile required by ADR-0006.
 *
 * No JavaScript bridge, file access, mixed content, or geolocation is permitted. The Activity owns
 * the WebView instance; this helper only mutates settings.
 */
object BrowserSecurityProfile {
    fun apply(
        settings: WebSettings,
        viewState: BrowserViewState = BrowserViewState(),
        platformDefaultUserAgent: String = settings.userAgentString.orEmpty(),
        darkening: BrowserAlgorithmicDarkening = AndroidBrowserAlgorithmicDarkening,
    ): BrowserWebViewCapabilities {
        settings.javaScriptEnabled = true // required for ordinary HTTPS pages; no bridge is added
        settings.domStorageEnabled = true
        settings.allowFileAccess = false
        settings.allowContentAccess = false
        disableLegacyFileUrlAccess(settings)
        disableSavedFormData(settings)
        settings.mediaPlaybackRequiresUserGesture = true
        settings.setSupportMultipleWindows(false)
        settings.javaScriptCanOpenWindowsAutomatically = false
        settings.mixedContentMode = WebSettings.MIXED_CONTENT_NEVER_ALLOW
        settings.cacheMode = WebSettings.LOAD_DEFAULT
        settings.setGeolocationEnabled(false)

        // Flint owns zoom. Pinch controls are both unusable with a D-pad and a second, divergent
        // source of zoom state.
        settings.setSupportZoom(false)
        settings.builtInZoomControls = false
        settings.displayZoomControls = false
        settings.useWideViewPort = true
        settings.loadWithOverviewMode = true

        val capabilities = applyViewSettings(
            settings = settings,
            state = viewState,
            platformDefaultUserAgent = platformDefaultUserAgent,
            darkening = darkening,
        )
        // This is deliberately on the production path. A future edit cannot silently weaken a
        // security flag while tests continue asserting an unused helper.
        assertHardened(settings)
        return capabilities
    }

    /** Applies preferences without relaxing any member of the security profile. */
    fun applyViewSettings(
        settings: WebSettings,
        state: BrowserViewState,
        platformDefaultUserAgent: String,
        darkening: BrowserAlgorithmicDarkening = AndroidBrowserAlgorithmicDarkening,
    ): BrowserWebViewCapabilities {
        settings.textZoom = BrowserViewSettings.normalizeZoom(state.zoomPercent)
        settings.userAgentString = BrowserUserAgentStrings.resolve(
            state.userAgentMode,
            platformDefaultUserAgent,
        )

        val darkeningAvailable = darkening.supported
        if (darkeningAvailable) {
            darkening.setAllowed(settings, state.darkModeEnabled)
        }
        return BrowserWebViewCapabilities(
            algorithmicDarkeningAvailable = darkeningAvailable,
        )
    }

    /**
     * Explicitly closes both legacy file-URL escape hatches.
     *
     * Android documents these as false by default on modern API levels, but a hardened profile must
     * not depend on the WebView implementation preserving that default. Robolectric exposed exactly
     * that assumption: one flag remained enabled and the production assertion correctly rejected the
     * profile. Keep the assignments next to the other file-access switches so the invariant is
     * established by Flint rather than inherited from a platform default.
     */
    @Suppress("DEPRECATION")
    private fun disableLegacyFileUrlAccess(settings: WebSettings) {
        settings.allowFileAccessFromFileURLs = false
        settings.allowUniversalAccessFromFileURLs = false
    }

    /**
     * Stops the WebView on Fire OS 6 remembering what is typed into forms.
     *
     * Android 8 handed the job to the autofill framework and made this setting do nothing, which is
     * why it is deprecated; Fire OS 6 reports API 25 and has no replacement to call, so the old
     * setting is used there and only there.
     *
     * [apiLevel] is a parameter so a test can exercise both sides of that line at whatever SDK the
     * suite runs on, rather than making Robolectric fetch a framework no other test needs.
     */
    internal fun disableSavedFormData(settings: WebSettings, apiLevel: Int = Build.VERSION.SDK_INT) {
        if (apiLevel < Build.VERSION_CODES.O) {
            @Suppress("DEPRECATION")
            settings.saveFormData = false
        }
    }

    fun assertHardened(settings: WebSettings) {
        check(!settings.allowFileAccess) { "file access must stay disabled" }
        check(!settings.allowContentAccess) { "content access must stay disabled" }
        check(!settings.allowFileAccessFromFileURLs) { "file URL access must stay disabled" }
        check(!settings.allowUniversalAccessFromFileURLs) { "universal file access must stay disabled" }
        check(!settings.supportMultipleWindows()) { "multiple windows must stay disabled" }
        check(!settings.javaScriptCanOpenWindowsAutomatically) { "window.open must stay disabled" }
        check(settings.mediaPlaybackRequiresUserGesture) { "media playback must require a gesture" }
        check(settings.mixedContentMode == WebSettings.MIXED_CONTENT_NEVER_ALLOW) {
            "mixed content must stay denied"
        }
    }

    fun detach(webView: WebView) {
        webView.stopLoading()
        webView.loadUrl("about:blank")
        webView.webChromeClient = null
        webView.webViewClient = android.webkit.WebViewClient()
        webView.clearHistory()
    }
}
