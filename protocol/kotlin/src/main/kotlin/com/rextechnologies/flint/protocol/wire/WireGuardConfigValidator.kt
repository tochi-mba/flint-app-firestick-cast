package com.rextechnologies.flint.protocol.wire

/**
 * Structural check for a WireGuard config text blob.
 *
 * This does not parse keys or endpoints; it only verifies that an `[Interface]` and a `[Peer]`
 * section are present so the wire layer can reject incomplete Set commands.
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
