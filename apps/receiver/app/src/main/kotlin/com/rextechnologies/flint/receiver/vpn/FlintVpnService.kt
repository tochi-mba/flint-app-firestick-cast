package com.rextechnologies.flint.receiver.vpn

/**
 * VPN service ownership for this app.
 *
 * The WireGuard Go userspace backend starts
 * [com.wireguard.android.backend.GoBackend.VpnService] by class name. That service is what must be
 * registered in the manifest — not a local stub that never calls [android.net.VpnService.Builder.establish].
 *
 * Consent still goes through [android.net.VpnService.prepare]; we never bypass the system prompt.
 */
object FlintVpnService {
    /** Manifest / Intent component: WireGuard's Go VpnService. */
    const val COMPONENT_NAME: String =
        "com.wireguard.android.backend.GoBackend\$VpnService"
}
