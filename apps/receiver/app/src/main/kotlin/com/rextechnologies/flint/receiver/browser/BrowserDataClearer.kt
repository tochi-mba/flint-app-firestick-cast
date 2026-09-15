package com.rextechnologies.flint.receiver.browser

import android.webkit.CookieManager
import android.webkit.WebStorage
import android.webkit.WebView

/**
 * Clears only the Flint receiver WebView's browsing data after an explicit TV confirmation.
 *
 * Never touches the Windows trust store, receiver TLS identity, pairing codes, or other apps.
 */
object BrowserDataClearer {
    fun clear(webView: WebView) {
        webView.stopLoading()
        webView.loadUrl("about:blank")
        webView.clearHistory()
        webView.clearFormData()
        webView.clearCache(true)
        webView.clearSslPreferences()
        CookieManager.getInstance().apply {
            removeAllCookies(null)
            flush()
        }
        WebStorage.getInstance().deleteAllData()
    }
}
