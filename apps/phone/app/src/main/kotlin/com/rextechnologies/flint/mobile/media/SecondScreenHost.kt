package com.rextechnologies.flint.mobile.media

import android.app.Activity
import android.app.Presentation
import android.content.Context
import android.hardware.display.DisplayManager
import android.hardware.display.VirtualDisplay
import android.os.Bundle
import android.os.Looper
import android.view.Surface
import android.view.View
import androidx.compose.runtime.Composable
import androidx.compose.ui.platform.ComposeView
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.ViewModelStore
import androidx.lifecycle.ViewModelStoreOwner
import androidx.lifecycle.setViewTreeLifecycleOwner
import androidx.lifecycle.setViewTreeViewModelStoreOwner
import androidx.savedstate.SavedStateRegistry
import androidx.savedstate.SavedStateRegistryController
import androidx.savedstate.SavedStateRegistryOwner
import androidx.savedstate.setViewTreeSavedStateRegistryOwner
import java.io.Closeable

/**
 * The second screen: a display only this app can see, with Flint's own content drawn on it.
 *
 * The display is created with no flags. `VIRTUAL_DISPLAY_FLAG_PUBLIC` is the one that would pull in
 * `CAPTURE_VIDEO_OUTPUT`, a system permission this app has no business holding, and a private display
 * needs none — which is the whole reason the second screen is the lower-friction of the two modes and
 * the reason it ships first.
 *
 * What it cannot do is as important as what it can. A `VirtualDisplay` shows only what its owner
 * draws into it. This cannot extend Android itself onto the television and cannot put another app's
 * window there, and the capability card says so in the same breath as it offers the mode.
 */
class SecondScreenHost(
    private val activity: Activity,
    private val width: Int,
    private val height: Int,
    private val densityDpi: Int,
) : Closeable {
    private var display: VirtualDisplay? = null
    private var presentation: ComposePresentation? = null

    /**
     * Starts drawing [content] onto a private display backed by [surface].
     *
     * [surface] is the encoder's input surface, so the composition is encoded directly with no copy
     * through application memory at any point.
     */
    fun start(surface: Surface, content: @Composable () -> Unit): Result<Unit> = runCatching {
        // A Presentation is a Dialog, and show() on any thread but the main one throws from deep
        // inside the window manager with a message about a Looper rather than about a dialog. This
        // is reached from the media path, where the surrounding code is on a background dispatcher,
        // so the mistake is an easy one and the diagnosis is not.
        check(Looper.myLooper() == Looper.getMainLooper()) {
            "The second screen must be started on the main thread; a Presentation is a Dialog"
        }
        check(display == null) { "The second screen is already running" }
        val manager = activity.getSystemService(DisplayManager::class.java)
            ?: error("This phone has no display manager")

        val created = manager.createVirtualDisplay(
            DISPLAY_NAME,
            width,
            height,
            densityDpi,
            surface,
            0,
        ) ?: error("This phone refused to create the private display")
        display = created

        val shown = ComposePresentation(activity, created.display, content)
        presentation = shown
        shown.show()
    }.onFailure {
        // start() is transactional. If the Presentation cannot be shown, neither the private display
        // nor its lifecycle owner may survive as an unreachable half-started session.
        close()
    }

    /**
     * Points the private display at another surface, keeping the presentation as it is.
     *
     * This is how an encoder is swapped underneath a running second screen -- for the key-frame
     * fallback, which can only be applied by configuring a new codec -- without the television
     * seeing the presentation torn down and rebuilt. Returns `false` when nothing is running.
     */
    fun retarget(surface: Surface): Boolean {
        val active = display ?: return false
        active.surface = surface
        return true
    }

    override fun close() {
        // Dismissed first, so the presentation's own onStop runs while it is still alive, and only
        // then destroyed. A Presentation that had already been destroyed could not be shown again.
        runCatching { presentation?.dismiss() }
        runCatching { presentation?.destroy() }
        runCatching { display?.release() }
        presentation = null
        display = null
    }

    private companion object {
        const val DISPLAY_NAME = "flint-second-screen"
    }
}

/**
 * A `Presentation` that can host Compose.
 *
 * The trap this exists to close: a `Presentation` is a `Dialog`, so its decor view has none of the
 * three owners a `ComposeView` looks for — `ViewTreeLifecycleOwner`,
 * `ViewTreeSavedStateRegistryOwner` and `ViewTreeViewModelStoreOwner`. Without all three, the first
 * composition throws, and the exception names a lifecycle rather than a dialog, so it reads as a
 * problem somewhere else entirely. It is wrapped once, here, rather than remembered at each call
 * site.
 */
private class ComposePresentation(
    outerContext: Context,
    display: android.view.Display,
    private val content: @Composable () -> Unit,
) : Presentation(outerContext, display), LifecycleOwner, ViewModelStoreOwner, SavedStateRegistryOwner {
    private val registry = LifecycleRegistry(this)
    private val savedState = SavedStateRegistryController.create(this)
    private val store = ViewModelStore()

    override val lifecycle: Lifecycle get() = registry
    override val viewModelStore: ViewModelStore get() = store
    override val savedStateRegistry: SavedStateRegistry get() = savedState.savedStateRegistry

    override fun onCreate(savedInstanceState: Bundle?) {
        savedState.performRestore(null)
        super.onCreate(savedInstanceState)

        val view = ComposeView(context).apply { setContent(content) }
        setContentView(view)
        attachOwners(window?.decorView ?: view)
        registry.handleLifecycleEvent(Lifecycle.Event.ON_CREATE)
    }

    private fun attachOwners(root: View) {
        root.setViewTreeLifecycleOwner(this)
        root.setViewTreeViewModelStoreOwner(this)
        root.setViewTreeSavedStateRegistryOwner(this)
    }

    override fun onStart() {
        super.onStart()
        registry.handleLifecycleEvent(Lifecycle.Event.ON_RESUME)
    }

    override fun onStop() {
        // ON_STOP, not ON_DESTROY. DESTROYED is terminal in a LifecycleRegistry: once dispatched,
        // the next ON_CREATE throws, so a presentation that was hidden and shown again -- which is
        // what a second screen does every time the television's input changes and comes back --
        // crashed as its content attached.
        registry.handleLifecycleEvent(Lifecycle.Event.ON_STOP)
        super.onStop()
    }

    /** The end of this presentation's life, dispatched by its owner rather than by being hidden. */
    fun destroy() {
        registry.handleLifecycleEvent(Lifecycle.Event.ON_DESTROY)
        store.clear()
    }
}
