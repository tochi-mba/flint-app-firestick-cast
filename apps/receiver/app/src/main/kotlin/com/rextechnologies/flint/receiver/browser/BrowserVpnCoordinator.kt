package com.rextechnologies.flint.receiver.browser

import android.content.Context
import android.net.VpnService
import android.util.Log
import com.rextechnologies.flint.receiver.vpn.WireGuardConfigValidator
import com.wireguard.android.backend.BackendException
import com.wireguard.android.backend.GoBackend
import com.wireguard.android.backend.Tunnel
import com.wireguard.config.BadConfigException
import com.wireguard.config.Config
import java.io.ByteArrayInputStream
import java.nio.charset.StandardCharsets
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/** Soft-fail VPN session states for a UI banner. Never throw across this boundary. */
sealed class BrowserVpnState {
    data object Idle : BrowserVpnState()
    data object NeedsConsent : BrowserVpnState()
    data object Connecting : BrowserVpnState()
    data object TunnelUpUnverified : BrowserVpnState()
    data object Connected : BrowserVpnState()
    data class Failed(val message: String) : BrowserVpnState()
    data object Unavailable : BrowserVpnState()
}

interface BrowserVpnTunnel {
    fun connect(configText: String): Result<Unit>
    fun disconnect()
}

/** Stand-in when no Android context (or tests) should touch the real tunnel. */
class NoOpBrowserVpnTunnel : BrowserVpnTunnel {
    override fun connect(configText: String): Result<Unit> =
        Result.failure(IllegalStateException("VPN backend not installed"))

    override fun disconnect() = Unit
}

/**
 * WireGuard tunnel via the official `com.wireguard.android:tunnel` Go backend.
 *
 * Never logs [configText]. Soft-fails on parse/consent/backend errors. Does not bypass
 * [VpnService.prepare] — the coordinator must surface NeedsConsent first.
 */
class BrowserVpnTunnelImpl(
    context: Context,
    backendFactory: (Context) -> GoBackend = { appContext -> GoBackend(appContext) },
    private val needsSystemConsent: () -> Boolean = {
        VpnService.prepare(context.applicationContext) != null
    },
    private val validateRoutes: (Config) -> String? = { config ->
        BrowserVpnRoutePolicy.validate(
            config = config,
            localAddresses = BrowserVpnRoutePolicy.localAddresses(),
            packageName = context.applicationContext.packageName,
        )
    },
) : BrowserVpnTunnel {
    private val appContext = context.applicationContext
    private val backend: GoBackend by lazy { backendFactory(appContext) }
    private val tunnel = object : Tunnel {
        override fun getName(): String = TUNNEL_NAME

        override fun onStateChange(newState: Tunnel.State) {
            Log.i(TAG, "tunnel state=$newState")
        }
    }

    @Volatile
    private var up: Boolean = false

    override fun connect(configText: String): Result<Unit> {
        if (!WireGuardConfigValidator.isValid(configText)) {
            return Result.failure(IllegalArgumentException("WireGuard config is incomplete"))
        }
        return try {
            if (needsSystemConsent()) {
                return Result.failure(IllegalStateException("VPN permission not granted"))
            }
            val config = Config.parse(
                ByteArrayInputStream(configText.toByteArray(StandardCharsets.UTF_8)),
            )
            validateRoutes(config)?.let { reason ->
                return Result.failure(IllegalArgumentException(reason))
            }
            val prepared = BrowserVpnRoutePolicy.withLocalRoutesCarvedOut(config)
            backend.setState(tunnel, Tunnel.State.UP, prepared)
            up = true
            Result.success(Unit)
        } catch (error: BadConfigException) {
            Result.failure(IllegalArgumentException("WireGuard config is invalid"))
        } catch (error: BackendException) {
            Result.failure(IllegalStateException(backendMessage(error)))
        } catch (error: Throwable) {
            Result.failure(
                IllegalStateException("VPN connect failed"),
            )
        }
    }

    override fun disconnect() {
        if (!up) {
            return
        }
        try {
            backend.setState(tunnel, Tunnel.State.DOWN, null)
        } catch (error: Throwable) {
            Log.w(TAG, "VPN disconnect failed")
        } finally {
            up = false
        }
    }

    private fun backendMessage(error: BackendException): String =
        when (error.reason) {
            BackendException.Reason.VPN_NOT_AUTHORIZED -> "VPN permission not granted"
            BackendException.Reason.DNS_RESOLUTION_FAILURE -> "VPN endpoint DNS failed"
            BackendException.Reason.UNABLE_TO_START_VPN -> "Unable to start VPN service"
            BackendException.Reason.TUN_CREATION_ERROR -> "Unable to create VPN interface"
            BackendException.Reason.GO_ACTIVATION_ERROR_CODE -> "WireGuard engine failed to start"
            BackendException.Reason.TUNNEL_MISSING_CONFIG -> "WireGuard config is incomplete"
            else -> "VPN connect failed"
        }

    companion object {
        private const val TAG = "FlintVpnTunnel"
        /** Tunnel.NAME_MAX_LENGTH is 15; keep short and alphanumeric. */
        private const val TUNNEL_NAME = "flint"
    }
}

/**
 * Profile-gated VPN coordinator for browser sessions.
 *
 * Soft-fails into banner states. Honours the system VPN consent prompt — never bypasses it.
 * [clearSettings] is the disable path: drop stored auto-connect prefs for a profile.
 */
class BrowserVpnCoordinator(
    private val tunnel: BrowserVpnTunnel = NoOpBrowserVpnTunnel(),
    private val networkStore: BrowserNetworkStore? = null,
    private val needsConsent: () -> Boolean = { false },
    private val verifier: BrowserVpnConnectionVerifier = UnavailableBrowserVpnConnectionVerifier,
) {
    private val _state = MutableStateFlow<BrowserVpnState>(BrowserVpnState.Idle)
    val state: StateFlow<BrowserVpnState> = _state.asStateFlow()

    @Volatile
    private var activeProfileId: String? = null
    private var activeSettings: ProfileNetworkSettings? = null
    @Volatile
    private var connectionAttemptId = 0L

    fun onBrowserSessionStart(
        profileId: String,
        settings: ProfileNetworkSettings,
        capability: VpnCapability,
    ) {
        if (activeProfileId == profileId && activeSettings == settings &&
            _state.value in setOf(
                BrowserVpnState.NeedsConsent,
                BrowserVpnState.Connecting,
                BrowserVpnState.TunnelUpUnverified,
                BrowserVpnState.Connected,
            )
        ) return
        onBrowserSessionEnd()
        try {
            if (!settings.isConfiguredForAutoConnect) {
                _state.value = BrowserVpnState.Idle
                return
            }
            val validated = settings.validated()
            if (validated == null) {
                _state.value = BrowserVpnState.Failed("VPN settings are incomplete")
                return
            }
            if (!capability.preparable) {
                _state.value = BrowserVpnState.Unavailable
                return
            }
            if (needsConsent()) {
                activeProfileId = profileId
                activeSettings = settings
                _state.value = BrowserVpnState.NeedsConsent
                return
            }

            connectValidated(profileId, settings, validated.configText)
        } catch (error: Throwable) {
            _state.value = BrowserVpnState.Failed("VPN connect failed")
        }
    }

    fun onBrowserSessionEnd() {
        connectionAttemptId += 1
        verifier.cancel()
        verifier.stopWatching()
        try {
            val previous = _state.value
            if (activeProfileId != null || previous is BrowserVpnState.Connected ||
                previous is BrowserVpnState.Connecting ||
                previous is BrowserVpnState.TunnelUpUnverified ||
                previous is BrowserVpnState.NeedsConsent
            ) {
                tunnel.disconnect()
            }
        } catch (_: Throwable) {
            // Soft-fail: session end must not throw into the browser host.
        } finally {
            activeProfileId = null
            activeSettings = null
            _state.value = BrowserVpnState.Idle
        }
    }

    /** Disable path: remove stored network settings for [profileId] and disconnect if active. */
    fun clearSettings(profileId: String) {
        try {
            networkStore?.remove(profileId)
            if (activeProfileId == profileId) {
                connectionAttemptId += 1
                verifier.cancel()
        verifier.stopWatching()
                tunnel.disconnect()
                activeProfileId = null
                activeSettings = null
                _state.value = BrowserVpnState.Idle
            }
        } catch (_: Throwable) {
            _state.value = BrowserVpnState.Failed("Could not clear VPN settings")
        }
    }

    /**
     * Explicit Connect-now / retry path after the user grants system consent.
     *
     * Does not require [ProfileNetworkSettings.autoConnectOnBrowserStart]; the user asked to
     * connect. Still soft-fails and never logs [ProfileNetworkSettings.configText].
     */
    fun connectNow(
        profileId: String,
        settings: ProfileNetworkSettings,
        capability: VpnCapability,
    ) {
        onBrowserSessionEnd()
        try {
            val validated = settings.validated()
            if (validated == null || !validated.vpnEnabled) {
                _state.value = BrowserVpnState.Failed("VPN settings are incomplete")
                return
            }
            if (!capability.preparable) {
                _state.value = BrowserVpnState.Unavailable
                return
            }
            if (needsConsent()) {
                activeProfileId = profileId
                activeSettings = settings
                _state.value = BrowserVpnState.NeedsConsent
                return
            }
            connectValidated(profileId, settings, validated.configText)
        } catch (error: Throwable) {
            _state.value = BrowserVpnState.Failed("VPN connect failed")
        }
    }

    /** User denied or cancelled the system VPN prompt. */
    fun onConsentDenied() {
        connectionAttemptId += 1
        verifier.cancel()
        verifier.stopWatching()
        activeProfileId = null
        activeSettings = null
        _state.value = BrowserVpnState.Failed("VPN permission was denied on this TV")
    }

    /**
     * Handles a verified tunnel that stopped being one.
     *
     * Drops out of Connected and tears the tunnel down. Browsing is gated on Connected, so this is
     * what re-blocks pages on a profile that requires VPN — the alternative is Flint continuing to
     * say traffic is protected when it is not.
     */
    private fun onTunnelLost(attemptId: Long, profileId: String) {
        if (attemptId != connectionAttemptId || activeProfileId != profileId) return
        if (_state.value != BrowserVpnState.Connected) return
        Log.w(TAG, "VPN connection lost after verification")
        runCatching { tunnel.disconnect() }
            .onFailure { Log.w(TAG, "Disconnect after lost VPN failed") }
        activeProfileId = null
        activeSettings = null
        _state.value = BrowserVpnState.Failed("VPN connection was lost")
    }

    private fun connectValidated(
        profileId: String,
        settings: ProfileNetworkSettings,
        configText: String,
    ) {
        connectionAttemptId += 1
        val attemptId = connectionAttemptId
        verifier.cancel()
        verifier.stopWatching()
        activeProfileId = profileId
        activeSettings = settings
        _state.value = BrowserVpnState.Connecting
        val result = tunnel.connect(configText)
        if (result.isFailure) {
            _state.value = BrowserVpnState.Failed("VPN connect failed")
            return
        }

        _state.value = BrowserVpnState.TunnelUpUnverified
        verifier.verify { verification ->
            if (attemptId != connectionAttemptId || activeProfileId != profileId) return@verify
            if (verification.isSuccess) {
                _state.value = BrowserVpnState.Connected
                // A single check proves the tunnel existed once. Keep watching, or a Wi-Fi change
                // or a dead endpoint leaves Connected latched while required-VPN browsing keeps
                // loading pages on the strength of a tunnel that is gone.
                verifier.watch { onTunnelLost(attemptId, profileId) }
            } else {
                runCatching { tunnel.disconnect() }
                .onFailure { Log.w(TAG, "Unverified VPN disconnect failed") }
                activeProfileId = null
                activeSettings = null
                _state.value = BrowserVpnState.Failed("VPN route could not be verified")
            }
        }
    }

    private companion object {
        const val TAG = "FlintVpnCoordinator"
    }
}
