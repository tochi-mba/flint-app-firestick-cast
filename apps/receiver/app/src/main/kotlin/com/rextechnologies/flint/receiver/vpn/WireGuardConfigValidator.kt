package com.rextechnologies.flint.receiver.vpn

/**
 * Structural check for a WireGuard config text blob.
 *
 * This does not parse keys or endpoints; it only verifies that an `[Interface]` and a `[Peer]`
 * section are present so callers can reject obviously incomplete text before any tunnel work.
 * Config text must never be logged.
 */
object WireGuardConfigValidator {
    private val INTERFACE_HEADER = Regex("""(?m)^\s*\[Interface\]\s*$""")
    private val PEER_HEADER = Regex("""(?m)^\s*\[Peer\]\s*$""")

    fun isValid(configText: String): Boolean {
        if (configText.isBlank()) return false
        return INTERFACE_HEADER.containsMatchIn(configText) && PEER_HEADER.containsMatchIn(configText)
    }
}
