package com.rextechnologies.flint.castcore.setup

/**
 * Which ports to try for ADB on one television, and in what order.
 *
 * Fire OS answers ADB over the network on a bounded range rather than only on 5555, so the range is
 * tried rather than the one port. It is tried on the one address a person chose or typed and never
 * widened into a sweep: thirty-one ports on one television is a look; thirty-one ports on every
 * address is a scan, and this app does not do that.
 */
object AdbPortScan {
    const val FIRST_PORT: Int = 5_555
    const val LAST_PORT: Int = 5_585

    val PORTS: IntRange = FIRST_PORT..LAST_PORT

    /** Every port to try, with the one that answered last time first when there is one. */
    fun order(knownPort: Int): List<Int> {
        require(knownPort in 0..65_535)
        if (knownPort == 0) return PORTS.toList()
        return listOf(knownPort) + PORTS.filter { it != knownPort }
    }
}
