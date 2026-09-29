package com.rextechnologies.flint.receiver.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.tv.material3.Text
import com.rextechnologies.flint.receiver.browser.BrowserVpnState
import com.rextechnologies.flint.receiver.browser.ProfileNetworkSettings
import com.rextechnologies.flint.receiver.browser.VpnCapability
import com.rextechnologies.flint.receiver.browser.VpnProvider

/**
 * Per-profile network / VPN settings (ADR-0022).
 *
 * Neutral when the Stick cannot prepare a VPN. Never presents an unavailable tunnel as connected.
 */
@Composable
internal fun ReceiverBrowserNetworkSheet(
    settings: ProfileNetworkSettings,
    capability: VpnCapability,
    vpnState: BrowserVpnState,
    onToggleEnabled: () -> Unit,
    onToggleAutoConnect: () -> Unit,
    onToggleRequireVpn: () -> Unit,
    onConnect: () -> Unit,
    onClear: () -> Unit,
    onClose: () -> Unit,
) {
    val firstFocus = remember { FocusRequester() }
    LaunchedEffect(Unit) { runCatching { firstFocus.requestFocus() } }

    val vpnAvailable = capability.preparable
    val hasConfig = settings.configText.isNotBlank() && settings.provider == VpnProvider.WIREGUARD
    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(ReceiverColors.Scrim)
            .testTag(ReceiverTags.BROWSER_NETWORK),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            modifier = Modifier
                .fillMaxWidth(ReceiverOverscan.CONTENT_FRACTION)
                .background(ReceiverColors.Panel, ReceiverShapes.Large)
                .border(ReceiverSpace.Hairline, ReceiverColors.Line, ReceiverShapes.Large)
                .padding(ReceiverSpace.Large),
            verticalArrangement = Arrangement.spacedBy(ReceiverSpace.Medium),
        ) {
            Text(text = "NETWORK", style = ReceiverType.Label, color = ReceiverColors.Muted)
            Text(
                text = if (vpnAvailable) {
                    "Optional VPN for this TV profile. Applies to every page in this workspace. Paste a WireGuard config from Windows (Browser → MORE → TV NETWORK / VPN). Full-tunnel 0.0.0.0/0 is fine — Flint keeps the local control network outside the tunnel."
                } else {
                    "VPN is not available on this device (${capability.reason})."
                },
                style = ReceiverType.Body,
                color = ReceiverColors.Muted,
            )
            Text(
                text = if (hasConfig) {
                    "WireGuard config saved on this TV (private key never shown here)."
                } else {
                    "No config on this TV yet — use Windows to paste one."
                },
                style = ReceiverType.Caption,
                color = ReceiverColors.Muted,
            )

            ReceiverVpnBanner(vpnState, requireVpn = settings.requireVpnBeforeBrowse)

            if (vpnAvailable) {
                Row(horizontalArrangement = Arrangement.spacedBy(ReceiverSpace.Small)) {
                    NetworkAction(
                        label = if (settings.vpnEnabled) "VPN on" else "VPN off",
                        detail = if (hasConfig) "WireGuard" else "Needs config",
                        onClick = onToggleEnabled,
                        modifier = Modifier
                            .weight(1f)
                            .focusRequester(firstFocus),
                    )
                    NetworkAction(
                        label = if (settings.autoConnectOnBrowserStart) "Auto-connect on" else "Auto-connect off",
                        detail = "When workspace opens",
                        onClick = onToggleAutoConnect,
                        modifier = Modifier.weight(1f),
                        enabled = settings.vpnEnabled && hasConfig,
                    )
                }
                NetworkAction(
                    label = if (settings.requireVpnBeforeBrowse) "Require VPN on" else "Require VPN off",
                    detail = "Block pages until VPN is connected",
                    onClick = onToggleRequireVpn,
                    enabled = settings.vpnEnabled && hasConfig,
                )
                NetworkAction(
                    label = when (vpnState) {
                        BrowserVpnState.NeedsConsent -> "Grant VPN permission"
                        BrowserVpnState.Connected -> "VPN connected"
                        BrowserVpnState.Connecting -> "Connecting…"
                        BrowserVpnState.TunnelUpUnverified -> "Verifying VPN…"
                        else -> "Connect now"
                    },
                    detail = "Shows the Android VPN prompt when needed",
                    onClick = onConnect,
                    enabled = hasConfig && settings.vpnEnabled &&
                        vpnState !is BrowserVpnState.Connecting &&
                        vpnState !is BrowserVpnState.TunnelUpUnverified,
                )
                NetworkAction(
                    label = "Remove VPN settings",
                    detail = "Clears config for this profile",
                    onClick = onClear,
                    destructive = true,
                )
            }

            NetworkAction(label = "Back", detail = "Browser tools", onClick = onClose)
        }
    }
}

@Composable
internal fun ReceiverVpnBanner(state: BrowserVpnState, requireVpn: Boolean = false) {
    val message = when (state) {
        BrowserVpnState.Idle -> return
        BrowserVpnState.NeedsConsent -> "VPN needs system permission — confirm on the next screen."
        BrowserVpnState.Connecting -> "Connecting VPN…"
        BrowserVpnState.TunnelUpUnverified ->
            "VPN tunnel is up; checking that traffic is actually protected…"
        BrowserVpnState.Connected -> "VPN connected for this browser session."
        is BrowserVpnState.Failed -> if (requireVpn) {
            "VPN did not connect — pages stay blocked until it does. ${state.message}"
        } else {
            "VPN did not connect — browsing uses the normal network. ${state.message}"
        }
        BrowserVpnState.Unavailable -> if (requireVpn) {
            "VPN unavailable on this TV — pages stay blocked."
        } else {
            "VPN unavailable on this TV."
        }
    }
    Text(
        text = message,
        style = ReceiverType.Caption,
        color = ReceiverColors.Signal,
        modifier = Modifier
            .fillMaxWidth()
            .testTag(ReceiverTags.BROWSER_VPN_BANNER)
            .semantics { contentDescription = "VPN status" },
    )
}

@Composable
private fun NetworkAction(
    label: String,
    detail: String,
    onClick: () -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    destructive: Boolean = false,
) {
    Column(
        modifier = modifier
            .tvClickable(enabled = enabled, shape = ReceiverShapes.Small, onClick = onClick)
            .padding(ReceiverSpace.Compact)
            .semantics { contentDescription = label },
    ) {
        Text(
            text = label.uppercase(),
            style = ReceiverType.Label,
            color = when {
                !enabled -> ReceiverColors.Line
                destructive -> ReceiverColors.Signal
                else -> ReceiverColors.Text
            },
        )
        Text(text = detail, style = ReceiverType.Caption, color = ReceiverColors.Muted)
    }
}
