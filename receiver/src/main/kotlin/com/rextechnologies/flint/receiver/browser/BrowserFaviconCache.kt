package com.rextechnologies.flint.receiver.browser

import android.graphics.Bitmap
import java.io.ByteArrayOutputStream
import java.security.MessageDigest
import java.util.LinkedHashMap

/** A bounded PNG ready for the tab model or secure browser channel. */
class CachedBrowserFavicon internal constructor(
    val id: Long,
    val width: Int,
    val height: Int,
    pngBytes: ByteArray,
) {
    private val storedBytes = pngBytes.copyOf()
    val pngBytes: ByteArray get() = storedBytes.copyOf()
    val byteCount: Int get() = storedBytes.size

    internal fun internalBytes(): ByteArray = storedBytes
}

enum class BrowserFaviconRejection {
    INVALID_DIMENSIONS,
    EMPTY_PAYLOAD,
    PAYLOAD_TOO_LARGE,
    NOT_PNG,
    ENCODING_FAILED,
}

sealed interface BrowserFaviconOffer {
    data class Accepted(val favicon: CachedBrowserFavicon, val inserted: Boolean) : BrowserFaviconOffer
    data class Rejected(val reason: BrowserFaviconRejection) : BrowserFaviconOffer
}

/**
 * Content-addressed, LRU-bounded favicon storage.
 *
 * A content-derived id is stable across duplicate callbacks and even eviction/reinsertion. The
 * cache owns every byte array it accepts and returns defensive copies, so a WebView callback cannot
 * mutate an icon after its bounds have been checked.
 */
class BrowserFaviconCache(
    private val maxEntries: Int = DEFAULT_MAX_ENTRIES,
) {
    companion object {
        const val MAX_WIDTH = 64
        const val MAX_HEIGHT = 64
        const val MAX_PNG_BYTES = 16 * 1024
        const val DEFAULT_MAX_ENTRIES = 64

        private val PNG_SIGNATURE = byteArrayOf(-119, 80, 78, 71, 13, 10, 26, 10)
    }

    init {
        require(maxEntries > 0) { "maxEntries must be positive" }
    }

    private val entries = LinkedHashMap<Long, CachedBrowserFavicon>(16, 0.75f, true)

    val size: Int
        @Synchronized get() = entries.size

    val totalBytes: Int
        @Synchronized get() = entries.values.sumOf(CachedBrowserFavicon::byteCount)

    @Synchronized
    fun offer(width: Int, height: Int, pngBytes: ByteArray): BrowserFaviconOffer {
        validate(width, height, pngBytes)?.let { return BrowserFaviconOffer.Rejected(it) }

        val id = stableId(pngBytes)
        entries[id]?.let { existing ->
            // Cryptographic collisions are not treated as equality. Rejecting the replacement is
            // safer than serving bytes under an id that already means something else.
            if (existing.width == width &&
                existing.height == height &&
                existing.internalBytes().contentEquals(pngBytes)
            ) {
                return BrowserFaviconOffer.Accepted(existing.copyForCaller(), inserted = false)
            }
            return BrowserFaviconOffer.Rejected(BrowserFaviconRejection.ENCODING_FAILED)
        }

        val stored = CachedBrowserFavicon(id, width, height, pngBytes)
        entries[id] = stored
        evictToBound()
        return BrowserFaviconOffer.Accepted(stored.copyForCaller(), inserted = true)
    }

    /** Encodes a WebView favicon only after its dimensions have passed the wire bound. */
    fun offer(bitmap: Bitmap): BrowserFaviconOffer {
        if (bitmap.width !in 1..MAX_WIDTH || bitmap.height !in 1..MAX_HEIGHT) {
            return BrowserFaviconOffer.Rejected(BrowserFaviconRejection.INVALID_DIMENSIONS)
        }
        val output = ByteArrayOutputStream(MAX_PNG_BYTES)
        if (!bitmap.compress(Bitmap.CompressFormat.PNG, 100, output)) {
            return BrowserFaviconOffer.Rejected(BrowserFaviconRejection.ENCODING_FAILED)
        }
        return offer(bitmap.width, bitmap.height, output.toByteArray())
    }

    @Synchronized
    fun get(id: Long): CachedBrowserFavicon? = entries[id]?.copyForCaller()

    @Synchronized
    fun contains(id: Long): Boolean = entries.containsKey(id)

    @Synchronized
    fun clear() {
        entries.clear()
    }

    private fun validate(width: Int, height: Int, bytes: ByteArray): BrowserFaviconRejection? = when {
        width !in 1..MAX_WIDTH || height !in 1..MAX_HEIGHT ->
            BrowserFaviconRejection.INVALID_DIMENSIONS
        bytes.isEmpty() -> BrowserFaviconRejection.EMPTY_PAYLOAD
        bytes.size > MAX_PNG_BYTES -> BrowserFaviconRejection.PAYLOAD_TOO_LARGE
        bytes.size < PNG_SIGNATURE.size ||
            !bytes.copyOfRange(0, PNG_SIGNATURE.size).contentEquals(PNG_SIGNATURE) ->
            BrowserFaviconRejection.NOT_PNG
        else -> null
    }

    private fun evictToBound() {
        while (entries.size > maxEntries) {
            val eldest = entries.entries.iterator()
            eldest.next()
            eldest.remove()
        }
    }

    private fun stableId(bytes: ByteArray): Long {
        val digest = MessageDigest.getInstance("SHA-256").digest(bytes)
        var value = 0L
        repeat(Long.SIZE_BYTES) { index ->
            value = (value shl 8) or (digest[index].toLong() and 0xFF)
        }
        value = value and Long.MAX_VALUE
        return if (value == 0L) 1L else value
    }

    private fun CachedBrowserFavicon.copyForCaller() =
        CachedBrowserFavicon(id, width, height, internalBytes())
}
