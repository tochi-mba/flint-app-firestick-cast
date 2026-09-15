package com.rextechnologies.flint.protocol.http

import java.nio.charset.StandardCharsets

enum class HttpStatus(val code: Int, val reason: String) {
    OK(200, "OK"),
    PARTIAL_CONTENT(206, "Partial Content"),
    BAD_REQUEST(400, "Bad Request"),
    FORBIDDEN(403, "Forbidden"),
    NOT_FOUND(404, "Not Found"),
    METHOD_NOT_ALLOWED(405, "Method Not Allowed"),
    RANGE_NOT_SATISFIABLE(416, "Range Not Satisfiable"),
    INTERNAL_ERROR(500, "Internal Server Error"),
    SERVICE_UNAVAILABLE(503, "Service Unavailable"),
}

/**
 * Builds a response head.
 *
 * `no-store` and `Accept-Ranges` are always present: media URLs carry a session
 * token, so an intermediate cache must never retain them, and players need to
 * know seeking is available before they issue the first ranged request.
 */
class HttpResponseHead(
    val status: HttpStatus,
    val contentType: String? = null,
    val contentLength: Long? = null,
    val contentRange: String? = null,
    val keepAlive: Boolean = false,
    extraHeaders: Map<String, String> = emptyMap(),
) {
    private val extra = extraHeaders.toMap()

    init {
        require(contentLength == null || contentLength >= 0)
        require(extra.keys.none { it.isEmpty() || it.any(Char::isISOControl) })
        require(extra.values.none { value -> value.any(Char::isISOControl) })
    }

    fun render(): ByteArray = buildString {
        append("HTTP/1.1 ").append(status.code).append(" ").append(status.reason).append(CRLF)
        append("Server: Flint").append(CRLF)
        append("Accept-Ranges: bytes").append(CRLF)
        append("Cache-Control: no-store").append(CRLF)
        contentType?.let { append("Content-Type: ").append(it).append(CRLF) }
        contentLength?.let { append("Content-Length: ").append(it).append(CRLF) }
        contentRange?.let { append("Content-Range: ").append(it).append(CRLF) }
        extra.forEach { (name, value) -> append(name).append(": ").append(value).append(CRLF) }
        append("Connection: ").append(if (keepAlive) "keep-alive" else "close").append(CRLF)
        append(CRLF)
    }.toByteArray(StandardCharsets.US_ASCII)

    companion object {
        private const val CRLF = "\r\n"

        fun text(status: HttpStatus, body: String, keepAlive: Boolean = false): Pair<ByteArray, ByteArray> {
            val encoded = body.toByteArray(StandardCharsets.UTF_8)
            val head = HttpResponseHead(
                status = status,
                contentType = "text/plain; charset=utf-8",
                contentLength = encoded.size.toLong(),
                keepAlive = keepAlive,
            )
            return head.render() to encoded
        }
    }
}
