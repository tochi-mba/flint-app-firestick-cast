package com.rextechnologies.flint.receiver.ui

import android.content.Context
import android.view.View
import android.webkit.WebView
import com.rextechnologies.flint.receiver.browser.BrowserWebViewDriver
import com.rextechnologies.flint.receiver.browser.PaneFullscreenController
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceSession

/**
 * Activity-owned multi-WebView binding for [BrowserWorkspaceSession].
 *
 * Mirrors [ReceiverBrowserTabBinding]: the service owns policy; this object owns native views.
 */
internal class ReceiverBrowserWorkspaceBinding(
    context: Context,
    private val session: BrowserWorkspaceSession,
    private val onNotice: (String) -> Unit,
    private val onRefusal: (com.rextechnologies.flint.receiver.browser.BrowserRefusal) -> Unit,
) {
    val host: ReceiverBrowserWorkspaceHost = ReceiverBrowserWorkspaceHost(
        context = context,
        buildDriver = ::buildDriver,
        onStateTooLarge = {
            onNotice("That page was too large to suspend, so it will reload when restored.")
        },
        onEditingChanged = { _, _ -> },
        onFullscreenChanged = session::onPageFullscreen,
    )

    init {
        session.attachHost(host)
    }

    fun destroy() {
        session.detachHost(host)
        host.destroyAll()
    }

    private fun buildDriver(
        webView: WebView,
        paneId: Long,
        generation: Long,
        fullscreen: PaneFullscreenController<View>,
    ): BrowserWebViewDriver = BrowserWebViewDriver(
        webView = webView,
        onEvent = { event -> session.onPaneStateEvent(paneId, generation, event) },
        onPageDialog = { dialog -> session.onDialog(paneId, generation, dialog) },
        fullscreen = fullscreen,
        onFavicon = {},
        onRefused = onRefusal,
        onFindChanged = {},
    ).also {
        webView.setTag(paneId)
    }
}
