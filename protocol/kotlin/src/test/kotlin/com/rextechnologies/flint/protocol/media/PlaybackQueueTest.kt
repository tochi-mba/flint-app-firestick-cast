package com.rextechnologies.flint.protocol.media

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertSame
import kotlin.test.assertTrue

class PlaybackQueueTest {
    private fun item(id: String, mime: String = "video/mp4") =
        QueueItem(id = id, title = "Title $id", mimeType = mime, durationMs = 1_000)

    private fun queueOf(vararg ids: String): PlaybackQueue =
        PlaybackQueue().addAll(ids.map { item(it) })

    @Test
    fun `adding to an empty queue starts playback at the first item`() {
        val queue = queueOf("a", "b")

        assertEquals(0, queue.currentIndex)
        assertEquals("a", queue.current?.id)
        assertEquals(2, queue.size)
        assertFalse(queue.isEmpty)
    }

    @Test
    fun `adding to a playing queue leaves the cursor alone`() {
        val queue = queueOf("a", "b").selectId("b").add(item("c"))

        assertEquals("b", queue.current?.id)
        assertEquals(3, queue.size)
    }

    @Test
    fun `duplicate ids are ignored rather than shadowing the original`() {
        val queue = queueOf("a").addAll(listOf(item("a"), item("b"), item("b")))

        assertEquals(listOf("a", "b"), queue.items.map { it.id })
    }

    @Test
    fun `adding nothing returns the same queue`() {
        val queue = queueOf("a")

        assertSame(queue, queue.addAll(emptyList()))
        assertSame(queue, queue.add(item("a")))
    }

    @Test
    fun `removing an item before the cursor keeps the same item playing`() {
        val queue = queueOf("a", "b", "c").selectId("c").removeAt(0)

        assertEquals("c", queue.current?.id)
        assertEquals(listOf("b", "c"), queue.items.map { it.id })
    }

    @Test
    fun `removing an item after the cursor keeps the same item playing`() {
        val queue = queueOf("a", "b", "c").selectId("a").removeAt(2)

        assertEquals("a", queue.current?.id)
    }

    @Test
    fun `removing the playing item advances into the freed slot`() {
        val queue = queueOf("a", "b", "c").selectId("b").removeId("b")

        assertEquals("c", queue.current?.id)
    }

    @Test
    fun `removing the last playing item falls back to the new final item`() {
        val queue = queueOf("a", "b").selectId("b").removeAt(1)

        assertEquals("a", queue.current?.id)
    }

    @Test
    fun `removing the only item empties the queue but keeps the modes`() {
        val queue = queueOf("a")
            .withRepeatMode(QueueRepeatMode.ALL)
            .removeAt(0)

        assertTrue(queue.isEmpty)
        assertEquals(-1, queue.currentIndex)
        assertEquals(QueueRepeatMode.ALL, queue.repeatMode)
    }

    @Test
    fun `removing an unknown index or id is a no-op`() {
        val queue = queueOf("a", "b")

        assertSame(queue, queue.removeAt(9))
        assertSame(queue, queue.removeId("missing"))
    }

    @Test
    fun `moving an item forward keeps the cursor on the same item`() {
        val queue = queueOf("a", "b", "c").selectId("a").move(0, 2)

        assertEquals(listOf("b", "c", "a"), queue.items.map { it.id })
        assertEquals("a", queue.current?.id)
    }

    @Test
    fun `moving an item backward keeps the cursor on the same item`() {
        val queue = queueOf("a", "b", "c").selectId("b").move(2, 0)

        assertEquals(listOf("c", "a", "b"), queue.items.map { it.id })
        assertEquals("b", queue.current?.id)
    }

    @Test
    fun `moving an item past the cursor shifts the cursor index`() {
        val queue = queueOf("a", "b", "c").selectId("b").move(0, 2)

        assertEquals("b", queue.current?.id)
        assertEquals(0, queue.currentIndex)
    }

    @Test
    fun `an out-of-range or identity move is a no-op`() {
        val queue = queueOf("a", "b")

        assertSame(queue, queue.move(0, 0))
        assertSame(queue, queue.move(-1, 1))
        assertSame(queue, queue.move(0, 5))
    }

    @Test
    fun `select ignores an unknown index or id`() {
        val queue = queueOf("a", "b")

        assertSame(queue, queue.selectIndex(7))
        assertSame(queue, queue.selectId("missing"))
    }

    @Test
    fun `next walks the queue and stops at the end`() {
        var queue: PlaybackQueue? = queueOf("a", "b")

        queue = queue!!.next()
        assertEquals("b", queue?.current?.id)
        assertNull(queue!!.next())
    }

    @Test
    fun `repeat all wraps in both directions`() {
        val queue = queueOf("a", "b").withRepeatMode(QueueRepeatMode.ALL)

        assertEquals("a", queue.selectId("b").next()?.current?.id)
        assertEquals("b", queue.selectId("a").previous()?.current?.id)
    }

    @Test
    fun `repeat one repeats automatically but a manual next still advances`() {
        val queue = queueOf("a", "b").withRepeatMode(QueueRepeatMode.ONE)

        assertEquals("a", queue.next(automatic = true)?.current?.id)
        assertEquals("b", queue.next(automatic = false)?.current?.id)
    }

    @Test
    fun `previous stops at the start without repeat`() {
        assertNull(queueOf("a", "b").previous())
    }

    @Test
    fun `next and previous do nothing on an empty or stopped queue`() {
        assertNull(PlaybackQueue().next())
        assertNull(PlaybackQueue().previous())
    }

    @Test
    fun `shuffle keeps the current item first and preserves every item exactly once`() {
        val queue = queueOf("a", "b", "c", "d", "e").selectId("c").withShuffle(true, seed = 42)

        assertEquals(2, queue.order.first())
        assertEquals(queue.items.indices.toSet(), queue.order.toSet())
        assertEquals("c", queue.current?.id)
        assertEquals(0, queue.orderPosition)
    }

    @Test
    fun `disabling shuffle restores the list order`() {
        val queue = queueOf("a", "b", "c").withShuffle(true, seed = 7).withShuffle(false)

        assertEquals(listOf(0, 1, 2), queue.order)
        assertFalse(queue.shuffled)
    }

    @Test
    fun `toggling shuffle to its current value is a no-op`() {
        val queue = queueOf("a", "b")

        assertSame(queue, queue.withShuffle(false))
    }

    @Test
    fun `shuffle on a stopped queue still permutes every item`() {
        val queue = PlaybackQueue(items = listOf(item("a"), item("b"), item("c")))
            .withShuffle(true, seed = 3)

        assertEquals(setOf(0, 1, 2), queue.order.toSet())
        assertEquals(-1, queue.orderPosition)
    }

    @Test
    fun `shuffled playback follows the shuffled order`() {
        val queue = queueOf("a", "b", "c", "d").selectId("a").withShuffle(true, seed = 11)
        val expected = queue.items[queue.order[1]].id

        assertEquals(expected, queue.next()?.current?.id)
    }

    @Test
    fun `clear keeps the modes and empties the list`() {
        val queue = queueOf("a", "b").withRepeatMode(QueueRepeatMode.ONE).clear()

        assertTrue(queue.isEmpty)
        assertEquals(QueueRepeatMode.ONE, queue.repeatMode)
    }

    @Test
    fun `item classification follows the mime type`() {
        assertTrue(item("p", "image/jpeg").isImage)
        assertTrue(item("s", "audio/mpeg").isAudio)
        assertFalse(item("v").isImage)
        assertFalse(item("v").isAudio)
    }

    @Test
    fun `invalid items and queues are refused`() {
        assertFailsWith<IllegalArgumentException> { QueueItem(" ", "t", "video/mp4") }
        assertFailsWith<IllegalArgumentException> { QueueItem("a", "t", "video/mp4", durationMs = -5) }
        assertFailsWith<IllegalArgumentException> {
            PlaybackQueue(items = listOf(item("a"), item("a")))
        }
        assertFailsWith<IllegalArgumentException> {
            PlaybackQueue(items = listOf(item("a")), currentIndex = 4)
        }
        assertFailsWith<IllegalArgumentException> {
            PlaybackQueue(items = listOf(item("a"), item("b")), order = listOf(0, 0))
        }
    }

    @Test
    fun `an unknown duration is allowed`() {
        assertEquals(
            QueueItem.UNKNOWN_DURATION,
            QueueItem("a", "t", "video/mp4").durationMs,
        )
    }
}

