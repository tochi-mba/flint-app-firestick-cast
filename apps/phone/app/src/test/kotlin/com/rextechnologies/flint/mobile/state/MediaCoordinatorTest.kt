package com.rextechnologies.flint.mobile.state

import com.rextechnologies.flint.castcore.copy.MediaCopy
import com.rextechnologies.flint.castcore.media.MediaHandoff
import com.rextechnologies.flint.castcore.media.MediaItem
import com.rextechnologies.flint.castcore.media.MediaState
import com.rextechnologies.flint.protocol.wire.ControlMessage
import com.rextechnologies.flint.protocol.wire.MediaAction
import com.rextechnologies.flint.protocol.wire.MediaCommandMessage
import com.rextechnologies.flint.protocol.wire.MediaDataMessage
import com.rextechnologies.flint.protocol.wire.PlaybackState
import com.rextechnologies.flint.protocol.wire.PlaybackStateMessage
import com.rextechnologies.flint.protocol.wire.TransportAction
import com.rextechnologies.flint.protocol.wire.TransportControl
import com.rextechnologies.flint.protocol.wire.WireMessage
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.InputStream
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertTrue

/**
 * The hand-off's own decisions, with the socket and the provider faked.
 *
 * Everything runs on the unconfined dispatcher so a whole transfer completes inside the call that
 * started it, and a fake link that waits on a gate is how the tests get in between two chunks.
 */
class MediaCoordinatorTest {
    private class Link(
        var connected: Boolean = true,
        var refuseAt: Int = Int.MAX_VALUE,
        val gateAt: Int = Int.MAX_VALUE,
    ) : MediaLink {
        val sent = mutableListOf<WireMessage>()
        val gate = CompletableDeferred<Unit>()
        private val listeners = mutableListOf<(WireMessage) -> Unit>()
        override val isConnected: Boolean get() = connected

        override suspend fun send(message: WireMessage): Boolean {
            if (message is MediaDataMessage) {
                val index = sent.count { it is MediaDataMessage }
                if (index == gateAt) gate.await()
                if (index >= refuseAt) return false
            }
            sent += message
            return true
        }

        override fun onMessage(listener: (WireMessage) -> Unit) {
            listeners += listener
        }

        fun receive(message: WireMessage) = listeners.forEach { it(message) }

        val chunks: List<MediaDataMessage> get() = sent.filterIsInstance<MediaDataMessage>()
        val loads: List<MediaCommandMessage> get() = sent.filterIsInstance<MediaCommandMessage>()
    }

    private class Source(
        private val bytes: ByteArray?,
        private val item: MediaItem?,
    ) : MediaSource {
        var opened = 0
        override fun describe(reference: String): MediaItem? = item
        override fun open(reference: String): InputStream? {
            opened++
            return bytes?.let { ByteArrayInputStream(it) }
        }
    }

    private fun bytes(count: Int) = ByteArray(count) { (it * 7 + 3).toByte() }

    private fun item(size: Long?, mime: String = "video/mp4") =
        MediaItem("holiday.mp4", mime, size, durationMs = 90_000)

    private fun coordinator(link: Link, source: Source) =
        MediaCoordinator(link, source, CoroutineScope(Dispatchers.Unconfined), io = Dispatchers.Unconfined)

    @Test
    fun `every size goes out in order, with one final chunk, then a blank-url load`() = runBlocking {
        val chunk = MediaHandoff.CHUNK_BYTES
        for (size in listOf(1, chunk, chunk + 1, 3 * chunk)) {
            val link = Link()
            val source = Source(bytes(size), item(size.toLong()))
            val coordinator = coordinator(link, source)
            assertTrue(coordinator.choose("content://x"))

            val reassembled = ByteArrayOutputStream()
            link.chunks.forEach { reassembled.write(it.data.toByteArray()) }
            assertContentEquals(bytes(size), reassembled.toByteArray(), "size $size")
            assertEquals(1, link.chunks.count { it.isFinal }, "size $size")
            assertTrue(link.chunks.last().isFinal)
            assertEquals((size + chunk - 1) / chunk, link.chunks.size, "size $size")

            val load = link.loads.single()
            assertEquals(MediaAction.LOAD, load.action)
            assertEquals("", load.url)
            assertEquals("holiday.mp4", load.title)
            assertEquals(90_000, load.durationMs)
            assertTrue(link.sent.indexOf(load) > link.sent.indexOf(link.chunks.last()))
            assertEquals(MediaState.Buffering(item(size.toLong())), coordinator.state.value)
        }
    }

    @Test
    fun `an empty file is refused before a byte leaves the phone`() {
        val link = Link()
        val coordinator = coordinator(link, Source(ByteArray(0), item(0)))
        coordinator.choose("content://x")
        assertEquals(MediaState.Failed(item(0), MediaCopy.EMPTY_FILE), coordinator.state.value)
        assertTrue(link.sent.isEmpty())
    }

    @Test
    fun `a provider that lies about the size still ends on the truth`() {
        // Size unknown and the stream turns out empty: the loop finds no chunk and says so.
        val link = Link()
        val coordinator = coordinator(link, Source(ByteArray(0), item(null)))
        coordinator.choose("content://x")
        assertEquals(MediaState.Failed(item(null), MediaCopy.EMPTY_FILE), coordinator.state.value)
        assertTrue(link.sent.isEmpty())
    }

    @Test
    fun `a type name too long for the wire is refused, and so is a file that cannot be read`() {
        val long = item(10, mime = "video/" + "x".repeat(300))
        val refused = coordinator(Link(), Source(bytes(10), long))
        refused.choose("content://x")
        assertEquals(MediaState.Failed(long, MediaCopy.MIME_TOO_LONG), refused.state.value)

        val unreadable = coordinator(Link(), Source(null, null))
        unreadable.choose("content://x")
        assertEquals(MediaState.Failed(null, MediaCopy.UNREADABLE), unreadable.state.value)
    }

    @Test
    fun `a refused write stops the reading and the load is never sent`() {
        val link = Link(refuseAt = 1)
        val source = Source(bytes(3 * MediaHandoff.CHUNK_BYTES), item(3L * MediaHandoff.CHUNK_BYTES))
        val coordinator = coordinator(link, source)
        coordinator.choose("content://x")
        assertEquals(
            MediaState.Failed(item(3L * MediaHandoff.CHUNK_BYTES), MediaCopy.LINK_LOST),
            coordinator.state.value,
        )
        assertEquals(1, link.chunks.size)
        assertTrue(link.loads.isEmpty())
    }

    @Test
    fun `cancelling between chunks stops the send and tells the television to forget it`() = runBlocking {
        val link = Link(gateAt = 1)
        val source = Source(bytes(3 * MediaHandoff.CHUNK_BYTES), item(3L * MediaHandoff.CHUNK_BYTES))
        val coordinator = coordinator(link, source)
        coordinator.choose("content://x")
        assertIs<MediaState.Sending>(coordinator.state.value)
        assertEquals(1, link.chunks.size)

        coordinator.cancel()
        link.gate.complete(Unit)
        assertEquals(MediaState.Idle, coordinator.state.value)
        assertEquals(1, link.chunks.size, "no chunk may go out after a cancel")
        assertTrue(link.loads.any { it.action == MediaAction.CLEAR })
        assertTrue(link.loads.none { it.action == MediaAction.LOAD })
    }

    @Test
    fun `the television's reports drive the state, and the controls go out with rising sequence numbers`() {
        val link = Link()
        val coordinator = coordinator(link, Source(bytes(10), item(10)))
        coordinator.choose("content://x")
        link.receive(PlaybackStateMessage(PlaybackState.PLAYING, 1_000, 90_000))
        assertEquals(MediaState.Playing(item(10), 1_000, 90_000), coordinator.state.value)

        coordinator.pause()
        coordinator.seekTo(5_000)
        coordinator.play()
        coordinator.stop()
        val controls = link.sent.filterIsInstance<ControlMessage>()
        assertEquals(
            listOf(TransportAction.PAUSE, TransportAction.SEEK_TO, TransportAction.PLAY, TransportAction.STOP),
            controls.map { (it.event as TransportControl).action },
        )
        assertEquals(5_000, (controls[1].event as TransportControl).positionMs)
        assertEquals(controls.map { it.sequenceNumber }.sorted(), controls.map { it.sequenceNumber })
        assertEquals(controls.map { it.sequenceNumber }.toSet().size, controls.size)

        link.receive(PlaybackStateMessage(PlaybackState.IDLE))
        assertEquals(MediaState.Idle, coordinator.state.value)
    }

    @Test
    fun `nothing is chosen without a television, and the banner says so`() = runBlocking {
        val link = Link(connected = false)
        val coordinator = coordinator(link, Source(bytes(10), item(10)))
        val notices = mutableListOf<String>()
        val listening = CoroutineScope(Dispatchers.Unconfined).launch { coordinator.notices.collect { notices += it } }
        assertFalse(coordinator.choose("content://x"))
        listening.cancel()
        assertEquals(listOf(MediaCopy.NOT_CONNECTED), notices)
        assertTrue(link.sent.isEmpty())
        assertEquals(MediaState.Idle, coordinator.state.value)
    }

    @Test
    fun `a second choice during a transfer is refused rather than interleaved`() {
        val link = Link(gateAt = 1)
        val coordinator = coordinator(link, Source(bytes(3 * MediaHandoff.CHUNK_BYTES), item(null)))
        assertTrue(coordinator.choose("content://one"))
        assertFalse(coordinator.choose("content://two"))
        link.gate.complete(Unit)
    }

    @Test
    fun `clearing returns the television to idle and the phone to nothing chosen`() {
        val link = Link()
        val coordinator = coordinator(link, Source(bytes(10), item(10)))
        coordinator.choose("content://x")
        coordinator.clear()
        assertEquals(MediaState.Idle, coordinator.state.value)
        assertEquals(MediaAction.CLEAR, link.loads.last().action)
    }
}
