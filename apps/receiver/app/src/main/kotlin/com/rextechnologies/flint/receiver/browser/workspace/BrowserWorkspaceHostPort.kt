package com.rextechnologies.flint.receiver.browser.workspace

import android.view.View
import com.rextechnologies.flint.receiver.browser.BrowserWebViewDriver
import com.rextechnologies.flint.receiver.browser.BrowserNativeInput

/** Activity-owned rendering boundary; every call is made on Android's main thread. */
internal interface BrowserWorkspaceHostPort {
    fun create(id: Long, generation: Long, url: String?)
    /** True only when native page/history state was restored successfully. */
    fun restore(id: Long, generation: Long): Boolean
    fun driverFor(id: Long): BrowserWebViewDriver?
    fun dispatchNativeInput(id: Long, input: BrowserNativeInput) { driverFor(id)?.dispatch(input) }
    fun freeze(id: Long)
    fun destroy(id: Long)
    fun destroyAll()
    fun exitFullscreen(id: Long): Boolean
    fun setPageFocusEnabled(enabled: Boolean)
    fun render(state: BrowserWorkspaceState)
    /**
     * Root view that shows every live pane together — used for the Windows JPEG preview so the host
     * sees the same mosaic layout as the sofa, not only the focused WebView.
     */
    fun previewCaptureView(): View? = null

    /**
     * Maps mosaic-root pixels to `(paneId, localX to localY)`. Gaps between panes miss.
     */
    fun hitTestMosaic(x: Int, y: Int): Pair<Long, Pair<Int, Int>>? = null
}
