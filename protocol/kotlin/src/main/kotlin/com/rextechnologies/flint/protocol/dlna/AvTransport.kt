package com.rextechnologies.flint.protocol.dlna

/** The AVTransport control endpoint extracted from a device description. */
data class RendererDescription(
    val friendlyName: String,
    val controlUrl: String,
    val serviceType: String = Ssdp.AV_TRANSPORT,
) {
    init {
        require(friendlyName.isNotBlank()) { "Renderer name must not be blank" }
        require(controlUrl.isNotBlank()) { "Control URL must not be blank" }
    }
}

/**
 * SOAP bodies for the two AVTransport actions REX Cast needs.
 *
 * The full UPnP stack is deliberately not implemented. Handing a renderer a URL
 * and pressing play is the entire fallback contract; anything more would imply
 * a level of device compatibility that has not been tested.
 */
object AvTransport {
    fun setUri(controlUrl: String, mediaUrl: String, metadata: String = ""): SoapRequest =
        SoapRequest(
            controlUrl = controlUrl,
            action = "SetAVTransportURI",
            body = buildString {
                append("<InstanceID>0</InstanceID>")
                append("<CurrentURI>").append(escapeXml(mediaUrl)).append("</CurrentURI>")
                append("<CurrentURIMetaData>").append(escapeXml(metadata)).append("</CurrentURIMetaData>")
            },
        )

    fun play(controlUrl: String): SoapRequest = SoapRequest(
        controlUrl = controlUrl,
        action = "Play",
        body = "<InstanceID>0</InstanceID><Speed>1</Speed>",
    )

    fun pause(controlUrl: String): SoapRequest = SoapRequest(
        controlUrl = controlUrl,
        action = "Pause",
        body = "<InstanceID>0</InstanceID>",
    )

    fun stop(controlUrl: String): SoapRequest = SoapRequest(
        controlUrl = controlUrl,
        action = "Stop",
        body = "<InstanceID>0</InstanceID>",
    )

    fun seek(controlUrl: String, positionSeconds: Long): SoapRequest {
        require(positionSeconds >= 0) { "Seek position must not be negative" }
        val hours = positionSeconds / 3_600
        val minutes = (positionSeconds % 3_600) / 60
        val seconds = positionSeconds % 60
        val target = "%d:%02d:%02d".format(hours, minutes, seconds)
        return SoapRequest(
            controlUrl = controlUrl,
            action = "Seek",
            body = "<InstanceID>0</InstanceID><Unit>REL_TIME</Unit><Target>$target</Target>",
        )
    }

    /**
     * A minimal DIDL-Lite record.
     *
     * Some renderers refuse a bare URL and others ignore metadata entirely, so
     * this stays short: a title, a class, and the resource with its MIME type.
     */
    fun didlMetadata(title: String, mediaUrl: String, mimeType: String): String {
        val upnpClass = when {
            mimeType.startsWith("audio/") -> "object.item.audioItem.musicTrack"
            mimeType.startsWith("image/") -> "object.item.imageItem.photo"
            else -> "object.item.videoItem"
        }
        val protocolInfo = "http-get:*:$mimeType:*"
        return buildString {
            append("<DIDL-Lite xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\" ")
            append("xmlns:dc=\"http://purl.org/dc/elements/1.1/\" ")
            append("xmlns:upnp=\"urn:schemas-upnp-org:metadata-1-0/upnp/\">")
            append("<item id=\"0\" parentID=\"-1\" restricted=\"1\">")
            append("<dc:title>").append(escapeXml(title)).append("</dc:title>")
            append("<upnp:class>").append(upnpClass).append("</upnp:class>")
            append("<res protocolInfo=\"").append(escapeXml(protocolInfo)).append("\">")
            append(escapeXml(mediaUrl))
            append("</res></item></DIDL-Lite>")
        }
    }

    /** Reads the friendly name and AVTransport control URL out of a device description. */
    fun parseDescription(xml: String, baseUrl: String): RendererDescription? {
        val friendlyName = xml.tagValue("friendlyName")?.let(::unescapeXml) ?: return null
        val serviceBlock = SERVICE_BLOCK.findAll(xml)
            .map { it.value }
            .firstOrNull { it.contains(Ssdp.AV_TRANSPORT, ignoreCase = true) }
            ?: return null
        val controlUrl = serviceBlock.tagValue("controlURL")?.let(::unescapeXml) ?: return null
        return RendererDescription(friendlyName, resolveUrl(baseUrl, controlUrl))
    }

    /** Resolves a possibly relative UPnP URL against the device description location. */
    fun resolveUrl(baseUrl: String, reference: String): String {
        if (reference.startsWith("http://", ignoreCase = true) ||
            reference.startsWith("https://", ignoreCase = true)
        ) {
            return reference
        }
        val schemeEnd = baseUrl.indexOf("://")
        if (schemeEnd < 0) return reference
        val authorityEnd = baseUrl.indexOf('/', schemeEnd + 3)
        val origin = if (authorityEnd < 0) baseUrl else baseUrl.substring(0, authorityEnd)
        return if (reference.startsWith('/')) origin + reference else "$origin/$reference"
    }

    private fun String.tagValue(tag: String): String? =
        Regex("<(?:\\w+:)?$tag\\b[^>]*>(.*?)</(?:\\w+:)?$tag>", RegexOption.DOT_MATCHES_ALL)
            .find(this)
            ?.groupValues
            ?.get(1)
            ?.trim()
            ?.takeIf { it.isNotEmpty() }

    private fun escapeXml(value: String): String = buildString(value.length) {
        value.forEach { character ->
            when (character) {
                '&' -> append("&amp;")
                '<' -> append("&lt;")
                '>' -> append("&gt;")
                '"' -> append("&quot;")
                '\'' -> append("&apos;")
                else -> if (!character.isISOControl()) append(character)
            }
        }
    }

    private fun unescapeXml(value: String): String = value
        .replace("&lt;", "<")
        .replace("&gt;", ">")
        .replace("&quot;", "\"")
        .replace("&apos;", "'")
        .replace("&amp;", "&")

    private val SERVICE_BLOCK = Regex("<service>.*?</service>", RegexOption.DOT_MATCHES_ALL)
}

/** A ready-to-send SOAP call: the HTTP body plus the headers a renderer requires. */
data class SoapRequest(
    val controlUrl: String,
    val action: String,
    val body: String,
    val serviceType: String = Ssdp.AV_TRANSPORT,
) {
    val soapAction: String
        get() = "\"$serviceType#$action\""

    val envelope: String
        get() = buildString {
            append("<?xml version=\"1.0\" encoding=\"utf-8\"?>")
            append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" ")
            append("s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">")
            append("<s:Body>")
            append("<u:").append(action).append(" xmlns:u=\"").append(serviceType).append("\">")
            append(body)
            append("</u:").append(action).append(">")
            append("</s:Body></s:Envelope>")
        }
}

