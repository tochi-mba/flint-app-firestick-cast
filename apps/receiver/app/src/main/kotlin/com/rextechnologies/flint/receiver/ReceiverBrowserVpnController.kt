package com.rextechnologies.flint.receiver

import android.util.Log
import com.rextechnologies.flint.receiver.browser.BrowserCockpitPublisher
import com.rextechnologies.flint.receiver.browser.BrowserCoordinator
import com.rextechnologies.flint.receiver.browser.BrowserNetworkStore
import com.rextechnologies.flint.receiver.browser.BrowserVpnConnectionVerifier
import com.rextechnologies.flint.receiver.browser.BrowserVpnCoordinator
import com.rextechnologies.flint.receiver.browser.BrowserVpnLossPolicy
import com.rextechnologies.flint.receiver.browser.BrowserVpnState
import com.rextechnologies.flint.receiver.browser.BrowserVpnTunnel
import com.rextechnologies.flint.receiver.browser.ProfileNetworkSettings
import com.rextechnologies.flint.receiver.browser.VpnCapability
import com.rextechnologies.flint.receiver.browser.VpnCapabilityProbe
import com.rextechnologies.flint.receiver.browser.VpnConsentHost
import com.rextechnologies.flint.receiver.browser.VpnProvider
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch

/**
 * Profile-gated VPN for the TV browser: settings, consent, connection and the browsing gate.
 *
 * Split out of [ReceiverBrowserController] because it is a policy of its own, and because its
 * rules only make sense read together:
 *
 *  * Settings are per TV profile. The television is shared; a tunnel is not.
 *  * Consent belongs to Android and to a person. This never bypasses `VpnService.prepare`, and a
 *    consent request is single-flight and invalidated by a profile or session change — a callback
 *    arriving for a profile nobody is using any more must not connect anything.
 *  * The browsing gate fails closed. When a profile requires VPN, pages stay blocked in every
 *    state except Connected, including the states that look like progress.
 *
 * "Connected" here means Android reported a validated VPN network, not that a tunnel reported UP.
 * This is not a packet kill switch and is not described as one.
 */
internal class ReceiverBrowserVpnController(
    private val scope: CoroutineScope,
    private val coordinator: BrowserCoordinator,
    private val cockpitPublisher: BrowserCockpitPublisher,
    private val networkStore: BrowserNetworkStore?,
    private val activeProfileId: () -> String,
    private val showNotice: (String) -> Unit,
    private val probe: VpnCapabilityProbe,
    tunnel: BrowserVpnTunnel,
    needsConsent: () -> Boolean,
    verifier: BrowserVpnConnectionVerifier,
) {
    private val vpn = BrowserVpnCoordinator(
        tunnel = tunnel,
        networkStore = networkStore,
        needsConsent = needsConsent,
        verifier = verifier,
    )

    val state get() = vpn.state

    @Volatile
    private var consentHost: VpnConsentHost? = null

    @Volatile
    private var consentRequestId = 0L

    @Volatile
    private var consentInFlight = false

    fun attachConsentHost(host: VpnConsentHost?) {
        consentHost = host
    }

    /** Runs [onConnected] each time the tunnel reaches a verified connection. */
    fun onConnected(onConnected: () -> Unit) {
        scope.launch {
            vpn.state.collect { if (it == BrowserVpnState.Connected) onConnected() }
        }
    }

    /**
     * Runs [onLost] when a verified tunnel stops being verified on a profile that requires one.
     *
     * Re-blocking new navigations is not enough on its own. A page that is already open keeps
     * fetching, and those requests leave over whatever network is left — so the profile that asked
     * for a tunnel before browsing has to have its pages stopped, not merely its next click
     * refused.
     */
    fun onRequiredTunnelLost(onLost: () -> Unit) {
        scope.launch {
            var wasConnected = false
            vpn.state.collect { state ->
                val connected = state == BrowserVpnState.Connected
                if (BrowserVpnLossPolicy.shouldClosePages(
                        wasConnected = wasConnected,
                        isConnected = connected,
                        requiresVpnBeforeBrowse = settingsForActiveProfile().requiresVpnBeforeBrowse,
                    )
                ) {
                    onLost()
                }
                wasConnected = connected
            }
        }
    }

    fun capability(): VpnCapability = probe.probe()

    fun settingsForActiveProfile(): ProfileNetworkSettings =
        networkStore?.get(activeProfileId()) ?: ProfileNetworkSettings()

    /** Turns VPN on for the active profile, or removes its settings when it is already on. */
    fun toggleEnabled(): Boolean {
        val profileId = activeProfileId()
        val current = settingsForActiveProfile()
        if (current.vpnEnabled) {
            vpn.clearSettings(profileId)
            publishNetworkState(profileId)
            showNotice("VPN settings removed for this TV profile.")
            return true
        }
        if (current.configText.isBlank() || current.provider != VpnProvider.WIREGUARD) {
            showNotice("Paste a WireGuard config from the Windows Flint app first.")
            return false
        }
        // Enabling implies connecting with the browser. Without this, Enable left auto-connect off
        // and the tunnel idle, which reads as a switch that did nothing.
        return save(
            profileId,
            current.copy(
                vpnEnabled = true,
                provider = VpnProvider.WIREGUARD,
                autoConnectOnBrowserStart = true,
            ),
            failure = "Could not save VPN settings for this profile.",
        )
    }

    fun toggleAutoConnect(): Boolean {
        val current = settingsForActiveProfile()
        if (!current.isReady()) {
            showNotice("Enable VPN with a saved WireGuard config before auto-connect.")
            return false
        }
        return save(
            activeProfileId(),
            current.copy(autoConnectOnBrowserStart = !current.autoConnectOnBrowserStart),
            failure = "Could not update auto-connect.",
        )
    }

    fun toggleRequireBeforeBrowse(): Boolean {
        val current = settingsForActiveProfile()
        if (!current.isReady()) {
            showNotice("Enable VPN with a saved WireGuard config before requiring it.")
            return false
        }
        val next = current.copy(requireVpnBeforeBrowse = !current.requireVpnBeforeBrowse)
        if (!save(activeProfileId(), next, failure = "Could not update require-VPN.")) {
            return false
        }
        if (next.requireVpnBeforeBrowse) {
            showNotice("Browsing waits for VPN on this profile.")
        }
        return true
    }

    /**
     * Fail-closed gate: when the profile requires VPN, pages must not load until Connected.
     *
     * Every non-connected state returns false, including the ones that look like progress. A page
     * that loads "just this once" while the tunnel is still coming up is the whole failure this
     * setting exists to prevent.
     *
     * @return true when browsing is allowed right now.
     */
    fun allowBrowsing(): Boolean {
        if (!settingsForActiveProfile().requiresVpnBeforeBrowse) return true
        return when (val session = vpn.state.value) {
            BrowserVpnState.Connected -> true
            BrowserVpnState.Connecting,
            BrowserVpnState.TunnelUpUnverified,
            BrowserVpnState.NeedsConsent,
            -> {
                showNotice("Waiting for VPN before pages load.")
                false
            }
            is BrowserVpnState.Failed -> {
                showNotice("VPN required — pages stay blocked. ${session.message}")
                false
            }
            BrowserVpnState.Unavailable -> {
                showNotice("VPN required but unavailable on this TV — pages stay blocked.")
                false
            }
            BrowserVpnState.Idle -> {
                showNotice("VPN required — connect VPN before browsing.")
                false
            }
        }
    }

    /**
     * Drops stored settings for one profile without narrating it.
     *
     * The host-driven path already reports what it did; [clearForActiveProfile] is the TV-local
     * one that owes the viewer a notice.
     */
    fun clearSettings(profileId: String) = vpn.clearSettings(profileId)

    fun clearForActiveProfile() {
        val profileId = activeProfileId()
        vpn.clearSettings(profileId)
        publishNetworkState(profileId)
        showNotice("VPN settings cleared for this TV profile.")
    }

    /**
     * Connects for the active profile, launching the system consent prompt when needed.
     *
     * Soft-fails into [state]; never silently browses past a required consent. The request id and
     * profile are both re-checked inside the callback, because the person may have switched profile
     * or left the browser while the system dialog was up.
     */
    fun connectForActiveProfile() {
        val profileId = activeProfileId()
        val settings = settingsForActiveProfile()
        val capability = probe.probe()
        vpn.connectNow(profileId, settings, capability)
        if (vpn.state.value !is BrowserVpnState.NeedsConsent) {
            publishNetworkState(profileId)
            return
        }

        if (consentInFlight) return
        val host = consentHost
        if (host == null) {
            showNotice("VPN needs system permission — open Network again after granting it in Settings.")
            publishNetworkState(profileId)
            return
        }

        consentInFlight = true
        val requestId = ++consentRequestId
        scope.launch {
            if (requestId != consentRequestId) return@launch
            host.requestConsent { granted ->
                if (requestId != consentRequestId || activeProfileId() != profileId) {
                    return@requestConsent
                }
                consentInFlight = false
                if (granted) {
                    vpn.connectNow(profileId, settings, capability)
                } else {
                    vpn.onConsentDenied()
                }
                publishNetworkState(profileId)
            }
        }
    }

    /** Re-runs profile-gated auto-connect on browser enter or workspace open. */
    fun ensureForActiveProfile() {
        maybeAutoConnect()
        if (vpn.state.value is BrowserVpnState.NeedsConsent) {
            connectForActiveProfile()
        }
    }

    fun maybeAutoConnect() {
        val profileId = activeProfileId()
        vpn.onBrowserSessionStart(profileId, settingsForActiveProfile(), probe.probe())
    }

    /** Drops any consent callback still in flight; a profile or session change invalidates it. */
    fun invalidateConsentRequest() {
        consentRequestId += 1
        consentInFlight = false
    }

    fun endSession() = vpn.onBrowserSessionEnd()

    fun publishNetworkState(profileId: String = activeProfileId()) {
        val page = coordinator.snapshot()
        if (page.epoch == null || page.epoch <= 0L) {
            Log.i(TAG, "Browser network publish skipped — no live browser epoch yet")
            return
        }
        val settings = networkStore?.get(profileId) ?: ProfileNetworkSettings()
        runCatching {
            val ok = cockpitPublisher.publishNetwork(
                page = page,
                profileId = profileId,
                settings = settings,
                capability = probe.probe(),
                session = vpn.state.value,
            )
            if (ok) {
                Log.i(
                    TAG,
                    "Browser network published profile=$profileId enabled=${settings.vpnEnabled} " +
                        "configPresent=${settings.configText.isNotBlank()} session=${vpn.state.value}",
                )
            } else {
                Log.w(TAG, "Browser network publish returned false (no active host session?)")
            }
        }.onFailure { Log.w(TAG, "Browser network state rejected by wire rules", it) }
    }

    private fun save(profileId: String, next: ProfileNetworkSettings, failure: String): Boolean {
        if (networkStore?.put(profileId, next) != true) {
            showNotice(failure)
            return false
        }
        publishNetworkState(profileId)
        return true
    }

    /** Enabled, WireGuard, and carrying a config — the precondition every other toggle shares. */
    private fun ProfileNetworkSettings.isReady(): Boolean =
        vpnEnabled && provider == VpnProvider.WIREGUARD && configText.isNotBlank()

    private companion object {
        const val TAG = "FlintVpnController"
    }
}
