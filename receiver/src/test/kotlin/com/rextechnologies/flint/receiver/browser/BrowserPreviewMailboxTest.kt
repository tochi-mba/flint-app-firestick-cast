package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertNull
import kotlin.test.assertTrue

class BrowserPreviewMailboxTest {
    @Test
    fun `one slot replaces and releases stale preview without a queue`() {
        val mailbox = BrowserPreviewMailbox()
        val firstPayload = RecordingPayload(BrowserPreviewMailbox.MAX_BYTES)
        val secondPayload = RecordingPayload(100)
        val first = frame(1, firstPayload)
        val second = frame(2, secondPayload)

        assertIs<BrowserPreviewOffer.Accepted>(mailbox.offer(first))
        val replacement = assertIs<BrowserPreviewOffer.Accepted>(mailbox.offer(second))
        assertTrue(replacement.replaced)
        assertEquals(1, firstPayload.releases)
        assertEquals(1, mailbox.size)

        assertEquals(second, mailbox.take())
        assertEquals(0, secondPayload.releases)
        assertNull(mailbox.take())
    }

    @Test
    fun `strictly rejects dimension pixel byte type and identifier boundary violations`() {
        val invalids = listOf(
            frame(1, RecordingPayload(1), width = 961),
            frame(1, RecordingPayload(1), height = 541),
            frame(1, RecordingPayload(BrowserPreviewMailbox.MAX_BYTES + 1)),
            frame(1, RecordingPayload(1, BrowserPreviewFormat.OTHER)),
            frame(0, RecordingPayload(1)),
        )

        invalids.forEach { candidate ->
            val result = assertIs<BrowserPreviewOffer.Rejected>(BrowserPreviewMailbox().offer(candidate))
            assertTrue(result.reason != PreviewRejection.NONE)
            assertEquals(1, (candidate.payload as RecordingPayload).releases)
        }
    }

    @Test
    fun `clear releases the only owned candidate exactly once`() {
        val mailbox = BrowserPreviewMailbox()
        val payload = RecordingPayload(100)
        mailbox.offer(frame(1, payload))
        mailbox.clear()
        mailbox.clear()

        assertEquals(1, payload.releases)
        assertEquals(0, mailbox.size)
    }

    private fun frame(id: Long, payload: RecordingPayload, width: Int = 960, height: Int = 540) =
        BrowserPreviewFrame(epoch = 1, navigationId = 1, frameId = id, width = width, height = height, payload = payload)

    private class RecordingPayload(
        override val byteCount: Int,
        override val format: BrowserPreviewFormat = BrowserPreviewFormat.JPEG,
    ) : BrowserPreviewPayload {
        var releases = 0
        override fun release() {
            releases += 1
        }
    }
}
