package com.rextechnologies.flint.receiver.media

import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.io.OutputStream

/**
 * Reassembles a file the phone pushes down the control socket, one chunk at a time, in order.
 *
 * Pulled out of the service so its rules have tests: one transfer at a time, a new transfer
 * replacing whatever was pending, a bound on how much of the television's storage a phone may fill,
 * and a partial file that never outlives the failure that interrupted it. Every method is
 * synchronised because chunks arrive on the connection's read loop while [discard] arrives from the
 * service's own scope.
 *
 * Plain JVM on purpose -- a `File` and a directory are all it needs -- so the hash test that proves
 * bytes come out as they went in runs without a television.
 */
class PushedMediaSink(
    private val directory: File,
    private val freeBytes: () -> Long = { directory.usableSpace },
    private val clock: () -> Long = System::currentTimeMillis,
) {
    sealed interface Outcome {
        data object Accepted : Outcome

        data class Completed(val file: File) : Outcome

        data class Refused(val reason: String) : Outcome
    }

    private var stream: OutputStream? = null
    private var partial: File? = null
    private var receivedBytes: Long = 0

    /** The last file that arrived in full, until it is discarded or replaced. */
    var pending: File? = null
        private set

    /** Bytes of the transfer under way, for diagnostics. Zero between transfers. */
    val bytesReceived: Long
        @Synchronized get() = receivedBytes

    /** Takes one chunk. The first chunk of a transfer replaces any file that was pending before it. */
    @Synchronized
    fun accept(bytes: ByteArray, offset: Int, length: Int, isFinal: Boolean): Outcome {
        require(offset >= 0 && length >= 0 && offset + length <= bytes.size)
        val output = stream ?: begin() ?: return Outcome.Refused(NO_SPACE)
        if (freeBytes() < length + MINIMUM_FREE_BYTES) {
            abandon()
            return Outcome.Refused(NO_SPACE)
        }
        try {
            output.write(bytes, offset, length)
            receivedBytes += length
            if (!isFinal) return Outcome.Accepted
            output.flush()
            output.close()
        } catch (failure: IOException) {
            abandon()
            return Outcome.Refused(WRITE_FAILED)
        }
        val completed = checkNotNull(partial)
        pending = completed
        stream = null
        partial = null
        receivedBytes = 0
        return Outcome.Completed(completed)
    }

    fun accept(bytes: ByteArray, isFinal: Boolean): Outcome = accept(bytes, 0, bytes.size, isFinal)

    /** Removes the transfer under way and the file that was pending, if either exists. */
    @Synchronized
    fun discard() {
        abandon()
        pending?.delete()
        pending = null
    }

    private fun begin(): OutputStream? {
        // A new transfer is a new file. Whatever finished before it is no longer what the phone
        // wants played, and leaving it in the cache until the session ends is how a television
        // fills up.
        pending?.delete()
        pending = null
        if (freeBytes() < MINIMUM_FREE_BYTES) return null
        val file = File(directory, "pushed-media-${clock()}.tmp")
        return try {
            FileOutputStream(file).also {
                stream = it
                partial = file
                receivedBytes = 0
            }
        } catch (_: IOException) {
            file.delete()
            null
        }
    }

    private fun abandon() {
        runCatching { stream?.close() }
        partial?.delete()
        stream = null
        partial = null
        receivedBytes = 0
    }

    companion object {
        /**
         * What is left for everything else on the television after a pushed file.
         *
         * A Fire TV Stick has a few gigabytes in total and the receiver's cache is not the only thing
         * on it. Sixty-four megabytes is enough for the platform to keep working and small enough
         * that a feature film still fits.
         */
        const val MINIMUM_FREE_BYTES: Long = 64L * 1024 * 1024

        const val NO_SPACE: String = "this TV has too little free space to receive the file"
        const val WRITE_FAILED: String = "could not save the pushed file on this TV"
    }
}
