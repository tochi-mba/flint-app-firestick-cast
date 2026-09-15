package com.rextechnologies.flint.protocol.discovery

import com.rextechnologies.flint.protocol.BinaryData
import java.net.Inet4Address

object DnsType {
    const val A: Int = 1
    const val PTR: Int = 12
    const val TXT: Int = 16
    const val AAAA: Int = 28
    const val SRV: Int = 33
    const val ANY: Int = 255
}

object DnsClass {
    const val IN: Int = 1
    const val CACHE_FLUSH: Int = 0x8000
}

data class DnsQuestion(
    val name: String,
    val type: Int,
    val questionClass: Int = DnsClass.IN,
) {
    init {
        require(type in 0..0xffff)
        require(questionClass in 0..0xffff)
    }
}

sealed interface DnsRecord {
    val name: String
    val type: Int
    val recordClass: Int
    val ttlSeconds: Long
}

data class PtrRecord(
    override val name: String,
    val target: String,
    override val recordClass: Int = DnsClass.IN,
    override val ttlSeconds: Long = 120,
) : DnsRecord {
    override val type: Int = DnsType.PTR
}

data class SrvRecord(
    override val name: String,
    val priority: Int,
    val weight: Int,
    val port: Int,
    val target: String,
    override val recordClass: Int = DnsClass.IN,
    override val ttlSeconds: Long = 120,
) : DnsRecord {
    init {
        require(priority in 0..0xffff)
        require(weight in 0..0xffff)
        require(port in 0..0xffff)
    }

    override val type: Int = DnsType.SRV
}

data class TxtRecord(
    override val name: String,
    val entries: List<BinaryData>,
    override val recordClass: Int = DnsClass.IN,
    override val ttlSeconds: Long = 120,
) : DnsRecord {
    init {
        require(entries.all { it.size <= 255 })
    }

    override val type: Int = DnsType.TXT
}

data class ARecord(
    override val name: String,
    val address: Inet4Address,
    override val recordClass: Int = DnsClass.IN,
    override val ttlSeconds: Long = 120,
) : DnsRecord {
    override val type: Int = DnsType.A
}

data class RawDnsRecord(
    override val name: String,
    override val type: Int,
    override val recordClass: Int,
    override val ttlSeconds: Long,
    val data: BinaryData,
) : DnsRecord {
    init {
        require(type in 0..0xffff)
    }
}

data class DnsPacket(
    val id: Int = 0,
    val flags: Int = 0,
    val questions: List<DnsQuestion> = emptyList(),
    val answers: List<DnsRecord> = emptyList(),
    val authorities: List<DnsRecord> = emptyList(),
    val additionals: List<DnsRecord> = emptyList(),
) {
    init {
        require(id in 0..0xffff)
        require(flags in 0..0xffff)
        require(questions.size <= 0xffff)
        require(answers.size <= 0xffff)
        require(authorities.size <= 0xffff)
        require(additionals.size <= 0xffff)
    }

    val records: List<DnsRecord>
        get() = answers + authorities + additionals
}


