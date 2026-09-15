package com.rextechnologies.flint.receiver.browser

/**
 * Newest-only JPEG offer helper for the opt-in preview path. PixelCopy/encode live in the Activity
 * capture loop; this type only applies size and enablement gates before publishing.
 */
class BrowserPreviewCapture(
    private val publisher: BrowserPreviewPublisher,
    private val maxBytes: Int = MAX_JPEG_BYTES,
    private val maxWidth: Int = BrowserPreviewLoop.MAX_WIDTH,
    private val maxHeight: Int = BrowserPreviewLoop.MAX_HEIGHT,
) {
    companion object {
        /** Soft gate before publish; capability advertises the same ceiling. */
        const val MAX_JPEG_BYTES = 180_000
    }
    fun offerEncodedFrame(
        epoch: Long,
        navigationId: Long,
        frameId: Long,
        width: Int,
        height: Int,
        jpeg: ByteArray,
    ): Boolean {
        if (width !in 1..maxWidth || height !in 1..maxHeight) return false
        if (jpeg.isEmpty() || jpeg.size > maxBytes) return false
        return publisher.offerJpeg(epoch, navigationId, frameId, width, height, jpeg)
    }
}
