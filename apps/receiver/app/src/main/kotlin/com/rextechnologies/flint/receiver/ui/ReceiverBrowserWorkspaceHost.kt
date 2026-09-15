package com.rextechnologies.flint.receiver.ui

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Color
import android.os.Bundle
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.webkit.WebView
import android.widget.FrameLayout
import android.widget.TextView
import com.rextechnologies.flint.receiver.browser.BrowserSecurityProfile
import com.rextechnologies.flint.receiver.browser.BrowserWebViewDriver
import com.rextechnologies.flint.receiver.browser.EditingAwareWebView
import com.rextechnologies.flint.receiver.browser.PaneFullscreenController
import com.rextechnologies.flint.receiver.browser.PaneFullscreenHost
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceHostPort
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceInteractionMode
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceLayout
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceLayoutFrames
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceSplit
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceState

/** Native geometry understood by [ReceiverBrowserWorkspaceHost], deliberately separate from UI copy. */
internal enum class ReceiverWorkspaceLayout {
    SINGLE,
    SIDE_BY_SIDE,
    STACKED,
    GRID,
}

private fun ReceiverWorkspaceLayout.toWorkspaceLayout(): BrowserWorkspaceLayout = when (this) {
    ReceiverWorkspaceLayout.SINGLE -> BrowserWorkspaceLayout.SINGLE
    ReceiverWorkspaceLayout.SIDE_BY_SIDE -> BrowserWorkspaceLayout.SPLIT_HORIZONTAL
    ReceiverWorkspaceLayout.STACKED -> BrowserWorkspaceLayout.SPLIT_VERTICAL
    ReceiverWorkspaceLayout.GRID -> BrowserWorkspaceLayout.GRID_2X2
}

/** The renderer-facing part of one workspace pane. It never carries page contents or credentials. */
internal data class ReceiverWorkspacePaneRender(
    val id: Long,
    val title: String,
    val live: Boolean,
    val suspended: Boolean,
)

/**
 * Activity-owned multi-WebView host for a real browser workspace.
 *
 * This is intentionally not a Compose grid: one native root owns every WebView, so clipping,
 * renderer release, and a site's custom fullscreen view all use the same pane bounds.  A suspended
 * pane is an explicit native placeholder, never a fake video frame.  The service-owned workspace
 * reducer decides *which* panes are live; this class only performs those decisions on the UI
 * thread.
 */
@SuppressLint("ViewConstructor", "SetTextI18n")
internal class ReceiverBrowserWorkspaceHost(
    context: Context,
    private val buildDriver: (
        webView: WebView,
        paneId: Long,
        generation: Long,
        fullscreen: PaneFullscreenController<View>,
    ) -> BrowserWebViewDriver,
    private val onStateTooLarge: (Long) -> Unit,
    private val onEditingChanged: (Long, Boolean) -> Unit,
    private val onFullscreenChanged: (Long, Long, Boolean) -> Unit = { _, _, _ -> },
) : FrameLayout(context), BrowserWorkspaceHostPort {
    companion object {
        /** Per-pane cap, leaving headroom below Android's process-wide binder state ceiling. */
        const val MAX_SAVED_STATE_BYTES = 32 * 1024
    }

    private data class LivePane(
        val webView: WebView,
        val driver: BrowserWebViewDriver,
        val fullscreen: PaneFullscreenController<View>,
    )

    private class PaneContainer(context: Context) : FrameLayout(context) {
        private val page = FrameLayout(context).apply {
            layoutParams = LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT)
            setBackgroundColor(Color.BLACK)
        }
        private val custom = FrameLayout(context).apply {
            layoutParams = LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT)
            setBackgroundColor(Color.BLACK)
            visibility = GONE
        }
        private val placeholder = TextView(context).apply {
            layoutParams = LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT)
            gravity = Gravity.CENTER
            setTextColor(Color.rgb(190, 198, 205))
            textSize = 15f
            setPadding(24, 24, 24, 24)
        }

        init {
            setBackgroundColor(Color.BLACK)
            addView(page)
            addView(placeholder)
            addView(custom)
        }

        fun showPage(webView: WebView) {
            if (webView.parent !== page) {
                (webView.parent as? ViewGroup)?.removeView(webView)
                page.removeAllViews()
                page.addView(webView, LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT))
            }
            placeholder.visibility = GONE
            page.visibility = if (custom.visibility == VISIBLE) INVISIBLE else VISIBLE
        }

        fun showSuspended(title: String) {
            page.removeAllViews()
            page.visibility = VISIBLE
            placeholder.text = buildString {
                append(if (title.isBlank()) "Page suspended" else title)
                append("\n\nSelect this page to restore it")
            }
            placeholder.visibility = VISIBLE
        }

        fun showUnavailable(title: String) {
            page.removeAllViews()
            page.visibility = VISIBLE
            placeholder.text = if (title.isBlank()) "Page unavailable" else title
            placeholder.visibility = VISIBLE
        }

        fun showFullscreen(view: View) {
            (view.parent as? ViewGroup)?.removeView(view)
            custom.removeAllViews()
            custom.addView(view, LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT))
            custom.visibility = VISIBLE
            page.visibility = INVISIBLE
            placeholder.visibility = GONE
        }

        fun hideFullscreen() {
            custom.removeAllViews()
            custom.visibility = GONE
            page.visibility = VISIBLE
        }

        fun remove(webView: WebView) {
            if (webView.parent === page) page.removeView(webView)
        }
    }

    private val containers = LinkedHashMap<Long, PaneContainer>()
    private val live = LinkedHashMap<Long, LivePane>()
    private val frozen = LinkedHashMap<Long, Bundle>()
    private val fallbackUrls = LinkedHashMap<Long, String>()
    private val pointerPaint = android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG).apply {
        color = android.graphics.Color.WHITE
        style = android.graphics.Paint.Style.STROKE
        strokeWidth = 3f
        setShadowLayer(3f, 0f, 0f, android.graphics.Color.BLACK)
    }
    private var pointerX = 0
    private var pointerY = 0
    private var pointerVisible = false
    private val paneGapPixels = (4f * resources.displayMetrics.density).toInt().coerceAtLeast(1)

    fun setPointer(x: Int, y: Int, visible: Boolean) {
        pointerX = x
        pointerY = y
        pointerVisible = visible
        invalidate()
    }

    override fun dispatchDraw(canvas: android.graphics.Canvas) {
        super.dispatchDraw(canvas)
        if (!pointerVisible) return
        val pane = containers[focusedId] ?: return
        canvas.save()
        canvas.clipRect(pane.left, pane.top, pane.right, pane.bottom)
        canvas.drawCircle((pane.left + pointerX).toFloat(), (pane.top + pointerY).toFloat(), 10f, pointerPaint)
        canvas.restore()
    }
    private val fullscreenIds = LinkedHashSet<Long>()

    private var visible = emptyList<ReceiverWorkspacePaneRender>()
    private var layout = ReceiverWorkspaceLayout.SINGLE
    private var focusedId: Long = 0L
    private var split = BrowserWorkspaceSplit.Even
    private var pageFocusEnabled = true

    init {
        layoutParams = ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT,
        )
        setBackgroundColor(Color.BLACK)
        clipChildren = true
        clipToPadding = true
    }

    override fun create(id: Long, generation: Long, url: String?) {
        if (live.containsKey(id)) return
        val container = containerFor(id)
        val controller = paneFullscreenController(id, generation, container)
        val webView = newWebView(id)
        val driver = try {
            buildDriver(webView, id, generation, controller)
        } catch (error: Exception) {
            webView.destroy()
            throw error
        }
        live[id] = LivePane(webView, driver, controller)
        url?.let { fallbackUrls[id] = it }
    }

    override fun restore(id: Long, generation: Long): Boolean {
        if (live.containsKey(id)) return true
        create(id, generation, fallbackUrls[id])
        val webView = live.getValue(id).webView
        return frozen.remove(id)?.let { bundle ->
            runCatching { webView.restoreState(bundle) != null }.getOrDefault(false)
        } ?: false
    }

    /** The fallback URL is intentionally exposed only to the owning workspace session. */
    fun fallbackUrl(id: Long): String? = fallbackUrls[id]

    override fun driverFor(id: Long): BrowserWebViewDriver? = live[id]?.driver

    override fun previewCaptureView(): View = this

    override fun freeze(id: Long) {
        val pane = live.remove(id) ?: return
        pane.fullscreen.abandon()
        fullscreenIds.remove(id)
        val bundle = Bundle()
        val saved = runCatching { pane.webView.saveState(bundle) }.getOrNull()
        pane.webView.url?.let { fallbackUrls[id] = it }
        if (saved != null && bundleSize(bundle) <= MAX_SAVED_STATE_BYTES) {
            frozen[id] = bundle
        } else {
            onStateTooLarge(id)
        }
        containers[id]?.remove(pane.webView)
        BrowserSecurityProfile.detach(pane.webView)
        pane.webView.destroy()
        updateKeepScreenOn()
    }

    override fun destroy(id: Long) {
        frozen.remove(id)
        fallbackUrls.remove(id)
        live.remove(id)?.let { pane ->
            pane.fullscreen.abandon()
            containers[id]?.remove(pane.webView)
            BrowserSecurityProfile.detach(pane.webView)
            pane.webView.destroy()
        }
        fullscreenIds.remove(id)
        containers.remove(id)?.let(::removeView)
        updateKeepScreenOn()
    }

    override fun exitFullscreen(id: Long): Boolean = live[id]?.fullscreen?.exit() == true

    fun exitFocusedFullscreen(): Boolean = exitFullscreen(focusedId)

    override fun setPageFocusEnabled(enabled: Boolean) {
        pageFocusEnabled = enabled
        live.forEach { (id, pane) ->
            val acceptsFocus = enabled && id == focusedId
            pane.webView.isFocusable = acceptsFocus
            pane.webView.isFocusableInTouchMode = acceptsFocus
            if (acceptsFocus && !pane.webView.hasFocus()) {
                pane.webView.requestFocus()
            } else if (!acceptsFocus) {
                pane.webView.clearFocus()
            }
        }
    }

    override fun render(state: BrowserWorkspaceState) {
        val theater = state.theaterPaneId
        val rendered = state.panes.sortedBy { it.slot }.filter { theater == null || it.id == theater }
        render(
            panes = rendered.map { pane ->
                ReceiverWorkspacePaneRender(
                    pane.id,
                    pane.page.title.ifBlank { pane.page.url },
                    pane.rendererResidency.hasRenderer,
                    pane.isSuspended,
                )
            },
            nextLayout = if (theater != null) {
                ReceiverWorkspaceLayout.SINGLE
            } else {
                when (state.layout) {
                    BrowserWorkspaceLayout.SINGLE -> ReceiverWorkspaceLayout.SINGLE
                    BrowserWorkspaceLayout.SPLIT_HORIZONTAL -> ReceiverWorkspaceLayout.SIDE_BY_SIDE
                    BrowserWorkspaceLayout.SPLIT_VERTICAL -> ReceiverWorkspaceLayout.STACKED
                    BrowserWorkspaceLayout.GRID_2X2 -> ReceiverWorkspaceLayout.GRID
                }
            },
            nextFocusedId = state.focusedPaneId,
            nextSplit = state.split,
        )
        setPageFocusEnabled(state.interactionMode == BrowserWorkspaceInteractionMode.PAGE)
    }

    /** Applies native pane positions and the truthful suspended/live representation. */
    fun render(
        panes: List<ReceiverWorkspacePaneRender>,
        nextLayout: ReceiverWorkspaceLayout,
        nextFocusedId: Long,
        nextSplit: BrowserWorkspaceSplit = BrowserWorkspaceSplit.Even,
    ) {
        visible = panes
        layout = nextLayout
        split = nextSplit
        focusedId = nextFocusedId
        val ids = panes.map { it.id }.toSet()
        containers.forEach { (id, container) -> container.visibility = if (id in ids) VISIBLE else GONE }
        panes.forEach { pane ->
            val container = containerFor(pane.id)
            val active = live[pane.id]
            when {
                pane.live && active != null -> container.showPage(active.webView)
                pane.suspended -> container.showSuspended(pane.title)
                else -> container.showUnavailable(pane.title)
            }
        }
        relayout()
        setPageFocusEnabled(pageFocusEnabled)
    }

    override fun destroyAll() {
        live.keys.toList().forEach(::destroy)
        frozen.clear()
        fallbackUrls.clear()
        containers.values.toList().forEach(::removeView)
        containers.clear()
        visible = emptyList()
        fullscreenIds.clear()
        updateKeepScreenOn()
    }

    override fun onSizeChanged(width: Int, height: Int, oldWidth: Int, oldHeight: Int) {
        super.onSizeChanged(width, height, oldWidth, oldHeight)
        relayout()
    }

    private fun containerFor(id: Long): PaneContainer = containers.getOrPut(id) {
        PaneContainer(context).also { container ->
            addView(container, LayoutParams(0, 0))
        }
    }

    private fun paneFullscreenController(id: Long, generation: Long, container: PaneContainer): PaneFullscreenController<View> =
        PaneFullscreenController(
            host = object : PaneFullscreenHost<View> {
                override fun showInPane(view: View) {
                    container.showFullscreen(view)
                    fullscreenIds += id
                    updateKeepScreenOn()
                    onFullscreenChanged(id, generation, true)
                }

                override fun hideInPane() {
                    container.hideFullscreen()
                    fullscreenIds -= id
                    updateKeepScreenOn()
                    onFullscreenChanged(id, generation, false)
                }

                override fun setKeepScreenOn(enabled: Boolean) {
                    if (enabled) fullscreenIds += id else fullscreenIds -= id
                    updateKeepScreenOn()
                }

                // Page fullscreen remains inside a pane. Theatre mode is a separate, explicit
                // workspace action and must not be smuggled through a WebView custom-view request.
                override fun setImmersive(enabled: Boolean) = Unit
            },
            theaterMode = { false },
            canEnter = { id == focusedId && fullscreenIds.isEmpty() },
        )

    private fun newWebView(id: Long): WebView = EditingAwareWebView(context) { editing ->
        onEditingChanged(id, editing)
    }.apply {
        layoutParams = LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT)
        isFocusable = pageFocusEnabled && id == focusedId
        isFocusableInTouchMode = isFocusable
    }

    private fun relayout() {
        if (width <= 0 || height <= 0 || visible.isEmpty()) return
        val frames = BrowserWorkspaceLayoutFrames.frames(
            layout = layout.toWorkspaceLayout(),
            paneCount = visible.size,
            width = width,
            height = height,
            gapPixels = paneGapPixels,
            split = split,
        )
        visible.forEachIndexed { index, pane ->
            val frame = frames.getOrNull(index) ?: return@forEachIndexed
            containers[pane.id]?.layoutParams = LayoutParams(frame.width, frame.height).apply {
                leftMargin = frame.left
                topMargin = frame.top
            }
        }
        requestLayout()
    }

    /**
     * Maps a mosaic-root pixel into the pane under that point (gaps miss).
     * Used for Windows preview pointer/scroll so clicks reach the page that was drawn there.
     */
    override fun hitTestMosaic(x: Int, y: Int): Pair<Long, Pair<Int, Int>>? {
        if (width <= 0 || height <= 0 || visible.isEmpty()) return null
        val hit = BrowserWorkspaceLayoutFrames.hitTest(
            layout = layout.toWorkspaceLayout(),
            paneCount = visible.size,
            width = width,
            height = height,
            x = x,
            y = y,
            gapPixels = paneGapPixels,
            split = split,
        ) ?: return null
        val pane = visible.getOrNull(hit.slot) ?: return null
        return pane.id to (hit.localX to hit.localY)
    }

    fun mosaicGapPixels(): Int = paneGapPixels

    private fun updateKeepScreenOn() {
        keepScreenOn = fullscreenIds.isNotEmpty()
    }

    private fun bundleSize(bundle: Bundle): Int {
        val parcel = android.os.Parcel.obtain()
        return try {
            parcel.writeBundle(bundle)
            parcel.dataSize()
        } finally {
            parcel.recycle()
        }
    }
}
