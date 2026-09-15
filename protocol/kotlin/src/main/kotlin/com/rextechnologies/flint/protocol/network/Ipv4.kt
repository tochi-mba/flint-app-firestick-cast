package com.rextechnologies.flint.protocol.network

import java.net.Inet4Address
import java.net.InetAddress

object Ipv4 {
    fun parse(value: String): Inet4Address {
        val octets = value.split('.')
        require(octets.size == 4) { "Not an IPv4 address: $value" }
        val bytes = ByteArray(4)
        octets.forEachIndexed { index, octet ->
            require(octet.isNotEmpty() && octet.all { it in '0'..'9' }) {
                "Not an IPv4 address: $value"
            }
            val number = octet.toIntOrNull()
            require(number != null && number in 0..255) { "Not an IPv4 address: $value" }
            bytes[index] = number.toByte()
        }
        return InetAddress.getByAddress(bytes) as Inet4Address
    }

    internal fun toUnsignedLong(address: Inet4Address): Long = address.address.fold(0L) { result, byte ->
        (result shl 8) or (byte.toLong() and 0xff)
    }

    internal fun fromUnsignedLong(value: Long): Inet4Address {
        require(value in 0..0xffff_ffffL)
        val bytes = byteArrayOf(
            (value ushr 24).toByte(),
            (value ushr 16).toByte(),
            (value ushr 8).toByte(),
            value.toByte(),
        )
        return InetAddress.getByAddress(bytes) as Inet4Address
    }
}

data class Ipv4Subnet(
    val localAddress: Inet4Address,
    val prefixLength: Int,
) {
    init {
        require(prefixLength in 0..32) { "IPv4 prefix must be between 0 and 32" }
    }

    private val localValue = Ipv4.toUnsignedLong(localAddress)
    private val mask: Long = if (prefixLength == 0) 0L else {
        (0xffff_ffffL shl (32 - prefixLength)) and 0xffff_ffffL
    }

    val networkAddress: Inet4Address
        get() = Ipv4.fromUnsignedLong(localValue and mask)

    val broadcastAddress: Inet4Address
        get() = Ipv4.fromUnsignedLong((localValue and mask) or (mask xor 0xffff_ffffL))

    val addressCount: Long
        get() = 1L shl (32 - prefixLength)

    fun contains(address: Inet4Address): Boolean =
        (Ipv4.toUnsignedLong(address) and mask) == (localValue and mask)

    /** The first address [hosts] would yield, as a number. */
    private val firstHostValue: Long
        get() = Ipv4.toUnsignedLong(networkAddress) + if (prefixLength <= 30) 1 else 0

    /** The last address [hosts] would yield, as a number. */
    private val lastHostValue: Long
        get() = Ipv4.toUnsignedLong(broadcastAddress) - if (prefixLength <= 30) 1 else 0

    /**
     * How many addresses [hosts] would yield, without walking them.
     *
     * Counted here rather than by whoever needs the number, so that a caller sizing a sweep and the
     * sequence it then sweeps can never disagree. They did: the discovery sweep subtracted the local
     * address unconditionally where this subtracts it only when it actually falls inside the host
     * range, which on a subnet whose local address sits outside that range is one host the sweep
     * budgeted for and the sequence never produced.
     */
    fun usableHostCount(excludeLocalAddress: Boolean = true): Long {
        val first = firstHostValue
        val last = lastHostValue
        val rawCount = if (last < first) 0 else last - first + 1
        val localIsIncluded = excludeLocalAddress && localValue in first..last
        return rawCount - if (localIsIncluded) 1 else 0
    }

    /**
     * Enumerates usable peer addresses. Network/broadcast addresses are omitted
     * for prefixes /0 through /30; both endpoints are usable for /31 (RFC 3021).
     */
    fun hosts(
        excludeLocalAddress: Boolean = true,
        maximumHosts: Int = 65_536,
    ): Sequence<Inet4Address> {
        require(maximumHosts > 0)
        val resultCount = usableHostCount(excludeLocalAddress)
        require(resultCount <= maximumHosts.toLong()) {
            "Subnet has $resultCount hosts; maximumHosts is $maximumHosts"
        }

        val first = firstHostValue
        val last = lastHostValue
        return sequence {
            var value = first
            while (value <= last) {
                if (!excludeLocalAddress || value != localValue) yield(Ipv4.fromUnsignedLong(value))
                value++
            }
        }
    }
}


