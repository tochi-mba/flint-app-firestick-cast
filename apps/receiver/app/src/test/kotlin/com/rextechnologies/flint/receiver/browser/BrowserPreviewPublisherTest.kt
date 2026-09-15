package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class BrowserPreviewPublisherTest {
    @Test
    fun `disabled publisher never sends`() {
        var sent = false
        val publisher = BrowserPreviewPublisher(send = {
            sent = true
            true
        })

        assertFalse(
            publisher.offerJpeg(1, 1, 1, 2, 2, byteArrayOf(0xff.toByte(), 0xd8.toByte(), 0xff.toByte(), 0xd9.toByte())),
        )
        assertFalse(sent)
    }

    @Test
    fun `enabled publisher sends a bounded jpeg and replaces older candidates`() {
        val sent = mutableListOf<Int>()
        val publisher = BrowserPreviewPublisher(send = { outbound ->
            val preview = (outbound as com.rextechnologies.flint.receiver.browser.net.BrowserOutboundMessage.Preview).message
            sent += preview.frameId.toInt()
            true
        })
        publisher.setEnabled(true)

        assertTrue(publisher.offerJpeg(1, 1, 1, 2, 2, jpeg(4)))
        assertTrue(publisher.offerJpeg(1, 1, 2, 2, 2, jpeg(8)))
        assertTrue(sent == listOf(1, 2))
        assertTrue(publisher.lastPublishedFrameId == 2L)
    }

    @Test
    fun `disabling clears last published frame id`() {
        val publisher = BrowserPreviewPublisher(send = { true })
        publisher.setEnabled(true)
        assertTrue(publisher.offerJpeg(1, 1, 9, 2, 2, jpeg(4)))
        assertTrue(publisher.lastPublishedFrameId == 9L)

        publisher.setEnabled(false)
        assertTrue(publisher.lastPublishedFrameId == 0L)
    }

    private fun jpeg(size: Int): ByteArray = ByteArray(size) { index ->
        when (index) {
            0 -> 0xff.toByte()
            1 -> 0xd8.toByte()
            size - 2 -> 0xff.toByte()
            size - 1 -> 0xd9.toByte()
            else -> 0x00
        }
    }
}
