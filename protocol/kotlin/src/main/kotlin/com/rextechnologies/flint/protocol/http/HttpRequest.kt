package com.rextechnologies.flint.protocol.http

import java.io.IOException
import java.io.InputStream
import java.util.Locale

class HttpFormatException(message: String) : IOException(message)

enum class HttpMethod {
    GET,
    HEAD,
    OPTIONS,
    ;

    companion object {
        fun parseOrNull(value: String): HttpMethod? = entries.firstOrNull { it.name == value }
    }
}

/**
 * A deliberately small read-only HTTP/1.1 request reader.
 *
 * The media server answers `GET`, `HEAD`, and `OPTIONS` for opaque token paths only,
 * so request bodies, chunked transfer, and every mutating method are refused rather
 * than parsed. Refusing is a smaller attack surface than implementing.
 */
data class HttpRequest(
    val method: HttpMethod,
    val target: String,
    val headers: Map<String, String>,
) {
    fun header(name: String): String? = headers[name.lowercase(Locale.ROOT)]

    val isKeepAlive: Boolean
        get() = !header("connection").equals("close", ignoreCase = true)

    companion object {
        const val MAX_REQUEST_LINE_BYTES: Int = 8 * 1024
        const val MAX_HEADER_BYTES: Int = 16 * 1024
        const val MAX_HEADER_COUNT: Int = 64

        /** Returns null on a clean EOF before a request line begins. */
        fun read(input: InputStream): HttpRequest? {
            val requestLine = readLine(input, MAX_REQUEST_LINE_BYTES, "request line") ?: return null
            if (requestLine.isEmpty()) throw HttpFormatException("Empty request line")

            val parts = requestLine.split(' ')
            if (parts.size != 3) throw HttpFormatException("Malformed request line")
            val method = HttpMethod.parseOrNull(parts[0])
                ?: throw HttpFormatException("Unsupported method: ${parts[0]}")
            val target = parts[1]
            if (target.isEmpty() || !target.startsWith('/')) {
                throw HttpFormatException("Request target must be an origin-form path")
            }
            if (!parts[2].startsWith("HTTP/1.")) {
                throw HttpFormatException("Unsupported HTTP version: ${parts[2]}")
            }

            val headers = LinkedHashMap<String, String>()
            var budget = MAX_HEADER_BYTES
            while (true) {
                val line = readLine(input, minOf(budget, MAX_HEADER_BYTES), "header")
                    ?: throw HttpFormatException("Truncated headers")
                if (line.isEmpty()) break
                budget -= line.length + 2
                if (budget <= 0) throw HttpFormatException("Header block is too large")
                if (headers.size >= MAX_HEADER_COUNT) {
                    throw HttpFormatException("Too many headers")
                }
                val separator = line.indexOf(':')
                if (separator <= 0) throw HttpFormatException("Malformed header line")
                val name = line.substring(0, separator).trim().lowercase(Locale.ROOT)
                if (name.isEmpty() || name.any { it.isWhitespace() }) {
                    throw HttpFormatException("Malformed header name")
                }
                val value = line.substring(separator + 1).trim()
                // Repeated headers are joined per RFC 9110 rather than silently overwritten.
                headers[name] = headers[name]?.let { "$it, $value" } ?: value
            }

            if (headers.containsKey("transfer-encoding") ||
                headers["content-length"]?.toLongOrNull()?.let { it > 0 } == true
            ) {
                throw HttpFormatException("Request bodies are not accepted")
            }
            return HttpRequest(method, target, headers)
        }

        private fun readLine(input: InputStream, limit: Int, description: String): String? {
            val builder = StringBuilder()
            var sawCarriageReturn = false
            while (true) {
                val byte = input.read()
                if (byte < 0) {
                    if (builder.isEmpty() && !sawCarriageReturn) return null
                    throw HttpFormatException("Truncated $description")
                }
                if (byte == '\n'.code) {
                    if (sawCarriageReturn) builder.setLength(builder.length - 1)
                    return builder.toString()
                }
                sawCarriageReturn = byte == '\r'.code
                if (byte == 0) throw HttpFormatException("NUL byte in $description")
                builder.append(byte.toChar())
                if (builder.length > limit) throw HttpFormatException("$description is too long")
            }
        }
    }
}
