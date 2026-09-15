package com.rextechnologies.flint.mobile.state

import com.rextechnologies.flint.castcore.copy.MediaCopy
import com.rextechnologies.flint.castcore.media.ChunkReader
import com.rextechnologies.flint.castcore.media.MediaHandoff
import com.rextechnologies.flint.castcore.media.MediaItem
import com.rextechnologies.flint.castcore.media.MediaState
import com.rextechnologies.flint.castcore.media.PlaybackReducer
import com.rextechnologies.flint.protocol.BinaryData
import com.rextechnologies.flint.protocol.wire.ControlMessage
import com.rextechnologies.flint.protocol.wire.MediaDataMessage
import com.rextechnologies.flint.protocol.wire.PlaybackStateMessage
import com.rextechnologies.flint.protocol.wire.TransportAction
import com.rextechnologies.flint.protocol.wire.TransportControl
import com.rextechnologies.flint.protocol.wire.WireMessage
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.InputStream
import java.util.concurrent.atomic.AtomicLong

/**
 * The part of a session a media hand-off needs, and no more.
 *
 * [SessionCoordinator] is the one implementation in the app. The interface exists so the coordinator
 * below can be driven by a fake that records what was sent and answers what it likes, which is how
 * every refusal path here has a test without a socket.
 */
interface MediaLink {
    val isConnected: Boolean

    suspend fun send(message: WireMessage): Boolean

    fun onMessage(listener: (WireMessage) -> Unit)
}

/**
 * Where the bytes come from, by an opaque reference the coordinator never parses.
 *
 * On the phone the reference is a content URI and the implementation reads through the
 * `ContentResolver`. A client must never name a filesystem path, and that includes this one: nothing
 * here knows or asks where the file lives.
 */
interface MediaSource {
    /** What the file is, or `null` when the provider will not say. Blocking: call on an IO thread. */
    fun describe(reference: String): MediaItem?

    /** The bytes, or `null` when they cannot be opened. Blocking: call on an IO thread. */
    fun open(reference: String): InputStream?
}

/**
 * Hands a file to the television and drives its playback.
 *
 * The push is the control socket: 512 KiB chunks in order, exactly one marked final, then a LOAD
 * with a blank URL, which is the receiver's own convention for "play what I just sent you". One
 * buffer is reused for the whole file and one copy is made per chunk, which is allowed here because
 * a hand-off is not the frame path; a copy of the whole file is not, and is never made.
 *
 * The television is the authority on playback. Its reports drive [state]; nothing here runs a clock
 * of its own. What this class decides is what to refuse before a byte leaves the phone, when to stop
 * reading, and what to say about it.
 */
class MediaCoordinator(
    private val link: MediaLink,
    private val source: MediaSource,
    private val scope: CoroutineScope,
    private val io: CoroutineDispatcher = Dispatchers.IO,
) {
    private val mutable = MutableStateFlow<MediaState>(MediaState.Idle)
    val state: StateFlow<MediaState> = mutable

    private val mutableNotices = MutableSharedFlow<String>(extraBufferCapacity = NOTICE_BUFFER)
    val notices: SharedFlow<String> = mutableNotices

    private val sequence = AtomicLong()
    private var transfer: Job? = null

    init {
        // On the connection's own thread. The update is a compare-and-set, so it is safe there, and
        // dispatching would only add a hop between the television's word and the scrubber.
        link.onMessage { message ->
            if (message is PlaybackStateMessage) mutable.update { PlaybackReducer.reduce(it, message) }
        }
    }

    /**
     * Starts sending the file at [reference].
     *
     * @return `false` when nothing was started: no television, or a transfer already under way. In
     *   both cases the banner says which.
     */
    fun choose(reference: String): Boolean {
        if (!link.isConnected) {
            mutableNotices.tryEmit(MediaCopy.NOT_CONNECTED)
            return false
        }
        if (mutable.value.isTransferring) return false
        transfer = scope.launch {
            val item = withContext(io) { source.describe(reference) }
            if (item == null) {
                mutable.value = MediaState.Failed(null, MediaCopy.UNREADABLE)
                return@launch
            }
            mutable.value = MediaState.Preparing(item)
            if (item.sizeBytes == 0L) {
                mutable.value = MediaState.Failed(item, MediaCopy.EMPTY_FILE)
                return@launch
            }
            if (MediaHandoff.mimeTypeOrNull(item.mimeType) == null) {
                mutable.value = MediaState.Failed(item, MediaCopy.MIME_TOO_LONG)
                return@launch
            }
            mutable.value = MediaState.Sending(item, 0, item.sizeBytes)
            val failure = withContext(io) { push(reference, item) }
            mutable.value = if (failure == null) MediaState.Buffering(item) else MediaState.Failed(item, failure)
        }
        return true
    }

    /**
     * The push loop. Returns the sentence for a failure, or `null` when the LOAD went out.
     *
     * Reading stops the moment a write is refused: a socket that will not take a chunk will not
     * take the next, and a phone that reads a two-gigabyte file into a dead socket is a phone
     * warming its pocket. Cancellation is checked between chunks, and never mid-chunk.
     */
    private suspend fun push(reference: String, item: MediaItem): String? {
        val stream = source.open(reference) ?: return MediaCopy.UNREADABLE
        var sent = 0L
        var finalSent = false
        stream.use { input ->
            val reader = ChunkReader(input)
            while (true) {
                scope.ensureActive()
                val chunk = reader.next() ?: break
                val payload = BinaryData.of(reader.buffer, 0, chunk.length)
                if (!link.send(MediaDataMessage(payload, chunk.isFinal))) return MediaCopy.LINK_LOST
                sent += chunk.length
                mutable.update { current ->
                    (current as? MediaState.Sending)?.copy(bytesSent = sent) ?: current
                }
                if (chunk.isFinal) {
                    finalSent = true
                    break
                }
            }
        }
        if (!finalSent) return MediaCopy.EMPTY_FILE
        val load = MediaHandoff.playPushedFile(item.title, item.mimeType, item.durationMs)
        return if (link.send(load)) null else MediaCopy.LINK_LOST
    }

    /** Abandons a transfer under way and tells the television to forget the part it has. */
    fun cancel() {
        val running = transfer ?: return
        transfer = null
        running.cancel()
        scope.launch {
            mutable.value = MediaState.Idle
            withContext(NonCancellable) { link.send(MediaHandoff.clear()) }
            mutableNotices.tryEmit(MediaCopy.CANCELLED)
        }
    }

    fun play() = control(TransportControl(TransportAction.PLAY))

    fun pause() = control(TransportControl(TransportAction.PAUSE))

    fun seekTo(positionMs: Long) = control(TransportControl(TransportAction.SEEK_TO, positionMs.coerceAtLeast(0)))

    /** Stops playback on the television, which then reports idle and the state follows. */
    fun stop() = control(TransportControl(TransportAction.STOP))

    /** Returns the television to its idle screen and this phone to nothing chosen. */
    fun clear() {
        transfer?.cancel()
        transfer = null
        mutable.value = MediaState.Idle
        scope.launch { withContext(NonCancellable) { link.send(MediaHandoff.clear()) } }
    }

    /** The session went away. Nothing is sent; there is nowhere to send it. */
    fun reset() {
        transfer?.cancel()
        transfer = null
        mutable.value = MediaState.Idle
    }

    private fun control(event: TransportControl) {
        scope.launch { link.send(ControlMessage(sequence.getAndIncrement(), event)) }
    }

    private companion object {
        const val NOTICE_BUFFER = 4
    }
}
