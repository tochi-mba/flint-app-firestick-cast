package com.rextechnologies.flint.castcore.media

import com.rextechnologies.flint.castcore.copy.MediaCopy
import com.rextechnologies.flint.protocol.wire.PlaybackState
import com.rextechnologies.flint.protocol.wire.PlaybackStateMessage
import java.io.InputStream

/**
 * The file somebody chose, as much of it as the phone was able to learn without reading it.
 *
 * @property title the display name, already stripped of anything that looks like a path.
 * @property mimeType the type the provider reported, already checked against the wire's cap.
 * @property sizeBytes the length, or `null` when the provider would not say -- which is an ordinary
 *   answer for a streamed document and not a fault.
 * @property durationMs the duration when it could be read, or [UNKNOWN_DURATION].
 */
data class MediaItem(
    val title: String,
    val mimeType: String,
    val sizeBytes: Long?,
    val durationMs: Long = UNKNOWN_DURATION,
) {
    init {
        require(title.isNotBlank())
        require(mimeType.isNotBlank())
        require(sizeBytes == null || sizeBytes >= 0)
        require(durationMs >= UNKNOWN_DURATION)
    }

    companion object {
        const val UNKNOWN_DURATION: Long = -1
    }
}

/**
 * Where a hand-off has got to, from the moment a file is chosen to the moment it is cleared.
 *
 * Sealed so the Media tab is a `when` with no default branch, and so a state that carries a
 * position cannot exist without the item it belongs to.
 */
sealed interface MediaState {
    /** The item in play, or `null` before one has been chosen. */
    val item: MediaItem?

    data object Idle : MediaState {
        override val item: MediaItem? get() = null
    }

    /** The provider is being asked what the file is. Nothing has left the phone. */
    data class Preparing(override val item: MediaItem) : MediaState

    /** Bytes are going down the control socket. [totalBytes] is `null` when the provider would not say. */
    data class Sending(override val item: MediaItem, val bytesSent: Long, val totalBytes: Long?) : MediaState {
        init {
            require(bytesSent >= 0)
            require(totalBytes == null || totalBytes >= 0)
        }

        /** How far through, or `null` while the total is unknown. */
        val fraction: Double?
            get() = totalBytes?.takeIf { it > 0 }?.let { (bytesSent.toDouble() / it).coerceIn(0.0, 1.0) }
    }

    /** Everything has arrived and the television is preparing to play it. */
    data class Buffering(override val item: MediaItem) : MediaState

    data class Playing(override val item: MediaItem, val positionMs: Long, val durationMs: Long) : MediaState {
        init {
            require(positionMs >= 0 && durationMs >= MediaItem.UNKNOWN_DURATION)
        }
    }

    data class Paused(override val item: MediaItem, val positionMs: Long, val durationMs: Long) : MediaState {
        init {
            require(positionMs >= 0 && durationMs >= MediaItem.UNKNOWN_DURATION)
        }
    }

    data class Ended(override val item: MediaItem) : MediaState

    /** Something went wrong, in the receiver's own words where it gave any. */
    data class Failed(override val item: MediaItem?, val message: String) : MediaState {
        init {
            require(message.isNotBlank())
        }
    }

    /** Whether the transport controls should be offered. */
    val isPlayable: Boolean
        get() = this is Playing || this is Paused || this is Buffering

    /** Whether a transfer is under way and could be cancelled. */
    val isTransferring: Boolean
        get() = this is Preparing || this is Sending
}

/**
 * Turns the television's playback reports into the phone's state.
 *
 * The television is the authority on playback: the scrubber follows its reported position rather
 * than a phone-side clock, and a report that says nothing is playing means nothing is playing. The
 * one thing it is not the authority on is a transfer that has not finished, so while bytes are still
 * going down the socket only an error report is listened to.
 */
object PlaybackReducer {
    fun reduce(current: MediaState, report: PlaybackStateMessage): MediaState {
        if (report.state == PlaybackState.ERROR) {
            return MediaState.Failed(current.item, report.detail.ifBlank { MediaCopy.RECEIVER_ERROR })
        }
        if (current.isTransferring) return current
        val item = current.item ?: return current
        return when (report.state) {
            PlaybackState.IDLE -> MediaState.Idle
            PlaybackState.BUFFERING -> MediaState.Buffering(item)
            PlaybackState.PLAYING -> MediaState.Playing(item, report.positionMs, report.durationMs)
            PlaybackState.PAUSED -> MediaState.Paused(item, report.positionMs, report.durationMs)
            PlaybackState.ENDED -> MediaState.Ended(item)
            PlaybackState.ERROR -> MediaState.Failed(item, report.detail.ifBlank { MediaCopy.RECEIVER_ERROR })
        }
    }
}

/**
 * Cuts a stream into chunks and knows which one is last before it has been sent.
 *
 * The receiver learns a pushed file is complete from exactly one chunk marked final, so the reader
 * looks one byte ahead: a chunk is final when the byte after it does not exist. Without that, a file
 * whose length is a whole number of chunks could only be finished by an empty chunk, and a reader
 * that sent one would send a different message from the one every other file gets.
 *
 * One buffer, reused. A caller that keeps a chunk's bytes copies them before asking for the next.
 */
class ChunkReader(private val input: InputStream, chunkBytes: Int = MediaHandoff.CHUNK_BYTES) {
    init {
        require(chunkBytes > 0)
    }

    /** Filled by [next]; valid up to the returned chunk's length until the next call. */
    val buffer: ByteArray = ByteArray(chunkBytes)

    private var lookahead: Int = NOT_READ

    class Chunk(val length: Int, val isFinal: Boolean)

    /** The next chunk, or `null` when the stream has nothing more -- including when it never had anything. */
    fun next(): Chunk? {
        if (lookahead == END) return null
        var length = 0
        if (lookahead >= 0) {
            buffer[0] = lookahead.toByte()
            length = 1
            lookahead = NOT_READ
        }
        while (length < buffer.size) {
            val read = input.read(buffer, length, buffer.size - length)
            if (read < 0) {
                lookahead = END
                break
            }
            length += read
        }
        if (length == 0) return null
        if (lookahead != END) {
            val peek = input.read()
            lookahead = if (peek < 0) END else peek
        }
        return Chunk(length, isFinal = lookahead == END)
    }

    private companion object {
        const val NOT_READ = -2
        const val END = -1
    }
}
