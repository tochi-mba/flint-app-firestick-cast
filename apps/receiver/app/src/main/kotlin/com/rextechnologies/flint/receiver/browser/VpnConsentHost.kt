package com.rextechnologies.flint.receiver.browser

/**
 * Activity-owned bridge for the system [android.net.VpnService.prepare] consent prompt.
 *
 * The Service never owns an Activity Result launcher. When consent is required, the coordinator
 * asks the host to launch the system Intent and reports the boolean grant back on the main thread.
 */
fun interface VpnConsentHost {
    /**
     * Shows the system VPN permission UI if needed, then invokes [onResult] with whether the user
     * granted permission. If prepare returns null (already granted), [onResult] is invoked with
     * true without showing UI.
     */
    fun requestConsent(onResult: (granted: Boolean) -> Unit)
}
