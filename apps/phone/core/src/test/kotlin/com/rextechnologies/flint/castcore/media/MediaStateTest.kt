package com.rextechnologies.flint.castcore.media

import com.rextechnologies.flint.castcore.copy.MediaCopy
import com.rextechnologies.flint.protocol.wire.PlaybackState
import com.rextechnologies.flint.protocol.wire.PlaybackStateMessage
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertNull
import kotlin.test.assertTrue

private val holiday = MediaItem("holiday.mp4", "video/mp4", sizeBytes = 3_000_000, durationMs = 90_000)

class MediaStateTest {
    @Test
    fun `sending knows its fraction only when the provider said how long the file is`() {
        assertEquals(0.5, MediaState.Sending(holiday, 1_500_000, 3_000_000).fraction)
        assertNull(MediaState.Sending(holiday, 1_500_000, null).fraction)
        assertEquals(1.0, MediaState.Sending(holiday, 4_000_000, 3_000_000).fraction)
    }

    @Test
    fun `only the states with a television playing offer transport controls`() {
        assertTrue(MediaState.Playing(holiday, 0, 90_000).isPlayable)
        assertTrue(MediaState.Paused(holiday, 0, 90_000).isPlayable)
        assertTrue(MediaState.Buffering(holiday).isPlayable)
        assertFalse(MediaState.Sending(holiday, 0, null).isPlayable)
        assertFalse(MediaState.Ended(holiday).isPlayable)
        assertFalse(MediaState.Idle.isPlayable)
    }

    @Test
    fun `a state that carries a position refuses a negative one, and a failure needs words`() {
        assertFailsWith<IllegalArgumentException> { MediaState.Playing(holiday, -1, 0) }
        assertFailsWith<IllegalArgumentException> { MediaState.Sending(holiday, -1, null) }
        assertFailsWith<IllegalArgumentException> { MediaState.Failed(holiday, " ") }
        assertFailsWith<IllegalArgumentException> { MediaItem(" ", "video/mp4", null) }
    }
}

class PlaybackReducerTest {
    private fun report(state: PlaybackState, position: Long = 0, duration: Long = -1, detail: String = "") =
        PlaybackStateMessage(state, position, duration, detail)

    @Test
    fun `the television's report drives the state once the file has arrived`() {
        val buffering = MediaState.Buffering(holiday)
        assertEquals(
            MediaState.Playing(holiday, 4_000, 90_000),
            PlaybackReducer.reduce(buffering, report(PlaybackState.PLAYING, 4_000, 90_000)),
        )
        assertEquals(
            MediaState.Paused(holiday, 4_000, 90_000),
            PlaybackReducer.reduce(buffering, report(PlaybackState.PAUSED, 4_000, 90_000)),
        )
        assertEquals(MediaState.Ended(holiday), PlaybackReducer.reduce(buffering, report(PlaybackState.ENDED)))
        assertEquals(MediaState.Idle, PlaybackReducer.reduce(buffering, report(PlaybackState.IDLE)))
        assertEquals(
            buffering,
            PlaybackReducer.reduce(MediaState.Playing(holiday, 1, 2), report(PlaybackState.BUFFERING)),
        )
    }

    @Test
    fun `while bytes are still going down the socket, only an error is listened to`() {
        val sending = MediaState.Sending(holiday, 10, 3_000_000)
        assertEquals(sending, PlaybackReducer.reduce(sending, report(PlaybackState.IDLE)))
        assertEquals(sending, PlaybackReducer.reduce(sending, report(PlaybackState.PLAYING, 5, 9)))
        val failed = assertIs<MediaState.Failed>(
            PlaybackReducer.reduce(sending, report(PlaybackState.ERROR, detail = "Container damaged.")),
        )
        assertEquals("Container damaged.", failed.message)
        assertEquals(holiday, failed.item)
    }

    @Test
    fun `an error with no words gets the honest fallback rather than a blank card`() {
        val failed = assertIs<MediaState.Failed>(
            PlaybackReducer.reduce(MediaState.Playing(holiday, 0, 0), report(PlaybackState.ERROR)),
        )
        assertEquals(MediaCopy.RECEIVER_ERROR, failed.message)
    }

    @Test
    fun `with nothing chosen, a report about playing is ignored and an error is still surfaced`() {
        assertEquals(MediaState.Idle, PlaybackReducer.reduce(MediaState.Idle, report(PlaybackState.PLAYING, 1, 2)))
        val failed = assertIs<MediaState.Failed>(
            PlaybackReducer.reduce(MediaState.Idle, report(PlaybackState.ERROR, detail = "No decoder.")),
        )
        assertNull(failed.item)
    }
}

class ChunkReaderTest {
    private fun bytes(count: Int): ByteArray = ByteArray(count) { (it * 31 + 7).toByte() }

    private fun reassemble(source: ByteArray, chunkBytes: Int): Pair<ByteArray, List<ChunkReader.Chunk>> {
        val reader = ChunkReader(ByteArrayInputStream(source), chunkBytes)
        val out = ByteArrayOutputStream()
        val chunks = mutableListOf<ChunkReader.Chunk>()
        while (true) {
            val chunk = reader.next() ?: break
            out.write(reader.buffer, 0, chunk.length)
            chunks += chunk
        }
        return out.toByteArray() to chunks
    }

    @Test
    fun `every size reassembles byte for byte with exactly one final chunk`() {
        val chunk = 8
        for (size in listOf(1, chunk - 1, chunk, chunk + 1, 3 * chunk, 3 * chunk + 5, 1_000)) {
            val source = bytes(size)
            val (back, chunks) = reassemble(source, chunk)
            assertContentEquals(source, back, "size $size")
            assertEquals(1, chunks.count { it.isFinal }, "size $size should have one final chunk")
            assertTrue(chunks.last().isFinal, "size $size")
            assertTrue(chunks.all { it.length in 1..chunk }, "size $size")
            assertEquals((size + chunk - 1) / chunk, chunks.size, "size $size")
        }
    }

    @Test
    fun `a whole number of chunks ends on a full final chunk, never on an empty one`() {
        val (_, chunks) = reassemble(bytes(24), 8)
        assertEquals(3, chunks.size)
        assertTrue(chunks.last().isFinal)
        assertEquals(8, chunks.last().length)
    }

    @Test
    fun `an empty stream yields nothing at all`() {
        assertNull(ChunkReader(ByteArrayInputStream(ByteArray(0)), 8).next())
    }

    @Test
    fun `a stream that returns short reads is still reassembled correctly`() {
        // InputStream.read may return fewer bytes than asked for, and a reader that mistook a short
        // read for the end would cut the file. This stream returns one byte at a time.
        val source = bytes(37)
        val trickle = object : java.io.InputStream() {
            private var at = 0
            override fun read(): Int = if (at < source.size) source[at++].toInt() and 0xff else -1
            override fun read(b: ByteArray, off: Int, len: Int): Int {
                if (len == 0) return 0
                val byte = read()
                if (byte < 0) return -1
                b[off] = byte.toByte()
                return 1
            }
        }
        val reader = ChunkReader(trickle, 8)
        val out = ByteArrayOutputStream()
        var finals = 0
        while (true) {
            val chunk = reader.next() ?: break
            out.write(reader.buffer, 0, chunk.length)
            if (chunk.isFinal) finals++
        }
        assertContentEquals(source, out.toByteArray())
        assertEquals(1, finals)
    }

    @Test
    fun `the default chunk is the wire's`() {
        assertEquals(MediaHandoff.CHUNK_BYTES, ChunkReader(ByteArrayInputStream(ByteArray(0))).buffer.size)
        assertFailsWith<IllegalArgumentException> { ChunkReader(ByteArrayInputStream(ByteArray(0)), 0) }
    }
}
