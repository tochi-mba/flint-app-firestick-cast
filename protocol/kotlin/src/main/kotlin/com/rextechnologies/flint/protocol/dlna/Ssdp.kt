package com.rextechnologies.flint.protocol.dlna

import java.util.Locale

/** A renderer discovered by SSDP, before its device description has been fetched. */
data class SsdpDevice(
    val location: String,
    val usn: String,
    val searchTarget: String,
    val server: String? = null,
) {
    init {
        require(location.isNotBlank()) { "SSDP location must not be blank" }
        require(usn.isNotBlank()) { "SSDP USN must not be blank" }
    }
}

/**
 * Simple Service Discovery Protocol, restricted to finding media renderers.
 *
 * The multicast group is fixed by the UPnP specification, but the socket that
 * carries it must still be pinned to the tether interface by the caller: on a
 * phone that also has cellular up, an unbound SSDP socket reaches neither the
 * TV nor anything else useful.
 */
object Ssdp {
    const val MULTICAST_ADDRESS: String = "239.255.255.250"
    const val PORT: Int = 1900
    const val MEDIA_RENDERER: String = "urn:schemas-upnp-org:device:MediaRenderer:1"
    const val AV_TRANSPORT: String = "urn:schemas-upnp-org:service:AVTransport:1"
    const val RENDERING_CONTROL: String = "urn:schemas-upnp-org:service:RenderingControl:1"

    /** Builds an M-SEARCH datagram. [maximumWaitSeconds] is the MX header. */
    fun searchRequest(
        searchTarget: String = MEDIA_RENDERER,
        maximumWaitSeconds: Int = 2,
    ): ByteArray {
        require(searchTarget.isNotBlank() && searchTarget.none(Char::isISOControl))
        require(maximumWaitSeconds in 1..5) { "SSDP MX must be between 1 and 5 seconds" }
        return buildString {
            append("M-SEARCH * HTTP/1.1\r\n")
            append("HOST: ").append(MULTICAST_ADDRESS).append(':').append(PORT).append("\r\n")
            append("MAN: \"ssdp:discover\"\r\n")
            append("MX: ").append(maximumWaitSeconds).append("\r\n")
            append("ST: ").append(searchTarget).append("\r\n")
            append("USER-AGENT: Flint/1 UPnP/1.0\r\n")
            append("\r\n")
        }.toByteArray(Charsets.US_ASCII)
    }

    /** Parses an M-SEARCH response or a NOTIFY advertisement. Returns null when unusable. */
    fun parseResponse(payload: ByteArray): SsdpDevice? {
        val text = payload.toString(Charsets.US_ASCII)
        val lines = text.split("\r\n", "\n").filter { it.isNotBlank() }
        if (lines.isEmpty()) return null
        val start = lines.first().uppercase(Locale.ROOT)
        if (!start.startsWith("HTTP/1.1 200") && !start.startsWith("NOTIFY")) return null

        val headers = HashMap<String, String>()
        lines.drop(1).forEach { line ->
            val separator = line.indexOf(':')
            if (separator > 0) {
                val name = line.substring(0, separator).trim().uppercase(Locale.ROOT)
                headers[name] = line.substring(separator + 1).trim()
            }
        }
        // A byebye advertisement announces a renderer that is going away, so it
        // must never be turned into a target the user can select.
        if (headers["NTS"]?.endsWith("byebye", ignoreCase = true) == true) return null

        val location = headers["LOCATION"]?.takeIf { it.startsWith("http://", ignoreCase = true) }
            ?: return null
        val usn = headers["USN"]?.takeIf { it.isNotBlank() } ?: return null
        val target = headers["ST"] ?: headers["NT"] ?: return null
        return SsdpDevice(location, usn, target, headers["SERVER"])
    }
}
