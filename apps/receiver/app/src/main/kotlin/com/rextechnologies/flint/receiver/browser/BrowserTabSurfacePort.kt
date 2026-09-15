package com.rextechnologies.flint.receiver.browser

/**
 * Activity-owned renderer operations used by the service-owned tab state.
 *
 * The service may retain this interface only while the browser composable is alive. It never owns
 * a WebView; the Activity creates and destroys the concrete host and detaches it on release.
 */
interface BrowserTabSurfacePort {
    fun apply(effect: TabEffect)
    fun driverFor(tabId: Long): BrowserWebViewDriver?
    fun exitFullscreen(): Boolean

    /**
     * When false, the page cannot take Android view focus. Used while Compose chrome owns the
     * remote so Select reaches the omnibar instead of the WebView.
     */
    fun setPageFocusEnabled(enabled: Boolean)
}
