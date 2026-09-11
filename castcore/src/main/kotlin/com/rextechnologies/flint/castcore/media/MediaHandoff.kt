package com.rextechnologies.flint.castcore.media

import com.rextechnologies.flint.protocol.wire.MediaAction
import com.rextechnologies.flint.protocol.wire.MediaCommandMessage
import java.nio.charset.StandardCharsets

/**
 * How a file gets from the phone to the television.
 *
 * There are two routes and they are not equal. The phone can serve the file over its own token-gated
 * HTTP server and hand the receiver a URL, which is the elegant one; or it can push the bytes down
 * the control socket it already owns and then say "play what I just sent you", which is the one that
 * works. Some Fire OS builds silently drop an outbound connection the receiver initiates to a private
 * LAN address — not refused, not reset, dropped — while the very same address works perfectly for the
 * connection the phone initiated. So the push path is the default and the HTTP path is the option.
 */
object MediaHandoff {
    /**
     * The chunk size for a pushed file.
     *
     * Well inside the 15 MiB a single binary field may carry and the 16 MiB frame ceiling, and small
     * enough that a chunk does not monopolise the socket while control traffic waits behind it.
     */
    const val CHUNK_BYTES: Int = 512 * 1024

    /**
     * The MIME cap.
     *
     * Kotlin's codec rejects a MIME type longer than this. C# and Rust allow 512, which means a MIME
     * type between 256 and 512 bytes encodes on Windows and is refused here. Capping at the smaller
     * of the two is the only value that is safe in every direction until the three implementations
     * are reconciled.
     */
    const val MAXIMUM_MIME_BYTES: Int = 255

    /** What to call a file whose name tells us nothing. */
    const val FALLBACK_TITLE: String = "Untitled"

    const val MAXIMUM_TITLE_BYTES: Int = 512

    /** How many chunks a file of this size becomes. */
    fun chunkCount(fileBytes: Long): Long {
        require(fileBytes >= 0)
        if (fileBytes == 0L) return 0
        return (fileBytes + CHUNK_BYTES - 1) / CHUNK_BYTES
    }

    /**
     * A MIME type that will survive the wire, or `null`.
     *
     * Returns `null` rather than truncating. A truncated MIME type is a different MIME type, and the
     * receiver would pick a decoder for something the file is not.
     */
    fun mimeTypeOrNull(candidate: String): String? {
        val trimmed = candidate.trim()
        if (trimmed.isEmpty()) return null
        if (trimmed.toByteArray(StandardCharsets.UTF_8).size > MAXIMUM_MIME_BYTES) return null
        if (trimmed.any { it.isISOControl() }) return null
        return trimmed
    }

    /**
     * A display title with every trace of where the file lives removed.
     *
     * A client must never be able to name a filesystem path, and that includes the phone naming its
     * own: a title is shown on a television in somebody's living room, and "/storage/emulated/0/"
     * in front of it says more about the phone than anybody asked.
     */
    fun titleFor(displayName: String): String {
        val lastSeparator = displayName.indexOfLast { it == '/' || it == '\\' }
        val leaf = if (lastSeparator >= 0) displayName.substring(lastSeparator + 1) else displayName
        val cleaned = leaf.filterNot { it.isISOControl() }.trim()
        if (cleaned.isEmpty()) return FALLBACK_TITLE
        return cleaned.truncateToBytes(MAXIMUM_TITLE_BYTES)
    }

    /**
     * The command that plays the file just pushed over the control socket.
     *
     * The blank URL is the whole signal, and it is the receiver's own convention: a blank `url` on a
     * LOAD means "play the file most recently pushed to you", and anything else is fetched over HTTP.
     */
    fun playPushedFile(
        title: String,
        mimeType: String,
        durationMs: Long = -1,
        startPositionMs: Long = 0,
    ): MediaCommandMessage {
        val safeMime = requireNotNull(mimeTypeOrNull(mimeType)) {
            "A MIME type this long cannot be sent; ask mimeTypeOrNull before building a command"
        }
        return MediaCommandMessage(
            action = MediaAction.LOAD,
            url = "",
            title = titleFor(title),
            mimeType = safeMime,
            durationMs = durationMs,
            startPositionMs = startPositionMs,
        )
    }

    /** Returns the television to its idle screen and releases its decoder. */
    fun clear(): MediaCommandMessage = MediaCommandMessage(action = MediaAction.CLEAR)

    private fun String.truncateToBytes(maximumBytes: Int): String {
        var result = this
        while (result.toByteArray(StandardCharsets.UTF_8).size > maximumBytes) {
            result = result.substring(0, result.length - 1)
        }
        return result
    }
}
