package com.rextechnologies.flint.protocol.dlna

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNull
import kotlin.test.assertTrue

class SsdpTest {
    private fun parse(raw: String): SsdpDevice? = Ssdp.parseResponse(raw.toByteArray(Charsets.US_ASCII))

    @Test
    fun `a search request carries the mandatory UPnP headers`() {
        val request = Ssdp.searchRequest().toString(Charsets.US_ASCII)

        assertTrue(request.startsWith("M-SEARCH * HTTP/1.1\r\n"))
        assertTrue(request.contains("HOST: 239.255.255.250:1900\r\n"))
        assertTrue(request.contains("MAN: \"ssdp:discover\"\r\n"))
        assertTrue(request.contains("MX: 2\r\n"))
        assertTrue(request.contains("ST: ${Ssdp.MEDIA_RENDERER}\r\n"))
        assertTrue(request.endsWith("\r\n\r\n"))
    }

    @Test
    fun `a search request accepts a custom target and wait`() {
        val request = Ssdp.searchRequest("ssdp:all", 4).toString(Charsets.US_ASCII)

        assertTrue(request.contains("ST: ssdp:all\r\n"))
        assertTrue(request.contains("MX: 4\r\n"))
    }

    @Test
    fun `an invalid search request is refused`() {
        assertFailsWith<IllegalArgumentException> { Ssdp.searchRequest(maximumWaitSeconds = 0) }
        assertFailsWith<IllegalArgumentException> { Ssdp.searchRequest(maximumWaitSeconds = 9) }
        assertFailsWith<IllegalArgumentException> { Ssdp.searchRequest("  ") }
        assertFailsWith<IllegalArgumentException> { Ssdp.searchRequest("bad\r\ninjection") }
    }

    @Test
    fun `a well-formed response is parsed`() {
        val device = parse(
            "HTTP/1.1 200 OK\r\n" +
                "CACHE-CONTROL: max-age=1800\r\n" +
                "LOCATION: http://192.168.12.34:8200/rootDesc.xml\r\n" +
                "SERVER: Linux/4.4 UPnP/1.0 MiniDLNA/1.2\r\n" +
                "ST: ${Ssdp.MEDIA_RENDERER}\r\n" +
                "USN: uuid:4d696e69-44c1::${Ssdp.MEDIA_RENDERER}\r\n\r\n",
        )

        assertEquals("http://192.168.12.34:8200/rootDesc.xml", device?.location)
        assertEquals(Ssdp.MEDIA_RENDERER, device?.searchTarget)
        assertTrue(device?.server.orEmpty().contains("MiniDLNA"))
    }

    @Test
    fun `a NOTIFY advertisement is accepted and byebye is ignored`() {
        val alive = parse(
            "NOTIFY * HTTP/1.1\r\nLOCATION: http://10.0.0.9/d.xml\r\n" +
                "NT: ${Ssdp.MEDIA_RENDERER}\r\nNTS: ssdp:alive\r\nUSN: uuid:x\r\n\r\n",
        )
        assertEquals("http://10.0.0.9/d.xml", alive?.location)

        val byebye = parse(
            "NOTIFY * HTTP/1.1\r\nLOCATION: http://10.0.0.9/d.xml\r\n" +
                "NT: ${Ssdp.MEDIA_RENDERER}\r\nNTS: ssdp:byebye\r\nUSN: uuid:x\r\n\r\n",
        )
        assertNull(byebye, "A renderer that is going away must not become a selectable target")
    }

    @Test
    fun `responses missing required fields are ignored`() {
        assertNull(parse("HTTP/1.1 200 OK\r\nUSN: uuid:x\r\nST: a\r\n\r\n"))
        assertNull(parse("HTTP/1.1 200 OK\r\nLOCATION: http://h/d.xml\r\nST: a\r\n\r\n"))
        assertNull(parse("HTTP/1.1 200 OK\r\nLOCATION: http://h/d.xml\r\nUSN: uuid:x\r\n\r\n"))
        assertNull(parse("HTTP/1.1 404 Not Found\r\nLOCATION: http://h/d.xml\r\n\r\n"))
        assertNull(parse(""))
    }

    @Test
    fun `a non-http location is ignored`() {
        assertNull(
            parse("HTTP/1.1 200 OK\r\nLOCATION: file:///etc/passwd\r\nUSN: uuid:x\r\nST: a\r\n\r\n"),
        )
    }

    @Test
    fun `header names are matched case-insensitively`() {
        val device = parse(
            "http/1.1 200 ok\r\nlocation: http://h/d.xml\r\nusn: uuid:x\r\nst: a\r\n\r\n",
        )

        assertEquals("http://h/d.xml", device?.location)
    }

    @Test
    fun `an ssdp device requires a location and USN`() {
        assertFailsWith<IllegalArgumentException> { SsdpDevice(" ", "usn", "st") }
        assertFailsWith<IllegalArgumentException> { SsdpDevice("http://h", " ", "st") }
    }
}
