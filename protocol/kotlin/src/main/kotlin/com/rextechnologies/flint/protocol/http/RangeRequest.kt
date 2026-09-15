package com.rextechnologies.flint.protocol.http

data class ByteRange(val startInclusive: Long, val endInclusive: Long) {
    init {
        require(startInclusive >= 0)
        require(endInclusive >= startInclusive)
    }

    val length: Long
        get() = endInclusive - startInclusive + 1

    fun contentRange(resourceLength: Long): String {
        require(endInclusive < resourceLength)
        return "bytes $startInclusive-$endInclusive/$resourceLength"
    }
}

class RangeNotSatisfiableException(
    val resourceLength: Long,
    message: String,
) : IllegalArgumentException(message) {
    val contentRangeHeader: String = "bytes */$resourceLength"
}

object RangeRequest {
    /**
     * Returns null when no Range header was supplied. A syntactically invalid,
     * multi-range, empty-resource, or out-of-bounds request throws and maps to 416.
     */
    fun parse(header: String?, resourceLength: Long): ByteRange? {
        require(resourceLength >= 0) { "Resource length must not be negative" }
        if (header == null) return null
        val trimmed = header.trim()
        val equals = trimmed.indexOf('=')
        if (equals <= 0 || !trimmed.substring(0, equals).trim().equals("bytes", ignoreCase = true)) {
            unsatisfiable(resourceLength, "Only byte ranges are supported")
        }
        val specification = trimmed.substring(equals + 1).trim()
        if (specification.isEmpty() || ',' in specification) {
            unsatisfiable(resourceLength, "Multiple or empty ranges are not supported")
        }
        if (specification.count { it == '-' } != 1 || specification.any { it.isWhitespace() }) {
            unsatisfiable(resourceLength, "Malformed byte range")
        }
        val separator = specification.indexOf('-')
        val startText = specification.substring(0, separator)
        val endText = specification.substring(separator + 1)
        if (startText.isEmpty() && endText.isEmpty()) {
            unsatisfiable(resourceLength, "Byte range has no bounds")
        }
        if (!startText.isAsciiDigitsOrEmpty() || !endText.isAsciiDigitsOrEmpty()) {
            unsatisfiable(resourceLength, "Byte range contains a non-decimal bound")
        }

        if (resourceLength == 0L) unsatisfiable(resourceLength, "Empty resource has no byte range")

        if (startText.isEmpty()) {
            val suffixLength = endText.toLongOrNull()
                ?: unsatisfiable(resourceLength, "Suffix length overflow")
            if (suffixLength <= 0) unsatisfiable(resourceLength, "Suffix length must be positive")
            val actualLength = minOf(suffixLength, resourceLength)
            return ByteRange(resourceLength - actualLength, resourceLength - 1)
        }

        val start = startText.toLongOrNull()
            ?: unsatisfiable(resourceLength, "Range start overflow")
        if (start >= resourceLength) unsatisfiable(resourceLength, "Range starts beyond the resource")
        val requestedEnd = if (endText.isEmpty()) {
            resourceLength - 1
        } else {
            endText.toLongOrNull() ?: unsatisfiable(resourceLength, "Range end overflow")
        }
        if (requestedEnd < start) unsatisfiable(resourceLength, "Range end precedes its start")
        return ByteRange(start, minOf(requestedEnd, resourceLength - 1))
    }

    private fun String.isAsciiDigitsOrEmpty(): Boolean = all { it in '0'..'9' }

    private fun unsatisfiable(resourceLength: Long, message: String): Nothing =
        throw RangeNotSatisfiableException(resourceLength, message)
}


