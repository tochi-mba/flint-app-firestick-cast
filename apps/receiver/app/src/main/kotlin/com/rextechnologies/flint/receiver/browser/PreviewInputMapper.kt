package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.protocol.wire.BrowserPointerAction
import com.rextechnologies.flint.protocol.wire.BrowserPointerInput
import com.rextechnologies.flint.protocol.wire.BrowserScrollInput

/**
 * Maps preview-relative pointer/scroll events into WebView coordinates only when the referenced
 * navigation and frame are still current. Stale geometry is rejected rather than applied.
 */
class PreviewInputMapper(
    private val maxCoordinate: Int = 65_535,
) {
    fun mapPointer(
        input: BrowserPointerInput,
        currentNavigationId: Long,
        currentFrameId: Long,
        viewportWidth: Int,
        viewportHeight: Int,
    ): PreviewMappedInput {
        if (input.navigationId != currentNavigationId ||
            !frameReferenceMatches(input.frameId, currentFrameId, currentNavigationId)
        ) {
            return PreviewMappedInput.Rejected(PreviewInputRejection.STALE_REFERENCE)
        }
        if (viewportWidth <= 0 || viewportHeight <= 0) {
            return PreviewMappedInput.Rejected(PreviewInputRejection.VIEWPORT_UNKNOWN)
        }
        if (input.x !in 0..maxCoordinate || input.y !in 0..maxCoordinate) {
            return PreviewMappedInput.Rejected(PreviewInputRejection.OUT_OF_BOUNDS)
        }
        if (input.buttons !in 0..1) {
            return PreviewMappedInput.Rejected(PreviewInputRejection.INVALID_BUTTONS)
        }
        if (input.action == BrowserPointerAction.DOWN && input.buttons != 1) {
            return PreviewMappedInput.Rejected(PreviewInputRejection.INVALID_BUTTONS)
        }
        if (input.action == BrowserPointerAction.UP && input.buttons != 0) {
            return PreviewMappedInput.Rejected(PreviewInputRejection.INVALID_BUTTONS)
        }
        val x = ((input.x.toLong() * (viewportWidth - 1)) / maxCoordinate).toInt()
        val y = ((input.y.toLong() * (viewportHeight - 1)) / maxCoordinate).toInt()
        return PreviewMappedInput.Pointer(input.action, x, y, input.buttons)
    }

    fun mapScroll(
        input: BrowserScrollInput,
        currentNavigationId: Long,
        currentFrameId: Long,
        viewportWidth: Int,
        viewportHeight: Int,
    ): PreviewMappedInput {
        if (input.navigationId != currentNavigationId ||
            !frameReferenceMatches(input.frameId, currentFrameId, currentNavigationId)
        ) {
            return PreviewMappedInput.Rejected(PreviewInputRejection.STALE_REFERENCE)
        }
        if (viewportWidth <= 0 || viewportHeight <= 0) {
            return PreviewMappedInput.Rejected(PreviewInputRejection.VIEWPORT_UNKNOWN)
        }
        val x = ((input.x.toLong() * (viewportWidth - 1)) / maxCoordinate).toInt()
        val y = ((input.y.toLong() * (viewportHeight - 1)) / maxCoordinate).toInt()
        return PreviewMappedInput.Scroll(x, y, input.deltaX, input.deltaY)
    }

    /**
     * Accepts the latest published preview frame id, or the navigation id (legacy hosts that cited
     * navigation in both fields before interactive preview shipped).
     */
    private fun frameReferenceMatches(frameId: Long, currentFrameId: Long, currentNavigationId: Long): Boolean =
        frameId == currentFrameId || frameId == currentNavigationId
}

enum class PreviewInputRejection {
    STALE_REFERENCE,
    VIEWPORT_UNKNOWN,
    OUT_OF_BOUNDS,
    INVALID_BUTTONS,
}

sealed interface PreviewMappedInput {
    data class Pointer(
        val action: BrowserPointerAction,
        val x: Int,
        val y: Int,
        val buttons: Int,
    ) : PreviewMappedInput

    data class Scroll(
        val x: Int,
        val y: Int,
        val deltaX: Int,
        val deltaY: Int,
    ) : PreviewMappedInput

    data class Rejected(val reason: PreviewInputRejection) : PreviewMappedInput
}
