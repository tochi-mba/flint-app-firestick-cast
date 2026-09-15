package com.rextechnologies.flint.protocol.dlna

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class AvTransportTest {
    private val control = "http://10.0.0.5:8200/ctl/AVTransport"

    @Test
    fun `set uri carries the media URL inside a SOAP envelope`() {
        val request = AvTransport.setUri(control, "http://10.0.0.1:8/media/v1/t/i")

        assertEquals("\"${Ssdp.AV_TRANSPORT}#SetAVTransportURI\"", request.soapAction)
        val envelope = request.envelope
        assertTrue(envelope.startsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>"))
        assertTrue(envelope.contains("<u:SetAVTransportURI xmlns:u=\"${Ssdp.AV_TRANSPORT}\">"))
        assertTrue(envelope.contains("<CurrentURI>http://10.0.0.1:8/media/v1/t/i</CurrentURI>"))
        assertTrue(envelope.contains("</s:Envelope>"))
    }

    @Test
    fun `XML metacharacters in a URL are escaped rather than breaking the envelope`() {
        val request = AvTransport.setUri(control, "http://h/a?x=1&y=2", "<title>a</title>")

        assertTrue(request.envelope.contains("http://h/a?x=1&amp;y=2"))
        assertTrue(request.envelope.contains("&lt;title&gt;"))
        assertFalse(request.envelope.contains("<title>"))
    }

    @Test
    fun `transport actions produce their standard bodies`() {
        assertTrue(AvTransport.play(control).envelope.contains("<Speed>1</Speed>"))
        assertTrue(AvTransport.pause(control).envelope.contains("<u:Pause"))
        assertTrue(AvTransport.stop(control).envelope.contains("<u:Stop"))
        assertEquals(control, AvTransport.play(control).controlUrl)
    }

    @Test
    fun `seek formats the position as UPnP relative time`() {
        assertTrue(AvTransport.seek(control, 0).envelope.contains("<Target>0:00:00</Target>"))
        assertTrue(AvTransport.seek(control, 65).envelope.contains("<Target>0:01:05</Target>"))
        assertTrue(AvTransport.seek(control, 3_725).envelope.contains("<Target>1:02:05</Target>"))
        assertFailsWith<IllegalArgumentException> { AvTransport.seek(control, -1) }
    }

    @Test
    fun `DIDL metadata reflects the media kind`() {
        val video = AvTransport.didlMetadata("Clip", "http://h/v", "video/mp4")
        val audio = AvTransport.didlMetadata("Song", "http://h/a", "audio/mpeg")
        val photo = AvTransport.didlMetadata("Snap", "http://h/p", "image/jpeg")

        assertTrue(video.contains("object.item.videoItem"))
        assertTrue(audio.contains("object.item.audioItem.musicTrack"))
        assertTrue(photo.contains("object.item.imageItem.photo"))
        assertTrue(video.contains("<dc:title>Clip</dc:title>"))
        assertTrue(video.contains("http-get:*:video/mp4:*"))
    }

    @Test
    fun `a device description yields the AVTransport control URL`() {
        val description = AvTransport.parseDescription(SAMPLE_DESCRIPTION, "http://10.0.0.5:8200/rootDesc.xml")

        assertEquals("Living Room Renderer", description?.friendlyName)
        assertEquals("http://10.0.0.5:8200/ctl/AVTransport", description?.controlUrl)
        assertEquals(Ssdp.AV_TRANSPORT, description?.serviceType)
    }

    @Test
    fun `a description without an AVTransport service or a name is unusable`() {
        assertNull(AvTransport.parseDescription("<root><device></device></root>", "http://h/d.xml"))
        assertNull(
            AvTransport.parseDescription(
                "<root><friendlyName>X</friendlyName><service>" +
                    "<serviceType>${Ssdp.RENDERING_CONTROL}</serviceType>" +
                    "<controlURL>/ctl/RC</controlURL></service></root>",
                "http://h/d.xml",
            ),
        )
        assertNull(
            AvTransport.parseDescription(
                "<root><friendlyName>X</friendlyName><service>" +
                    "<serviceType>${Ssdp.AV_TRANSPORT}</serviceType></service></root>",
                "http://h/d.xml",
            ),
        )
    }

    @Test
    fun `escaped names in a description are decoded`() {
        val description = AvTransport.parseDescription(
            "<root><friendlyName>Tom &amp; Jerry&apos;s TV</friendlyName><service>" +
                "<serviceType>${Ssdp.AV_TRANSPORT}</serviceType>" +
                "<controlURL>/ctl</controlURL></service></root>",
            "http://h:80/d.xml",
        )

        assertEquals("Tom & Jerry's TV", description?.friendlyName)
    }

    @Test
    fun `relative, absolute, and rooted control URLs all resolve`() {
        assertEquals(
            "http://10.0.0.5:8200/ctl/AV",
            AvTransport.resolveUrl("http://10.0.0.5:8200/desc/root.xml", "/ctl/AV"),
        )
        assertEquals(
            "http://10.0.0.5:8200/ctl/AV",
            AvTransport.resolveUrl("http://10.0.0.5:8200/desc/root.xml", "ctl/AV"),
        )
        assertEquals(
            "http://other/ctl",
            AvTransport.resolveUrl("http://10.0.0.5:8200/desc/root.xml", "http://other/ctl"),
        )
        assertEquals(
            "https://other/ctl",
            AvTransport.resolveUrl("http://10.0.0.5/d.xml", "https://other/ctl"),
        )
        assertEquals(
            "http://10.0.0.5:8200/ctl",
            AvTransport.resolveUrl("http://10.0.0.5:8200", "/ctl"),
        )
        assertEquals("/ctl", AvTransport.resolveUrl("not-a-url", "/ctl"))
    }

    @Test
    fun `a renderer description requires a name and a control URL`() {
        assertFailsWith<IllegalArgumentException> { RendererDescription(" ", "http://h/c") }
        assertFailsWith<IllegalArgumentException> { RendererDescription("Name", "  ") }
    }

    private companion object {
        val SAMPLE_DESCRIPTION = """
            <?xml version="1.0"?>
            <root xmlns="urn:schemas-upnp-org:device-1-0">
              <device>
                <friendlyName>Living Room Renderer</friendlyName>
                <serviceList>
                  <service>
                    <serviceType>${Ssdp.RENDERING_CONTROL}</serviceType>
                    <controlURL>/ctl/RenderingControl</controlURL>
                  </service>
                  <service>
                    <serviceType>${Ssdp.AV_TRANSPORT}</serviceType>
                    <controlURL>/ctl/AVTransport</controlURL>
                  </service>
                </serviceList>
              </device>
            </root>
        """.trimIndent()
    }
}
