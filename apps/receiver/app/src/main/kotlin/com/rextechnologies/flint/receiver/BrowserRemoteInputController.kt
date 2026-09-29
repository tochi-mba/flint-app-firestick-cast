package com.rextechnologies.flint.receiver

import android.util.Log
import com.rextechnologies.flint.protocol.wire.BrowserInputMessage
import com.rextechnologies.flint.protocol.wire.BrowserPointerAction
import com.rextechnologies.flint.receiver.browser.BrowserCoordinator
import com.rextechnologies.flint.receiver.browser.BrowserInputMapping
import com.rextechnologies.flint.receiver.browser.BrowserInputRouter
import com.rextechnologies.flint.receiver.browser.BrowserNativeInput

/** Maps authenticated host input against the current page and publishes its optional TV cursor. */
internal class BrowserRemoteInputController(
    private val coordinator: BrowserCoordinator,
    private val inputRouter: BrowserInputRouter,
    private val lastPreviewFrameId: () -> Long = { 0L },
    private val publishCursor: (BrowserCursorUi) -> Unit,
) {
    fun handle(input: BrowserInputMessage) {
        val page = coordinator.snapshot()
        val viewport = coordinator.viewport()
        val previewFrameId = lastPreviewFrameId()
        val mapping = inputRouter.handle(
            input,
            currentNavigationId = page.navigationId,
            // Prefer the last JPEG frame the host actually saw; fall back to navigation for legacy.
            currentFrameId = if (previewFrameId > 0L) previewFrameId else page.navigationId,
            viewportWidth = viewport?.first ?: 0,
            viewportHeight = viewport?.second ?: 0,
        )
        if (mapping !is BrowserInputMapping.Accepted) {
            Log.i(TAG, "Secure browser input rejected sequence=${input.sequence}")
            return
        }
        when (val native = mapping.input) {
            is BrowserNativeInput.Pointer -> {
                if (native.action != BrowserPointerAction.MOVE) {
                    Log.i(
                        TAG,
                        "Secure browser input pointer seq=${input.sequence} action=${native.action} x=${native.x} y=${native.y}",
                    )
                }
            }
            is BrowserNativeInput.Scroll ->
                Log.i(
                    TAG,
                    "Secure browser input scroll seq=${input.sequence} dx=${native.deltaX} dy=${native.deltaY}",
                )
            is BrowserNativeInput.KeyStroke ->
                Log.i(TAG, "Secure browser input key seq=${input.sequence} key=${native.key}")
            is BrowserNativeInput.ComposedText ->
                Log.i(
                    TAG,
                    "Secure browser input text seq=${input.sequence} utf8Bytes=${native.text.utf8Bytes}",
                )
        }
        coordinator.dispatchInput(mapping.input)
        cursor(mapping.input, viewport)?.let(publishCursor)
    }

    private fun cursor(input: BrowserNativeInput, viewport: Pair<Int, Int>?): BrowserCursorUi? {
        val width = viewport?.first ?: return null
        val height = viewport.second
        if (width <= 0 || height <= 0) return null
        val (x, y, pressed) = when (input) {
            is BrowserNativeInput.Pointer -> Triple(
                input.x,
                input.y,
                input.action == BrowserPointerAction.DOWN ||
                    (input.action == BrowserPointerAction.MOVE && input.buttons == 1),
            )
            is BrowserNativeInput.Scroll -> Triple(input.x, input.y, false)
            else -> return null
        }
        return BrowserCursorUi(
            visible = true,
            xFraction = (x.toFloat() / (width - 1).coerceAtLeast(1)).coerceIn(0f, 1f),
            yFraction = (y.toFloat() / (height - 1).coerceAtLeast(1)).coerceIn(0f, 1f),
            pressed = pressed,
        )
    }

    private companion object {
        const val TAG = "FlintBrowser"
    }
}
