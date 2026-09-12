package com.rextechnologies.flint.mobile.platform

import android.content.Context
import android.os.Build
import android.os.PowerManager
import com.rextechnologies.flint.castcore.media.ThermalLevel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

/**
 * The phone's own account of how hot it is, as a flow of [ThermalLevel].
 *
 * Nothing here infers temperature from frame timings; a dropped frame has a dozen causes and heat is
 * only one of them. `PowerManager` has reported thermal status since API 29, and below that the level
 * stays at [ThermalLevel.NONE], which is honest: it is not "cool", it is "nobody has said".
 */
class ThermalWatch(context: Context) {
    private val power = context.applicationContext.getSystemService(PowerManager::class.java)

    private val mutable = MutableStateFlow(ThermalLevel.NONE)
    val level: StateFlow<ThermalLevel> = mutable

    private var listener: PowerManager.OnThermalStatusChangedListener? = null

    fun start() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) return
        val manager = power ?: return
        if (listener != null) return
        val registered = PowerManager.OnThermalStatusChangedListener { status ->
            mutable.value = fromStatus(status)
        }
        listener = registered
        // The current status first, so a session started on a phone that is already warm does not
        // wait for the next change to find out.
        mutable.value = fromStatus(manager.currentThermalStatus)
        runCatching { manager.addThermalStatusListener(registered) }
    }

    fun stop() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) return
        val registered = listener ?: return
        listener = null
        runCatching { power?.removeThermalStatusListener(registered) }
    }

    companion object {
        /** One for one with `PowerManager.THERMAL_STATUS_*`; an unknown value is treated as none. */
        fun fromStatus(status: Int): ThermalLevel = when (status) {
            PowerManager.THERMAL_STATUS_LIGHT -> ThermalLevel.LIGHT
            PowerManager.THERMAL_STATUS_MODERATE -> ThermalLevel.MODERATE
            PowerManager.THERMAL_STATUS_SEVERE -> ThermalLevel.SEVERE
            PowerManager.THERMAL_STATUS_CRITICAL -> ThermalLevel.CRITICAL
            PowerManager.THERMAL_STATUS_EMERGENCY -> ThermalLevel.EMERGENCY
            PowerManager.THERMAL_STATUS_SHUTDOWN -> ThermalLevel.SHUTDOWN
            else -> ThermalLevel.NONE
        }
    }
}
