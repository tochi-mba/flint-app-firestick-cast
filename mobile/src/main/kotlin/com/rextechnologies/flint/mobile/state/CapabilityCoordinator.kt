package com.rextechnologies.flint.mobile.state

import android.app.Activity
import android.content.Context
import com.rextechnologies.flint.castcore.capability.PhoneCapabilities
import com.rextechnologies.flint.castcore.capability.ProbeOutcome
import com.rextechnologies.flint.mobile.platform.PhoneProbes
import com.rextechnologies.flint.protocol.wire.CodecId
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.withContext

/** What this phone has told Flint about itself, and which of the questions are still outstanding. */
data class ProbedPhone(
    val encoders: Set<CodecId> = emptySet(),
    val encoderProbe: ProbeOutcome = ProbeOutcome.NOT_PROBED,
    val virtualDisplayProbe: ProbeOutcome = ProbeOutcome.NOT_PROBED,
    val encoderProbeRunning: Boolean = false,
    val secondScreenProbeRunning: Boolean = false,
) {
    /**
     * Whether any question has been put to this phone yet.
     *
     * The Cast page's empty state is "nothing has been asked", and it is a different screen from a
     * report full of Blocked cards. Without this the two were told apart by the report being null,
     * which stopped being true the moment the report became something derived rather than assigned.
     */
    val hasBeenAsked: Boolean
        get() = encoderProbe != ProbeOutcome.NOT_PROBED ||
            virtualDisplayProbe != ProbeOutcome.NOT_PROBED ||
            encoderProbeRunning ||
            secondScreenProbeRunning
}

/**
 * Asks the phone what it can do, and remembers the answers.
 *
 * Nothing here infers a capability from the model name or the API level. Until something has asked,
 * every answer is [ProbeOutcome.NOT_PROBED] — which is a third thing, not a polite "no".
 */
class CapabilityCoordinator(
    context: Context,
    private val deviceName: String,
    private val screenWidth: Int,
    private val screenHeight: Int,
    private val densityDpi: Int,
) {
    private val applicationContext = context.applicationContext

    private val mutable = MutableStateFlow(ProbedPhone())
    val state: StateFlow<ProbedPhone> = mutable

    /** Asks the platform what it can encode. */
    suspend fun probeEncoders(): Set<CodecId> {
        if (mutable.value.encoderProbeRunning) return mutable.value.encoders
        mutable.update { it.copy(encoderProbeRunning = true) }
        val found = withContext(Dispatchers.Default) { PhoneProbes.probeHardwareEncoders() }
        mutable.update {
            it.copy(
                encoders = found,
                encoderProbe = if (found.isEmpty()) ProbeOutcome.UNSUPPORTED else ProbeOutcome.SUPPORTED,
                encoderProbeRunning = false,
            )
        }
        return found
    }

    /**
     * The second-screen check.
     *
     * It needs an Activity, because a `Presentation` is a Dialog and a Dialog belongs to one. The
     * probe itself moves to the main thread and suspends there; it does not block it, which is the
     * difference between this answering and this always answering no.
     */
    suspend fun probeSecondScreen(activity: Activity): ProbeOutcome {
        if (mutable.value.secondScreenProbeRunning) return mutable.value.virtualDisplayProbe
        mutable.update { it.copy(secondScreenProbeRunning = true) }
        val outcome = PhoneProbes.probeVirtualDisplay(activity)
        mutable.update { it.copy(virtualDisplayProbe = outcome, secondScreenProbeRunning = false) }
        return outcome
    }

    /** Everything the assessor needs about this phone, from what has actually been asked. */
    fun capabilities(probed: ProbedPhone = mutable.value): PhoneCapabilities = PhoneProbes.capabilities(
        context = applicationContext,
        deviceName = deviceName,
        screenWidth = screenWidth,
        screenHeight = screenHeight,
        densityDpi = densityDpi,
        encoders = probed.encoders,
        encoderProbe = probed.encoderProbe,
        virtualDisplayProbe = probed.virtualDisplayProbe,
    )
}
