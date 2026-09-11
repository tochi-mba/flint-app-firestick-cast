package com.rextechnologies.flint.protocol.discovery

import java.nio.charset.StandardCharsets
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class ReceiverProbeTest {
    @Test
    fun `request is one ascii line terminated by a single line feed`() {
        val bytes = ReceiverProbe.requestBytes()
        assertEquals("REXCAST DISCOVER/1\n", String(bytes, StandardCharsets.US_ASCII))
        assertEquals(19, bytes.size)
        assertFalse(bytes.any { it < 0 }, "the request must stay inside ASCII")
    }

    @Test
    fun `request comparison is exact`() {
        assertTrue(ReceiverProbe.isRequest("REXCAST DISCOVER/1"))
        assertFalse(ReceiverProbe.isRequest("rexcast discover/1"))
        assertFalse(ReceiverProbe.isRequest(" REXCAST DISCOVER/1"))
        assertFalse(ReceiverProbe.isRequest("REXCAST DISCOVER/1 "))
        assertFalse(ReceiverProbe.isRequest("REXCAST DISCOVER/2"))
        assertFalse(ReceiverProbe.isRequest(""))
    }

    @Test
    fun `response is three tab separated fields and one line feed`() {
        val text = String(ReceiverProbe.responseBytes("Fire TV Stick 4K", 47_855), StandardCharsets.UTF_8)
        assertEquals("REXCAST RECEIVER/1\tFire TV Stick 4K\t47855\n", text)
    }

    @Test
    fun `response encodes a non ascii model name as utf8`() {
        val text = String(ReceiverProbe.responseBytes("Télé", 47_855), StandardCharsets.UTF_8)
        assertTrue(text.contains("Télé"))
    }

    @Test
    fun `response rejects an impossible port`() {
        assertFailsWith<IllegalArgumentException> { ReceiverProbe.responseBytes("Fire TV", 0) }
        assertFailsWith<IllegalArgumentException> { ReceiverProbe.responseBytes("Fire TV", 65_536) }
    }

    @Test
    fun `every record separator in a model name becomes a space`() {
        assertEquals("a b c d", ReceiverProbe.sanitizeModelName("a\tb\rc\nd"))
    }

    @Test
    fun `a blank model name falls back rather than producing an empty field`() {
        assertEquals("Fire TV", ReceiverProbe.sanitizeModelName("   "))
        assertEquals("Fire TV", ReceiverProbe.sanitizeModelName("\t\n"))
    }

    @Test
    fun `parses the response the receiver actually sends`() {
        val parsed = ReceiverProbe.parseResponse("REXCAST RECEIVER/1\tFire TV Stick\t47855\n")
        assertEquals(ReceiverAnnouncement("Fire TV Stick", 47_855), parsed)
    }

    @Test
    fun `tolerates a carriage return before the line feed`() {
        val parsed = ReceiverProbe.parseResponse("REXCAST RECEIVER/1\tFire TV\t47855\r\n")
        assertEquals(47_855, parsed?.port)
    }

    @Test
    fun `reports the bound port rather than assuming the default`() {
        val parsed = ReceiverProbe.parseResponse("REXCAST RECEIVER/1\tFire TV\t50123")
        assertEquals(50_123, parsed?.port)
    }

    @Test
    fun `a blank name in a well formed response still identifies a receiver`() {
        assertEquals("Fire TV", ReceiverProbe.parseResponse("REXCAST RECEIVER/1\t \t47855")?.modelName)
    }

    @Test
    fun `rejects anything that is not this announcement`() {
        assertNull(ReceiverProbe.parseResponse(""))
        assertNull(ReceiverProbe.parseResponse("HTTP/1.1 200 OK"))
        assertNull(ReceiverProbe.parseResponse("REXCAST RECEIVER/1\tFire TV"))
        assertNull(ReceiverProbe.parseResponse("REXCAST RECEIVER/1\tFire TV\t47855\textra"))
        assertNull(ReceiverProbe.parseResponse("REXCAST RECEIVER/2\tFire TV\t47855"))
        assertNull(ReceiverProbe.parseResponse("REXCAST RECEIVER/1\tFire TV\tnot-a-port"))
        assertNull(ReceiverProbe.parseResponse("REXCAST RECEIVER/1\tFire TV\t0"))
        assertNull(ReceiverProbe.parseResponse("REXCAST RECEIVER/1\tFire TV\t65536"))
    }

    @Test
    fun `refuses to read an unbounded line from an unknown host`() {
        val overlong = "REXCAST RECEIVER/1\t" + "n".repeat(ReceiverProbe.MAX_RESPONSE_BYTES) + "\t47855"
        assertNull(ReceiverProbe.parseResponse(overlong))
    }

    @Test
    fun `a round trip through both halves survives`() {
        val bytes = ReceiverProbe.responseBytes("Fire TV\tStick", 47_855)
        val parsed = ReceiverProbe.parseResponse(String(bytes, StandardCharsets.UTF_8))
        assertEquals(ReceiverAnnouncement("Fire TV Stick", 47_855), parsed)
    }

    @Test
    fun `an announcement refuses to carry a field separator or an impossible port`() {
        assertFailsWith<IllegalArgumentException> { ReceiverAnnouncement("Fire\tTV", 47_855) }
        assertFailsWith<IllegalArgumentException> { ReceiverAnnouncement("Fire TV", 0) }
    }
}

class ReceiverProbeRoundTripTest {
    @Test
    fun `parsing what the receiver wrote gives back what it meant, for arbitrary names and ports`() {
        // Asserted only by example before. The property is the one the two sides actually rely on:
        // whatever the television calls itself, the phone reads the same name and the same port.
        val alphabet = listOf("a", "Z", " ", "-", "é", "東", "😀", "\t", "\n", "\r", "")
        var seed = 7L
        fun next(): Long {
            seed = seed * 6_364_136_223_846_793_005L + 1_442_695_040_888_963_407L
            return seed ushr 33
        }
        repeat(500) {
            val length = (next() % 40).toInt()
            val name = buildString {
                repeat(length) { append(alphabet[(next() % alphabet.size).toInt()]) }
            }
            val port = (next() % 65_535).toInt() + 1
            val line = String(ReceiverProbe.responseBytes(name, port), StandardCharsets.UTF_8)
            val parsed = assertNotNull(ReceiverProbe.parseResponse(line), "\"$name\" at $port")
            assertEquals(port, parsed.port)
            assertEquals(ReceiverProbe.sanitizeModelName(name), parsed.modelName)
            assertTrue(parsed.modelName.isNotBlank())
            assertTrue(line.toByteArray(StandardCharsets.UTF_8).size <= ReceiverProbe.MAX_RESPONSE_BYTES)
        }
    }
}
