package com.rextechnologies.flint.protocol.http

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertTrue

class HttpResponseHeadTest {
    private fun render(head: HttpResponseHead): String = head.render().toString(Charsets.US_ASCII)

    @Test
    fun `every response forbids caching and advertises range support`() {
        val rendered = render(HttpResponseHead(HttpStatus.OK, contentLength = 0))

        assertTrue(rendered.startsWith("HTTP/1.1 200 OK\r\n"))
        assertTrue(rendered.contains("Cache-Control: no-store\r\n"))
        assertTrue(rendered.contains("Accept-Ranges: bytes\r\n"))
        assertTrue(rendered.endsWith("\r\n\r\n"))
    }

    @Test
    fun `partial content carries its content range`() {
        val rendered = render(
            HttpResponseHead(
                status = HttpStatus.PARTIAL_CONTENT,
                contentType = "video/mp4",
                contentLength = 100,
                contentRange = "bytes 0-99/500",
                keepAlive = true,
            ),
        )

        assertTrue(rendered.contains("HTTP/1.1 206 Partial Content\r\n"))
        assertTrue(rendered.contains("Content-Type: video/mp4\r\n"))
        assertTrue(rendered.contains("Content-Length: 100\r\n"))
        assertTrue(rendered.contains("Content-Range: bytes 0-99/500\r\n"))
        assertTrue(rendered.contains("Connection: keep-alive\r\n"))
    }

    @Test
    fun `connection close is the default`() {
        assertTrue(render(HttpResponseHead(HttpStatus.NOT_FOUND)).contains("Connection: close\r\n"))
    }

    @Test
    fun `extra headers are rendered`() {
        val rendered = render(
            HttpResponseHead(HttpStatus.OK, extraHeaders = mapOf("Allow" to "GET, HEAD")),
        )

        assertTrue(rendered.contains("Allow: GET, HEAD\r\n"))
    }

    @Test
    fun `header injection through an extra header is refused`() {
        assertFailsWith<IllegalArgumentException> {
            HttpResponseHead(HttpStatus.OK, extraHeaders = mapOf("X" to "a\r\nEvil: 1"))
        }
        assertFailsWith<IllegalArgumentException> {
            HttpResponseHead(HttpStatus.OK, extraHeaders = mapOf("A\r\nB" to "v"))
        }
        assertFailsWith<IllegalArgumentException> {
            HttpResponseHead(HttpStatus.OK, extraHeaders = mapOf("" to "v"))
        }
    }

    @Test
    fun `a negative content length is refused`() {
        assertFailsWith<IllegalArgumentException> {
            HttpResponseHead(HttpStatus.OK, contentLength = -1)
        }
    }

    @Test
    fun `text builds a matching head and body`() {
        val (head, body) = HttpResponseHead.text(HttpStatus.RANGE_NOT_SATISFIABLE, "nope")

        assertEquals("nope", body.toString(Charsets.UTF_8))
        val rendered = head.toString(Charsets.US_ASCII)
        assertTrue(rendered.contains("416 Range Not Satisfiable"))
        assertTrue(rendered.contains("Content-Length: 4\r\n"))
        assertTrue(rendered.contains("Content-Type: text/plain; charset=utf-8\r\n"))
    }

    @Test
    fun `text can keep the connection open`() {
        val (head, _) = HttpResponseHead.text(HttpStatus.NOT_FOUND, "x", keepAlive = true)

        assertTrue(head.toString(Charsets.US_ASCII).contains("Connection: keep-alive"))
    }

    @Test
    fun `status codes match their reason phrases`() {
        assertEquals(503, HttpStatus.SERVICE_UNAVAILABLE.code)
        assertEquals("Forbidden", HttpStatus.FORBIDDEN.reason)
        assertEquals(405, HttpStatus.METHOD_NOT_ALLOWED.code)
        assertEquals(400, HttpStatus.BAD_REQUEST.code)
        assertEquals(500, HttpStatus.INTERNAL_ERROR.code)
    }
}
