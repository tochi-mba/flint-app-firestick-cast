package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.receiver.vpn.WireGuardConfigValidator
import java.nio.charset.StandardCharsets

enum class VpnProvider {
    NONE,
    WIREGUARD,
}

/**
 * Per-profile VPN preferences for the TV browser.
 *
 * [configText] is opaque WireGuard configuration. Never log it.
 */
data class ProfileNetworkSettings(
    val vpnEnabled: Boolean = false,
    val provider: VpnProvider = VpnProvider.NONE,
    val autoConnectOnBrowserStart: Boolean = false,
    /** When VPN is enabled, browsing waits until the tunnel is connected. */
    val requireVpnBeforeBrowse: Boolean = false,
    /** Opaque WireGuard config text; never log this. */
    val configText: String = "",
) {
    val isConfiguredForAutoConnect: Boolean
        get() = vpnEnabled &&
            autoConnectOnBrowserStart &&
            provider == VpnProvider.WIREGUARD &&
            configText.isNotBlank() &&
            configUtf8Bytes() <= MAX_CONFIG_UTF8_BYTES &&
            WireGuardConfigValidator.isValid(configText)

    /**
     * True when browsing must wait for a verified VPN session. Incomplete config still blocks —
     * fail closed rather than browsing on the normal network.
     */
    val requiresVpnBeforeBrowse: Boolean
        get() = vpnEnabled && requireVpnBeforeBrowse

    // Data-class defaults include every field. A settings object must be safe to interpolate into
    // diagnostics without turning a private WireGuard key into a log, assertion or crash report.
    override fun toString(): String =
        "ProfileNetworkSettings(vpnEnabled=$vpnEnabled, provider=$provider, " +
            "autoConnectOnBrowserStart=$autoConnectOnBrowserStart, " +
            "requireVpnBeforeBrowse=$requireVpnBeforeBrowse, configText=<redacted>)"

    /**
     * Returns a usable copy, or null when VPN is enabled but the configuration is incomplete or
     * oversized. Disabled settings always validate (cleared provider fields stay as stored).
     */
    fun validated(): ProfileNetworkSettings? {
        if (!vpnEnabled) return this
        if (provider != VpnProvider.WIREGUARD) return null
        if (configText.isBlank()) return null
        if (configUtf8Bytes() > MAX_CONFIG_UTF8_BYTES) return null
        if (!WireGuardConfigValidator.isValid(configText)) return null
        return this
    }

    private fun configUtf8Bytes(): Int = configText.toByteArray(StandardCharsets.UTF_8).size

    companion object {
        const val MAX_CONFIG_UTF8_BYTES = 64 * 1024
    }
}
