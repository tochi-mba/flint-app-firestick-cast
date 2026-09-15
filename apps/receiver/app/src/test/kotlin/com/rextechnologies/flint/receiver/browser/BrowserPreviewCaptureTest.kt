package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class BrowserPreviewCaptureTest {
    @Test
    fun `oversized frames are rejected before publish`() {
        val sent = mutableListOf<Int>()
        val publisher = BrowserPreviewPublisher(send = {
            sent += 1
            true
        })
        publisher.setEnabled(true)
        val capture = BrowserPreviewCapture(publisher, maxBytes = 16)

        assertFalse(
            capture.offerEncodedFrame(1, 1, 1, 320, 180, ByteArray(32)),
        )
        assertTrue(sent.isEmpty())
    }

    @Test
    fun `bounded frames are offered when preview is enabled`() {
        val sent = mutableListOf<Int>()
        val publisher = BrowserPreviewPublisher(send = {
            sent += 1
            true
        })
        publisher.setEnabled(true)
        val capture = BrowserPreviewCapture(publisher)

        assertTrue(capture.offerEncodedFrame(1, 1, 1, 320, 180, ByteArray(32) { 0xFF.toByte() }))
        assertTrue(sent.single() == 1)
    }
}
