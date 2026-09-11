package com.rextechnologies.flint.protocol.wire

import java.io.OutputStream
import java.nio.ByteBuffer

/**
 * The steady-state media path, written without allocating.
 *
 * [WireCodec.writeTo] builds a payload array for every frame it sends, and then [BinaryData] copies
 * the caller's bytes on the way in and again on the way out. At sixty frames a second with audio
 * alongside it, that is several megabytes a second of garbage produced by the one loop in the system
 * that must not touch a general-purpose allocator: a collection pause between two frames is a stutter
 * on the television, and it arrives exactly when the encoder is busiest.
 *
 * So the capture-encode-send loop uses this instead. Nothing here allocates: the caller owns a buffer
 * the encoder already filled, and the bytes go straight from it to the socket behind a small reusable
 * header array.
 *
 * The bytes produced are identical to the ones [WireCodec] produces for the same message, which is
 * asserted rather than assumed — this is a second way of writing the same format, not a second
 * format, and the golden corpus stays the authority on what that format is.
 *
 * **One writer at a time.** Every method here reuses one header array and writes it to the stream in
 * two calls, so two threads sharing a writer would interleave one frame's header with another's
 * payload and desynchronise the receiver for the rest of the session. Two threads sharing a *socket*
 * would do the same even with a writer each. The caller owns that exclusion — in this codebase the
 * session holds a single write lock across both calls and the encoder is the only producer — and
 * nothing in this class attempts to provide it, because a lock on the frame path is a lock taken
 * sixty times a second for a contention that a correct caller never has.
 */
class FrameWriter {
    /**
     * The envelope, plus the fixed part of a video or audio payload.
     *
     * Sized for the largest of the two: four bytes of length, eight of envelope, eight of
     * presentation time, one key-frame flag and four of payload length.
     */
    private val header = ByteArray(HEADER_CAPACITY)

    /**
     * Writes one video packet.
     *
     * @param data the encoder's own buffer. It is read from `offset` for `length` bytes and is never
     *   retained, copied or modified.
     */
    fun writeVideoPacket(
        output: OutputStream,
        protocolVersion: Int,
        presentationTimeUs: Long,
        keyFrame: Boolean,
        data: ByteArray,
        offset: Int,
        length: Int,
    ) {
        requireFrame(protocolVersion, presentationTimeUs, data.size, offset, length)
        require(length > 0) { "A video packet with no bytes in it is not a packet" }

        // presentation time (8) + key-frame flag (1) + binary length (4)
        val payloadLength = 8 + 1 + 4 + length
        var at = writeEnvelope(protocolVersion, WireMessageType.VIDEO.id, payloadLength)
        at = putLong(at, presentationTimeUs)
        header[at++] = if (keyFrame) 1 else 0
        at = putInt(at, length)

        output.write(header, 0, at)
        output.write(data, offset, length)
    }

    /** Writes one audio packet, which is the same shape without the key-frame flag. */
    fun writeAudioPacket(
        output: OutputStream,
        protocolVersion: Int,
        presentationTimeUs: Long,
        data: ByteArray,
        offset: Int,
        length: Int,
    ) {
        requireFrame(protocolVersion, presentationTimeUs, data.size, offset, length)
        require(length > 0) { "An audio packet with no bytes in it is not a packet" }

        val payloadLength = 8 + 4 + length
        var at = writeEnvelope(protocolVersion, WireMessageType.AUDIO.id, payloadLength)
        at = putLong(at, presentationTimeUs)
        at = putInt(at, length)

        output.write(header, 0, at)
        output.write(data, offset, length)
    }

    /**
     * The same for a direct buffer, which is what `MediaCodec` hands back.
     *
     * `ByteBuffer.get(ByteArray)` into one array the size of the frame would reintroduce the copy
     * this class exists to remove, so the buffer is drained a chunk at a time through one reusable
     * scratch block. That block is allocated once per writer, not once per frame.
     *
     * Unlike the array overload this **consumes** [data]: it writes everything from the buffer's
     * current position to its limit and leaves the position at the limit, which is what a caller
     * about to hand the same buffer back to `MediaCodec.releaseOutputBuffer` wants. Duplicate the
     * buffer first if the bytes are needed again.
     */
    fun writeVideoPacket(
        output: OutputStream,
        protocolVersion: Int,
        presentationTimeUs: Long,
        keyFrame: Boolean,
        data: ByteBuffer,
    ) {
        val length = data.remaining()
        require(length > 0) { "A video packet with no bytes in it is not a packet" }
        requireFrame(protocolVersion, presentationTimeUs, length, 0, length)

        val payloadLength = 8 + 1 + 4 + length
        var at = writeEnvelope(protocolVersion, WireMessageType.VIDEO.id, payloadLength)
        at = putLong(at, presentationTimeUs)
        header[at++] = if (keyFrame) 1 else 0
        at = putInt(at, length)

        output.write(header, 0, at)
        drain(output, data)
    }

    private fun drain(output: OutputStream, data: ByteBuffer) {
        val block = scratch ?: ByteArray(SCRATCH_BYTES).also { scratch = it }
        while (data.hasRemaining()) {
            val count = minOf(block.size, data.remaining())
            data.get(block, 0, count)
            output.write(block, 0, count)
        }
    }

    private var scratch: ByteArray? = null

    /** Returns the offset just past the envelope. */
    private fun writeEnvelope(protocolVersion: Int, typeId: Int, payloadLength: Int): Int {
        val bodyLength = ENVELOPE_LENGTH.toLong() + payloadLength
        if (bodyLength > WireCodec.MAX_FRAME_LENGTH) {
            throw WireFormatException("Frame is too large: $bodyLength bytes")
        }
        var at = putInt(0, bodyLength.toInt())
        at = putShort(at, MAGIC)
        at = putShort(at, protocolVersion)
        at = putShort(at, typeId)
        at = putShort(at, 0)
        return at
    }

    private fun requireFrame(
        protocolVersion: Int,
        presentationTimeUs: Long,
        capacity: Int,
        offset: Int,
        length: Int,
    ) {
        if (protocolVersion !in ProtocolVersion.MIN_SUPPORTED..ProtocolVersion.CURRENT) {
            throw UnsupportedProtocolVersionException(protocolVersion)
        }
        require(presentationTimeUs >= 0) { "A presentation time cannot be negative" }
        require(offset >= 0 && length >= 0 && offset + length <= capacity) {
            "The requested range is not inside the buffer"
        }
        if (length > MAX_MEDIA_PACKET_BYTES) {
            throw WireFormatException("Binary field is too long: $length bytes")
        }
    }

    private fun putShort(at: Int, value: Int): Int {
        header[at] = (value ushr 8).toByte()
        header[at + 1] = value.toByte()
        return at + 2
    }

    private fun putInt(at: Int, value: Int): Int {
        header[at] = (value ushr 24).toByte()
        header[at + 1] = (value ushr 16).toByte()
        header[at + 2] = (value ushr 8).toByte()
        header[at + 3] = value.toByte()
        return at + 4
    }

    private fun putLong(at: Int, value: Long): Int {
        var index = at
        var shift = 56
        while (shift >= 0) {
            header[index++] = (value ushr shift).toByte()
            shift -= 8
        }
        return index
    }

    private companion object {
        const val ENVELOPE_LENGTH = 8
        const val MAGIC = 0x5243
        const val HEADER_CAPACITY = 4 + ENVELOPE_LENGTH + 8 + 1 + 4
        const val MAX_MEDIA_PACKET_BYTES = 15 * 1024 * 1024

        /** One block, reused for the life of the writer. 64 KiB matches the receiver's socket buffer. */
        const val SCRATCH_BYTES = 64 * 1024
    }
}
