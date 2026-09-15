package com.rextechnologies.flint.receiver.browser

import android.graphics.Bitmap
import android.graphics.Canvas
import android.os.Handler
import android.os.Looper
import android.view.View
import java.io.ByteArrayOutputStream
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong

/**
 * Opt-in View → JPEG capture loop for the passive host preview.
 *
 * Draws the attached [View] into a bounded bitmap on the UI thread, encodes off-thread, and never
 * queues more than one in-flight capture. A busy slot skips rather than waits, so control traffic
 * stays ahead of preview.
 *
 * Software draw is intentional: [android.view.PixelCopy] needs a Window/Surface and would capture
 * unrelated chrome; drawing the target view (a single WebView, or the workspace mosaic root) is the
 * page/mosaic preview the host expects, and it works on the Fire OS API-25 floor.
 */
class BrowserPreviewLoop(
    private val capture: BrowserPreviewCapture,
    private val epoch: () -> Long,
    private val navigationId: () -> Long,
    private val interactiveIntervalMs: Long = 200L,
    private val idleIntervalMs: Long = 1_000L,
    private val jpegQuality: Int = 70,
    private val maxWidth: Int = MAX_WIDTH,
    private val maxHeight: Int = MAX_HEIGHT,
) {
    companion object {
        /** Advertised and enforced capture width ceiling. */
        const val MAX_WIDTH = 960

        /** Advertised and enforced capture height ceiling. */
        const val MAX_HEIGHT = 540

        /** ~200 ms interactive tick → 5 fps. */
        const val INTERACTIVE_FPS = 5

        /** ~1000 ms idle tick → 1 fps. */
        const val IDLE_FPS = 1
    }
    private val mainHandler = Handler(Looper.getMainLooper())
    private val enabled = AtomicBoolean(false)
    private val capturing = AtomicBoolean(false)
    private val nextFrameId = AtomicLong(1)
    private var target: View? = null
    private var interactive = true

    private val tick = object : Runnable {
        override fun run() {
            if (!enabled.get()) return
            maybeCapture()
            val interval = if (interactive) interactiveIntervalMs else idleIntervalMs
            mainHandler.postDelayed(this, interval)
        }
    }

    fun attach(view: View) {
        target = view
    }

    fun detach() {
        stop()
        target = null
    }

    fun setEnabled(value: Boolean) {
        if (value) start() else stop()
    }

    fun setInteractive(value: Boolean) {
        interactive = value
    }

    /** The view currently being drawn, if any — useful for tests and attachment diagnostics. */
    fun attachedTarget(): View? = target

    private fun start() {
        if (!enabled.compareAndSet(false, true)) return
        mainHandler.removeCallbacks(tick)
        mainHandler.post(tick)
    }

    private fun stop() {
        enabled.set(false)
        mainHandler.removeCallbacks(tick)
        capturing.set(false)
    }

    private fun maybeCapture() {
        val view = target ?: return
        if (view.width <= 0 || view.height <= 0) return
        if (!capturing.compareAndSet(false, true)) return

        val scale = minOf(
            1f,
            maxWidth.toFloat() / view.width.toFloat(),
            maxHeight.toFloat() / view.height.toFloat(),
        )
        val width = (view.width * scale).toInt().coerceAtLeast(1)
        val height = (view.height * scale).toInt().coerceAtLeast(1)
        val bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        val currentEpoch = epoch()
        val currentNavigation = navigationId()
        val frameId = nextFrameId.getAndIncrement()

        val drawn = try {
            val canvas = Canvas(bitmap)
            canvas.scale(scale, scale)
            view.draw(canvas)
            true
        } catch (_: RuntimeException) {
            false
        }

        if (!drawn) {
            bitmap.recycle()
            capturing.set(false)
            return
        }

        Thread({
            try {
                val stream = ByteArrayOutputStream()
                if (bitmap.compress(Bitmap.CompressFormat.JPEG, jpegQuality, stream)) {
                    capture.offerEncodedFrame(
                        currentEpoch,
                        currentNavigation,
                        frameId,
                        width,
                        height,
                        stream.toByteArray(),
                    )
                }
            } finally {
                bitmap.recycle()
                capturing.set(false)
            }
        }, "flint-browser-preview-encode").start()
    }
}
