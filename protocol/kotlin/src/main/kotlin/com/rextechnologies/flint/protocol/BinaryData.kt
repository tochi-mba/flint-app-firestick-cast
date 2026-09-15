package com.rextechnologies.flint.protocol

/**
 * An immutable-by-contract byte sequence with content equality.
 *
 * Both construction and access copy the underlying array so protocol model
 * instances cannot be changed after they have been validated or authenticated.
 */
class BinaryData private constructor(private val value: ByteArray) {
    val size: Int
        get() = value.size

    val isEmpty: Boolean
        get() = value.isEmpty()

    fun toByteArray(): ByteArray = value.copyOf()

    internal fun writeTo(destination: ByteArray, destinationOffset: Int = 0) {
        value.copyInto(destination, destinationOffset)
    }

    internal fun <T> withBytes(block: (ByteArray) -> T): T = block(value)

    override fun equals(other: Any?): Boolean =
        this === other || other is BinaryData && value.contentEquals(other.value)

    override fun hashCode(): Int = value.contentHashCode()

    override fun toString(): String = "BinaryData(${value.size} bytes)"

    companion object {
        val EMPTY: BinaryData = BinaryData(ByteArray(0))

        fun of(bytes: ByteArray): BinaryData =
            if (bytes.isEmpty()) EMPTY else BinaryData(bytes.copyOf())

        /**
         * A copy of one region of [bytes], for a caller that fills a reused buffer and needs only
         * the part it filled -- one copy rather than a `copyOfRange` followed by [of]'s own.
         */
        fun of(bytes: ByteArray, offset: Int, length: Int): BinaryData {
            require(offset >= 0 && length >= 0 && offset + length <= bytes.size) { "Region is outside the array" }
            return if (length == 0) EMPTY else BinaryData(bytes.copyOfRange(offset, offset + length))
        }
    }
}
