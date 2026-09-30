package com.rextechnologies.flint.receiver.net

import android.content.Context
import android.net.wifi.WifiManager

/**
 * Keeps the Wi-Fi chip passing multicast up to the app.
 *
 * Without a [WifiManager.MulticastLock] the Wi-Fi stack filters out packets that are not addressed
 * to this device, so a Wi-Fi Fire TV never hears an mDNS query and a host can only find it by the
 * slower sweep. Every Fire TV Stick is Wi-Fi only. On a device with no Wi-Fi service there is
 * nothing to filter, and the hold does nothing.
 */
internal object WifiMulticast {
    /**
     * Takes the lock and returns the handle that releases it.
     *
     * Best effort: a platform that will not give the lock, because it is at its limit of Wi-Fi locks
     * or denies the permission, still gets a handle, and the advertisement is still sent. Only
     * hearing queries depends on the lock.
     */
    fun hold(context: Context, tag: String): AutoCloseable = runCatching {
        val wifi = context.applicationContext.getSystemService(WifiManager::class.java)
            ?: return AutoCloseable {}
        val lock = wifi.createMulticastLock(tag).apply { setReferenceCounted(false) }
        lock.acquire()
        AutoCloseable { if (lock.isHeld) lock.release() }
    }.getOrElse { AutoCloseable {} }
}
