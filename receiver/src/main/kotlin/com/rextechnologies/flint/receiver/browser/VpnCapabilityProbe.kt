package com.rextechnologies.flint.receiver.browser

import android.content.Context
import android.net.VpnService
import android.os.Build

data class VpnCapability(
    val preparable: Boolean,
    val reason: String,
)

fun interface VpnCapabilityProbe {
    fun probe(): VpnCapability
}

/** Fixed probe for unit tests and deterministic UI paths. */
class FixedVpnCapabilityProbe(
    private val result: VpnCapability,
) : VpnCapabilityProbe {
    override fun probe(): VpnCapability = result
}

/**
 * Reports whether this process can ask the user for VPN consent via [VpnService.prepare].
 *
 * Honesty: Robolectric and a missing context cannot prepare a real VPN — those paths return
 * [VpnCapability.preparable] = false with a plain reason. A non-null prepare Intent means the
 * device supports VPN but still needs the RSA/system consent prompt; that remains preparable so
 * the coordinator can surface [BrowserVpnState.NeedsConsent] rather than claiming the feature works.
 */
class AndroidVpnCapabilityProbe(
    private val context: Context?,
) : VpnCapabilityProbe {
    override fun probe(): VpnCapability {
        if (context == null) {
            return VpnCapability(preparable = false, reason = "No Android context")
        }
        if (isRobolectricRuntime()) {
            return VpnCapability(preparable = false, reason = "VPN unavailable under Robolectric")
        }
        return try {
            Class.forName("android.net.VpnService")
            // Touch prepare so a broken stub surfaces as unavailable rather than a later crash.
            VpnService.prepare(context)
            VpnCapability(preparable = true, reason = "VpnService available")
        } catch (_: ClassNotFoundException) {
            VpnCapability(preparable = false, reason = "VpnService class not present")
        } catch (_: Throwable) {
            VpnCapability(preparable = false, reason = "VpnService probe failed")
        }
    }

    /**
     * True when the system still needs the user to approve VPN for this app.
     * Never treat a missing grant as something to route around.
     */
    fun needsConsent(): Boolean {
        val ctx = context ?: return false
        if (isRobolectricRuntime()) return false
        return try {
            VpnService.prepare(ctx) != null
        } catch (_: Throwable) {
            false
        }
    }

    private fun isRobolectricRuntime(): Boolean {
        val fingerprint = Build.FINGERPRINT ?: return false
        return fingerprint.contains("robolectric", ignoreCase = true) ||
            Build.PRODUCT.equals("robolectric", ignoreCase = true) ||
            Build.MODEL.equals("robolectric", ignoreCase = true)
    }
}
