package com.rextechnologies.flint.protocol.media

import kotlin.random.Random

enum class QueueRepeatMode { OFF, ONE, ALL }

data class QueueItem(
    val id: String,
    val title: String,
    val mimeType: String,
    val durationMs: Long = UNKNOWN_DURATION,
    val subtitleId: String? = null,
) {
    init {
        require(id.isNotBlank()) { "Queue item id must not be blank" }
        require(durationMs == UNKNOWN_DURATION || durationMs >= 0) { "Invalid duration: $durationMs" }
    }

    val isImage: Boolean get() = mimeType.startsWith("image/")
    val isAudio: Boolean get() = mimeType.startsWith("audio/")

    companion object {
        const val UNKNOWN_DURATION: Long = -1
    }
}

/**
 * The ordered playback list and its cursor, free of any player API.
 *
 * The phone owns this state and the receiver renders it, so it lives here where
 * both sides compile identical transitions instead of re-deriving them.
 */
data class PlaybackQueue(
    val items: List<QueueItem> = emptyList(),
    val currentIndex: Int = -1,
    val repeatMode: QueueRepeatMode = QueueRepeatMode.OFF,
    val shuffled: Boolean = false,
    val order: List<Int> = items.indices.toList(),
) {
    init {
        require(items.distinctBy { it.id }.size == items.size) { "Queue item ids must be unique" }
        require(currentIndex == -1 || currentIndex in items.indices) { "Cursor is out of range" }
        require(order.sorted() == items.indices.toList()) { "Order must permute every item exactly once" }
    }

    val current: QueueItem?
        get() = items.getOrNull(currentIndex)

    val isEmpty: Boolean get() = items.isEmpty()

    val size: Int get() = items.size

    /** Position of the cursor within the active order, or -1 when stopped. */
    val orderPosition: Int
        get() = if (currentIndex < 0) -1 else order.indexOf(currentIndex)

    fun add(item: QueueItem): PlaybackQueue = addAll(listOf(item))

    fun addAll(newItems: List<QueueItem>): PlaybackQueue {
        val existing = items.mapTo(mutableSetOf()) { it.id }
        val accepted = newItems.filter { existing.add(it.id) }
        if (accepted.isEmpty()) return this
        return copy(
            items = items + accepted,
            currentIndex = if (currentIndex == -1) items.size else currentIndex,
            order = order + accepted.indices.map { items.size + it },
        )
    }

    fun removeAt(index: Int): PlaybackQueue {
        if (index !in items.indices) return this
        val remaining = items.toMutableList().apply { removeAt(index) }
        if (remaining.isEmpty()) return PlaybackQueue(repeatMode = repeatMode, shuffled = shuffled)
        val newOrder = order.filter { it != index }.map { if (it > index) it - 1 else it }
        val newCursor = when {
            currentIndex < index -> currentIndex
            currentIndex > index -> currentIndex - 1
            // Removing the playing item advances to whatever slid into its slot.
            else -> index.coerceAtMost(remaining.lastIndex)
        }
        return copy(items = remaining, currentIndex = newCursor, order = newOrder)
    }

    fun removeId(id: String): PlaybackQueue = removeAt(items.indexOfFirst { it.id == id })

    fun move(fromIndex: Int, toIndex: Int): PlaybackQueue {
        if (fromIndex !in items.indices || toIndex !in items.indices || fromIndex == toIndex) return this
        val moved = items.toMutableList().apply { add(toIndex, removeAt(fromIndex)) }
        fun remap(value: Int): Int = when {
            value == fromIndex -> toIndex
            fromIndex < toIndex && value in (fromIndex + 1)..toIndex -> value - 1
            toIndex < fromIndex && value in toIndex until fromIndex -> value + 1
            else -> value
        }
        return copy(
            items = moved,
            currentIndex = if (currentIndex == -1) -1 else remap(currentIndex),
            order = order.map(::remap),
        )
    }

    fun selectIndex(index: Int): PlaybackQueue =
        if (index in items.indices) copy(currentIndex = index) else this

    fun selectId(id: String): PlaybackQueue = selectIndex(items.indexOfFirst { it.id == id })

    fun withRepeatMode(mode: QueueRepeatMode): PlaybackQueue = copy(repeatMode = mode)

    /** Reorders playback without moving the visible list; the current item stays first. */
    fun withShuffle(enabled: Boolean, seed: Long = 0L): PlaybackQueue {
        if (enabled == shuffled) return this
        if (!enabled) return copy(shuffled = false, order = items.indices.toList())
        val remaining = items.indices.filter { it != currentIndex }.toMutableList()
        val random = Random(seed)
        for (index in remaining.lastIndex downTo 1) {
            val target = random.nextInt(index + 1)
            val swap = remaining[index]
            remaining[index] = remaining[target]
            remaining[target] = swap
        }
        val shuffledOrder = if (currentIndex >= 0) listOf(currentIndex) + remaining else remaining
        return copy(shuffled = true, order = shuffledOrder)
    }

    /**
     * The queue advanced past the current item, or null when playback should stop.
     *
     * [automatic] distinguishes a track ending on its own from the user pressing
     * next: repeat-one repeats only in the first case, and a manual next at the
     * end of a non-repeating queue stops rather than wrapping.
     */
    fun next(automatic: Boolean = true): PlaybackQueue? {
        if (isEmpty || currentIndex < 0) return null
        if (automatic && repeatMode == QueueRepeatMode.ONE) return this
        val position = orderPosition
        if (position < 0) return null
        if (position < order.lastIndex) return copy(currentIndex = order[position + 1])
        return if (repeatMode == QueueRepeatMode.ALL) copy(currentIndex = order.first()) else null
    }

    fun previous(): PlaybackQueue? {
        if (isEmpty || currentIndex < 0) return null
        val position = orderPosition
        if (position <= 0) {
            return if (repeatMode == QueueRepeatMode.ALL) copy(currentIndex = order.last()) else null
        }
        return copy(currentIndex = order[position - 1])
    }

    fun clear(): PlaybackQueue = PlaybackQueue(repeatMode = repeatMode, shuffled = shuffled)
}

