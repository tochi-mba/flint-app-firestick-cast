package com.rextechnologies.flint.protocol.discovery

import com.rextechnologies.flint.protocol.BinaryData
import java.net.Inet4Address
import java.nio.charset.StandardCharsets

data class FlintService(
    val instanceName: String,
    val hostName: String,
    val port: Int,
    val address: Inet4Address,
    val attributes: Map<String, String> = emptyMap(),
    val ttlSeconds: Long = 120,
) {
    init {
        require(instanceName.isNotBlank() && '.' !in instanceName)
        require(hostName.isNotBlank() && '.' !in hostName)
        require(port in 1..65_535)
        require(ttlSeconds in 0..0xffff_ffffL)
        require(attributes.size <= 64)
        require(attributes.keys.all { it.isNotEmpty() && '=' !in it })
    }
}

object FlintDnsSd {
    const val SERVICE_TYPE: String = "_rexcast._tcp.local"
    private const val RESPONSE_FLAGS = 0x8400

    fun query(): DnsPacket = DnsPacket(
        questions = listOf(DnsQuestion(SERVICE_TYPE, DnsType.PTR, DnsClass.IN)),
    )

    fun announcement(service: FlintService): DnsPacket {
        val instance = "${service.instanceName}.$SERVICE_TYPE"
        val host = "${service.hostName}.local"
        val uniqueClass = DnsClass.IN or DnsClass.CACHE_FLUSH
        val txtEntries = service.attributes.toSortedMap(String.CASE_INSENSITIVE_ORDER).map { (key, value) ->
            val encoded = "$key=$value".toByteArray(StandardCharsets.UTF_8)
            if (encoded.size > 255) throw DnsFormatException("DNS-SD TXT entry is too long: $key")
            BinaryData.of(encoded)
        }
        return DnsPacket(
            flags = RESPONSE_FLAGS,
            answers = listOf(PtrRecord(SERVICE_TYPE, instance, ttlSeconds = service.ttlSeconds)),
            additionals = listOf(
                SrvRecord(
                    name = instance,
                    priority = 0,
                    weight = 0,
                    port = service.port,
                    target = host,
                    recordClass = uniqueClass,
                    ttlSeconds = service.ttlSeconds,
                ),
                TxtRecord(instance, txtEntries, uniqueClass, service.ttlSeconds),
                ARecord(host, service.address, uniqueClass, service.ttlSeconds),
            ),
        )
    }

    fun extractServices(packet: DnsPacket): List<FlintService> {
        val records = packet.records
        val pointers = records.filterIsInstance<PtrRecord>()
            .filter { namesEqual(it.name, SERVICE_TYPE) }
        return pointers.mapNotNull { pointer ->
            val srv = records.filterIsInstance<SrvRecord>()
                .firstOrNull { namesEqual(it.name, pointer.target) } ?: return@mapNotNull null
            val address = records.filterIsInstance<ARecord>()
                .firstOrNull { namesEqual(it.name, srv.target) } ?: return@mapNotNull null
            val txt = records.filterIsInstance<TxtRecord>()
                .firstOrNull { namesEqual(it.name, pointer.target) }
            val canonicalTarget = pointer.target.trimEnd('.')
            val instanceSuffix = ".$SERVICE_TYPE"
            if (!canonicalTarget.endsWith(instanceSuffix, ignoreCase = true)) return@mapNotNull null
            val instanceName = canonicalTarget.dropLast(instanceSuffix.length)
            val hostName = srv.target.removeSuffixCaseInsensitive(".local") ?: return@mapNotNull null
            try {
                FlintService(
                    instanceName = instanceName,
                    hostName = hostName,
                    port = srv.port,
                    address = address.address,
                    attributes = parseAttributes(txt),
                    ttlSeconds = minOf(pointer.ttlSeconds, srv.ttlSeconds, address.ttlSeconds),
                )
            } catch (_: IllegalArgumentException) {
                null
            }
        }.distinctBy { Triple(it.instanceName.lowercase(), it.hostName.lowercase(), it.port) }
    }

    private fun parseAttributes(txt: TxtRecord?): Map<String, String> {
        if (txt == null) return emptyMap()
        return buildMap {
            txt.entries.forEach { entry ->
                val text = entry.withBytes { String(it, StandardCharsets.UTF_8) }
                val separator = text.indexOf('=')
                val key = if (separator < 0) text else text.substring(0, separator)
                val value = if (separator < 0) "" else text.substring(separator + 1)
                if (key.isNotEmpty() && '=' !in key) put(key, value)
            }
        }
    }

    private fun namesEqual(left: String, right: String): Boolean =
        left.trimEnd('.').equals(right.trimEnd('.'), ignoreCase = true)

    private fun String.removeSuffixCaseInsensitive(suffix: String): String? =
        if (endsWith(suffix, ignoreCase = true)) dropLast(suffix.length) else null
}

