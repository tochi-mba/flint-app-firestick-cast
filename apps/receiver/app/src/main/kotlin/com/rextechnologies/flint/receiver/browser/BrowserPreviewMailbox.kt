package com.rextechnologies.flint.receiver.browser

/** JPEG is the sole preview representation; no video/mirror fallback is permitted here. */
enum class BrowserPreviewFormat {
    JPEG,
    OTHER,
}

/** Ownership transfers to [BrowserPreviewMailbox.offer] until [BrowserPreviewMailbox.take] succeeds. */
interface BrowserPreviewPayload {
    val byteCount: Int
    val format: BrowserPreviewFormat
    fun release()
}

data class BrowserPreviewFrame(
    val epoch: Long,
    val navigationId: Long,
    val frameId: Long,
    val width: Int,
    val height: Int,
    val payload: BrowserPreviewPayload,
) {
    override fun toString(): String =
        "BrowserPreviewFrame(epoch=$epoch, navigationId=$navigationId, frameId=$frameId, " +
            "width=$width, height=$height, payload=<redacted bytes=${payload.byteCount}>)"
}

enum class PreviewRejection {
    NONE,
    INVALID_IDENTIFIER,
    INVALID_DIMENSIONS,
    PIXEL_LIMIT_EXCEEDED,
    INVALID_FORMAT,
    INVALID_BYTE_COUNT,
}

sealed interface BrowserPreviewOffer {
    data class Accepted(val replaced: Boolean) : BrowserPreviewOffer
    data class Rejected(val reason: PreviewRejection) : BrowserPreviewOffer
}

/**
 * A synchronised capacity-one handoff. It has no queue and releases only payloads it still owns.
 * The receiver's capture worker supplies the ordering/current-epoch guard before offering here.
 */
class BrowserPreviewMailbox {
    companion object {
        const val MAX_BYTES: Int = 768 * 1024
        const val MAX_WIDTH: Int = 960
        const val MAX_HEIGHT: Int = 540
        const val MAX_PIXELS: Int = MAX_WIDTH * MAX_HEIGHT
    }

    private var candidate: BrowserPreviewFrame? = null

    val size: Int
        @Synchronized get() = if (candidate == null) 0 else 1

    @Synchronized
    fun offer(frame: BrowserPreviewFrame): BrowserPreviewOffer {
        val rejection = validate(frame)
        if (rejection != PreviewRejection.NONE) {
            frame.payload.release()
            return BrowserPreviewOffer.Rejected(rejection)
        }

        val previous = candidate
        candidate = frame
        if (previous != null) {
            previous.payload.release()
        }
        return BrowserPreviewOffer.Accepted(replaced = previous != null)
    }

    /** Transfers ownership to the caller. The mailbox must not release a successfully taken frame. */
    @Synchronized
    fun take(): BrowserPreviewFrame? {
        val next = candidate
        candidate = null
        return next
    }

    @Synchronized
    fun clear() {
        val previous = candidate
        candidate = null
        previous?.payload?.release()
    }

    private fun validate(frame: BrowserPreviewFrame): PreviewRejection = when {
        frame.epoch <= 0 || frame.navigationId <= 0 || frame.frameId <= 0 ->
            PreviewRejection.INVALID_IDENTIFIER
        frame.width !in 1..MAX_WIDTH || frame.height !in 1..MAX_HEIGHT ->
            PreviewRejection.INVALID_DIMENSIONS
        frame.width.toLong() * frame.height.toLong() > MAX_PIXELS ->
            PreviewRejection.PIXEL_LIMIT_EXCEEDED
        frame.payload.format != BrowserPreviewFormat.JPEG ->
            PreviewRejection.INVALID_FORMAT
        frame.payload.byteCount !in 1..MAX_BYTES ->
            PreviewRejection.INVALID_BYTE_COUNT
        else -> PreviewRejection.NONE
    }
}
