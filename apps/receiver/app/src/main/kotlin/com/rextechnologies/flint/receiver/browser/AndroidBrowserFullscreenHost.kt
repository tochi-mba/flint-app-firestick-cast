package com.rextechnologies.flint.receiver.browser

import android.app.Activity
import android.content.Context
import android.content.ContextWrapper
import android.view.View
import android.view.WindowManager
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat

/** A browser container that can place Chromium's custom fullscreen view above its page. */
interface BrowserFullscreenViewHost {
    val fullscreenContext: Context
    fun showFullscreen(view: View)
    fun hideFullscreen()
}

/**
 * Binds the fullscreen policy to a real window.
 *
 * Separate from [BrowserFullscreenController] so the ordering rules stay testable without a device:
 * this file is the only part that needs an `Activity`, and it does nothing but obey.
 */
class AndroidBrowserFullscreenHost(
    private val host: BrowserFullscreenViewHost,
    private val onChanged: (Boolean) -> Unit = {},
) : BrowserFullscreenHost<View> {
    override fun showFullscreenView(view: View) {
        host.showFullscreen(view)
        onChanged(true)
    }

    override fun hideFullscreenView() {
        host.hideFullscreen()
        onChanged(false)
    }

    override fun setImmersive(enabled: Boolean) {
        val activity = host.fullscreenContext.findActivity() ?: return
        val controller = WindowInsetsControllerCompat(activity.window, activity.window.decorView)
        if (enabled) {
            controller.systemBarsBehavior =
                WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            controller.hide(WindowInsetsCompat.Type.systemBars())
        } else {
            controller.show(WindowInsetsCompat.Type.systemBars())
        }
        WindowCompat.setDecorFitsSystemWindows(activity.window, !enabled)
    }

    /**
     * Holds the display awake while a video plays.
     *
     * A playing video is not user activity as far as Fire OS is concerned, so without this the
     * screen dims part-way through anything longer than the display timeout.
     */
    override fun setKeepScreenOn(enabled: Boolean) {
        val window = host.fullscreenContext.findActivity()?.window ?: return
        if (enabled) {
            window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        } else {
            window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        }
    }
}

/** Walks the Compose context chain out to the Activity that owns the window. */
internal fun Context.findActivity(): Activity? {
    var current: Context? = this
    while (current is ContextWrapper) {
        if (current is Activity) return current
        current = current.baseContext
    }
    return null
}
