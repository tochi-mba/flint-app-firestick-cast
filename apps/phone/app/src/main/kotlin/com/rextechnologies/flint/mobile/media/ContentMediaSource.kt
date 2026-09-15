package com.rextechnologies.flint.mobile.media

import android.content.Context
import android.content.Intent
import android.media.MediaMetadataRetriever
import android.net.Uri
import android.provider.OpenableColumns
import com.rextechnologies.flint.castcore.media.MediaHandoff
import com.rextechnologies.flint.castcore.media.MediaItem
import com.rextechnologies.flint.mobile.state.MediaSource
import java.io.InputStream

/**
 * A file the system picker handed over, read through the `ContentResolver` and never by path.
 *
 * The Storage Access Framework gives the app a content URI and a grant to read it. Everything this
 * class learns about the file -- its display name, its size, its type, its duration -- comes from
 * asking the provider, and everything it reads comes through a stream the provider opens. No path is
 * resolved, and no copy of the file is made on the phone: the stream goes straight to the socket.
 */
class ContentMediaSource(context: Context) : MediaSource {
    private val applicationContext = context.applicationContext
    private val resolver = applicationContext.contentResolver

    override fun describe(reference: String): MediaItem? {
        val uri = runCatching { Uri.parse(reference) }.getOrNull() ?: return null
        // The grant the picker gave lasts until the process dies. Taking it persistably costs
        // nothing on a provider that supports it and is refused harmlessly on one that does not.
        runCatching { resolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION) }

        var displayName: String? = null
        var sizeBytes: Long? = null
        runCatching {
            resolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)
                ?.use { cursor ->
                    if (cursor.moveToFirst()) {
                        val nameColumn = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                        val sizeColumn = cursor.getColumnIndex(OpenableColumns.SIZE)
                        if (nameColumn >= 0 && !cursor.isNull(nameColumn)) displayName = cursor.getString(nameColumn)
                        if (sizeColumn >= 0 && !cursor.isNull(sizeColumn)) sizeBytes = cursor.getLong(sizeColumn)
                    }
                }
        }

        val mimeType = runCatching { resolver.getType(uri) }.getOrNull().orEmpty().ifBlank { UNKNOWN_MIME }
        val title = MediaHandoff.titleFor(displayName ?: uri.lastPathSegment.orEmpty())
        return MediaItem(
            title = title,
            mimeType = mimeType,
            sizeBytes = sizeBytes?.takeIf { it >= 0 },
            durationMs = durationOf(uri),
        )
    }

    override fun open(reference: String): InputStream? =
        runCatching { resolver.openInputStream(Uri.parse(reference)) }.getOrNull()

    /**
     * The duration, when the file has one to read.
     *
     * Best effort: a retriever that cannot parse the container answers with an unknown duration, and
     * the receiver reports the real one once it has the file, which is what the scrubber follows.
     */
    private fun durationOf(uri: Uri): Long {
        val retriever = MediaMetadataRetriever()
        return try {
            retriever.setDataSource(applicationContext, uri)
            retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DURATION)?.toLongOrNull()
                ?.takeIf { it >= 0 }
                ?: MediaItem.UNKNOWN_DURATION
        } catch (_: Throwable) {
            MediaItem.UNKNOWN_DURATION
        } finally {
            runCatching { retriever.release() }
        }
    }

    private companion object {
        /** What the wire carries when the provider would not name a type. The receiver sniffs the container. */
        const val UNKNOWN_MIME = "application/octet-stream"
    }
}
