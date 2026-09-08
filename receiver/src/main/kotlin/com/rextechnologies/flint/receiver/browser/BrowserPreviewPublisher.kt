package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.protocol.BinaryData
import com.rextechnologies.flint.protocol.wire.BrowserPreviewMessage
import com.rextechnologies.flint.receiver.browser.net.BrowserOutboundMessage
import com.rextechnologies.flint.receiver.browser.net.BrowserTlsServer

/**
 * Opt-in, newest-only JPEG preview publisher. Control/state always pre-empt this path: a full
 * mailbox slot is replaced, never queued behind commands.
 */
class BrowserPreviewPublisher(
    private val mailbox: BrowserPreviewMailbox = BrowserPreviewMailbox(),
    private val send: (BrowserOutboundMessage) -> Boolean,
) {
    @Volatile
    private var enabled: Boolean = false

    /** Last JPEG frame id successfully offered to the host; 0 when preview is off or never sent. */
    @Volatile
    var lastPublishedFrameId: Long = 0
        private set

    fun setEnabled(enabled: Boolean) {
        this.enabled = enabled
        if (!enabled) {
            mailbox.clear()
            lastPublishedFrameId = 0
        }
    }

    fun offerJpeg(
        epoch: Long,
        navigationId: Long,
        frameId: Long,
        width: Int,
        height: Int,
        jpeg: ByteArray,
    ): Boolean {
        if (!enabled) return false
        val payload = ByteArrayPreviewPayload(jpeg)
        val offer = mailbox.offer(
            BrowserPreviewFrame(epoch, navigationId, frameId, width, height, payload),
        )
        if (offer !is BrowserPreviewOffer.Accepted) {
            return false
        }
        val frame = mailbox.take() ?: return false
        return try {
            val sent = send(
                BrowserOutboundMessage.Preview(
                    BrowserPreviewMessage(
                        epoch = frame.epoch,
                        navigationId = frame.navigationId,
                        frameId = frame.frameId,
                        width = frame.width,
                        height = frame.height,
                        jpeg = BinaryData.of((frame.payload as ByteArrayPreviewPayload).bytes),
                    ),
                ),
            )
            if (sent) {
                lastPublishedFrameId = frame.frameId
            }
            sent
        } finally {
            frame.payload.release()
        }
    }
}

/** Test/production JPEG payload that owns a copied byte array until release. */
class ByteArrayPreviewPayload(source: ByteArray) : BrowserPreviewPayload {
    val bytes: ByteArray = source.copyOf()
    override val byteCount: Int = bytes.size
    override val format: BrowserPreviewFormat = BrowserPreviewFormat.JPEG
    override fun release() = Unit
}

/** Convenience factory bound to a live TLS server writer. */
fun BrowserPreviewPublisher(server: BrowserTlsServer): BrowserPreviewPublisher =
    BrowserPreviewPublisher(send = server::send)
