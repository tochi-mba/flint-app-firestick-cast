package com.rextechnologies.flint.receiver.media

import java.io.File
import java.nio.file.Files
import java.security.MessageDigest
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** The rules a pushed file lives by, without a television or a socket. */
class PushedMediaSinkTest {
    private val directory: File = Files.createTempDirectory("pushed").toFile()

    private fun sink(free: Long = Long.MAX_VALUE, clock: () -> Long = System::nanoTime) =
        PushedMediaSink(directory, freeBytes = { free }, clock = clock)

    private fun source(size: Int): ByteArray = ByteArray(size) { (it * 13 + 5).toByte() }

    private fun sha256(bytes: ByteArray): ByteArray = MessageDigest.getInstance("SHA-256").digest(bytes)

    @Test
    fun `the bytes on disk are the bytes that were sent, whatever the chunking`() {
        val bytes = source(1_000_003)
        listOf(1, 7, 4_096, 512 * 1024, bytes.size).forEach { chunk ->
            val sink = sink()
            var at = 0
            var completed: File? = null
            while (at < bytes.size) {
                val length = minOf(chunk, bytes.size - at)
                val outcome = sink.accept(bytes, at, length, isFinal = at + length == bytes.size)
                at += length
                if (outcome is PushedMediaSink.Outcome.Completed) completed = outcome.file
            }
            val file = assertNotNull(completed, "chunk $chunk")
            assertContentEquals(sha256(bytes), sha256(file.readBytes()), "chunk $chunk")
            assertEquals(file, sink.pending)
            sink.discard()
        }
    }

    @Test
    fun `a new transfer replaces the file that was pending, and a partial one never outlives its failure`() {
        var tick = 0L
        val sink = sink(clock = { tick++ })
        val first = assertIs<PushedMediaSink.Outcome.Completed>(sink.accept(source(10), isFinal = true)).file
        assertTrue(first.exists())

        assertEquals(PushedMediaSink.Outcome.Accepted, sink.accept(source(10), isFinal = false))
        assertFalse(first.exists(), "the pending file should go when the next transfer begins")
        assertNull(sink.pending)
        assertEquals(10, sink.bytesReceived)

        sink.discard()
        assertEquals(0, sink.bytesReceived)
        assertTrue(directory.listFiles().orEmpty().isEmpty(), "nothing should be left behind")
    }

    @Test
    fun `too little free space refuses the transfer and leaves nothing on disk`() {
        val sink = sink(free = PushedMediaSink.MINIMUM_FREE_BYTES - 1)
        val refused = assertIs<PushedMediaSink.Outcome.Refused>(sink.accept(source(10), isFinal = false))
        assertEquals(PushedMediaSink.NO_SPACE, refused.reason)
        assertTrue(directory.listFiles().orEmpty().isEmpty())
    }

    @Test
    fun `space is checked for every chunk, not only the first`() {
        var free = PushedMediaSink.MINIMUM_FREE_BYTES + 100
        val sink = PushedMediaSink(directory, freeBytes = { free })
        assertEquals(PushedMediaSink.Outcome.Accepted, sink.accept(source(50), isFinal = false))
        free = PushedMediaSink.MINIMUM_FREE_BYTES + 10
        assertIs<PushedMediaSink.Outcome.Refused>(sink.accept(source(50), isFinal = false))
        assertTrue(directory.listFiles().orEmpty().isEmpty(), "the partial file should be gone")
        assertNull(sink.pending)
    }

    @Test
    fun `discarding with nothing pending is harmless`() {
        val sink = sink()
        sink.discard()
        assertNull(sink.pending)
    }
}
