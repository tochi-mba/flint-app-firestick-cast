package com.rextechnologies.flint.receiver.ui

import com.rextechnologies.flint.receiver.showBrowserRefusal
import com.rextechnologies.flint.receiver.setBrowserFullscreen
import com.rextechnologies.flint.receiver.showBrowserNotice
import android.content.Context
import android.view.View
import android.webkit.WebView
import com.rextechnologies.flint.receiver.ReceiverService
import com.rextechnologies.flint.receiver.browser.AndroidBrowserFullscreenHost
import com.rextechnologies.flint.receiver.browser.BrowserFullscreenController
import com.rextechnologies.flint.receiver.browser.BrowserTabHost
import com.rextechnologies.flint.receiver.browser.BrowserTabSurfacePort
import com.rextechnologies.flint.receiver.browser.BrowserWebViewDriver
import com.rextechnologies.flint.receiver.browser.TabEffect

/**
 * Owns the Activity-side WebViews for the browser's service-side tab model.
 *
 * Every callback is tagged with its tab id before it leaves this object. That is the guard that
 * prevents a late title/progress event from a detached renderer replacing foreground chrome.
 */
internal class ReceiverBrowserTabBinding(
    context: Context,
    private val service: ReceiverService,
    onViewportChanged: () -> Unit,
    onEditingChanged: (Boolean) -> Unit = {},
) : BrowserTabSurfacePort {
    val host: BrowserTabHost = BrowserTabHost(
        context = context,
        buildDriver = ::buildDriver,
        onStateTooLarge = {
            service.showBrowserNotice("That tab was too large to suspend, so it will reload when reopened.")
        },
        onEditingChanged = onEditingChanged,
    )

    private val fullscreen = LinkedHashMap<Long, BrowserFullscreenController<View>>()

    init {
        host.addOnLayoutChangeListener { _, _, _, _, _, _, _, _, _ -> onViewportChanged() }
    }

    override fun apply(effect: TabEffect) {
        when (effect) {
            is TabEffect.Create -> host.create(effect.id, effect.url)
            is TabEffect.Show -> host.show(effect.id)
            is TabEffect.Freeze -> {
                fullscreen.remove(effect.id)?.abandon()
                host.freeze(effect.id)
            }
            is TabEffect.Restore -> host.restore(effect.id)
            is TabEffect.Destroy -> {
                fullscreen.remove(effect.id)?.abandon()
                host.destroy(effect.id)
            }
            TabEffect.Refused -> Unit
        }
    }

    override fun driverFor(tabId: Long): BrowserWebViewDriver? = host.driverFor(tabId)

    override fun exitFullscreen(): Boolean = fullscreen[host.activeId()]?.exit() == true

    override fun setPageFocusEnabled(enabled: Boolean) = host.setPageFocusEnabled(enabled)

    fun destroy() {
        fullscreen.values.forEach { it.abandon() }
        fullscreen.clear()
        host.destroyAll()
    }

    private fun buildDriver(webView: WebView, tabId: Long): BrowserWebViewDriver {
        val fullscreenController = BrowserFullscreenController(
            host = AndroidBrowserFullscreenHost(host) { active ->
                if (host.activeId() == tabId) service.setBrowserFullscreen(active)
            },
            canEnter = { host.activeId() == tabId },
        )
        fullscreen[tabId] = fullscreenController
        return BrowserWebViewDriver(
            webView = webView,
            onEvent = { event -> service.onBrowserTabStateEvent(tabId, event) },
            onPageDialog = { pending -> service.onBrowserTabPageDialog(tabId, pending) },
            fullscreen = fullscreenController,
            onFavicon = { bitmap -> service.onBrowserTabFavicon(tabId, bitmap) },
            onRefused = service::showBrowserRefusal,
            onFindChanged = { find -> service.onBrowserFindChanged(tabId, find) },
        )
    }
}
