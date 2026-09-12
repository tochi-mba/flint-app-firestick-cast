package com.rextechnologies.flint.mobile.state

import android.app.Activity
import android.content.Context
import android.os.Build
import com.rextechnologies.flint.castcore.capability.PhoneCapabilities
import com.rextechnologies.flint.castcore.capability.ProbeOutcome
import com.rextechnologies.flint.castcore.media.CodecChoice
import com.rextechnologies.flint.mobile.platform.EncoderRoundTrip
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
    /** Whether a test frame drawn through the encoder came back from a decoder as itself. */
    val encoderRoundTrip: ProbeOutcome = ProbeOutcome.NOT_PROBED,
    val roundTripDetail: String = "",
    /** The codec the test frame went through, so the result can say so. */
    val roundTripCodec: CodecId? = null,
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

/** What one encoder check found, for the sentence the banner shows. */
data class EncoderCheck(
    val encoders: Set<CodecId>,
    val through: CodecId?,
    val roundTrip: ProbeOutcome,
    val detail: String,
)

/**
 * Asks the phone what it can do, and remembers the answers.
 *
 * Nothing here infers a capability from the model name or the API level. Until something has asked,
 * every answer is [ProbeOutcome.NOT_PROBED] — which is a third thing, not a polite "no".
 *
 * The two platform calls are injected so the coordinator's own decisions -- what runs after what,
 * what is skipped and why, what the state says while a check is running -- can be tested without a
 * codec or a display in the room.
 */
class CapabilityCoordinator(
    context: Context,
    private val deviceName: String,
    private val screenWidth: Int,
    private val screenHeight: Int,
    private val densityDpi: Int,
    private val enumerateEncoders: () -> Set<CodecId> = PhoneProbes::probeHardwareEncoders,
    private val roundTrip: suspend (Activity, CodecId) -> EncoderRoundTrip.Outcome = { activity, codec ->
        EncoderRoundTrip.run(activity, codec, Build.VERSION.SDK_INT)
    },
    private val probeDisplay: suspend (Activity) -> ProbeOutcome = PhoneProbes::probeVirtualDisplay,
) {
    private val applicationContext = context.applicationContext

    private val mutable = MutableStateFlow(ProbedPhone())
    val state: StateFlow<ProbedPhone> = mutable

    /**
     * The encoder check: what the platform lists, then whether a frame survives the one a session
     * would use.
     *
     * The round trip needs an Activity because it draws into a `Presentation`, and a Presentation is
     * a Dialog. It is skipped, and said to be skipped, on a phone that has already refused a private
     * display -- the same display it would draw into -- because a failure to draw is not a failure
     * to encode, and a mirror through a projection may still work.
     */
    suspend fun probeEncoders(activity: Activity): EncoderCheck {
        val before = mutable.value
        if (before.encoderProbeRunning) {
            return EncoderCheck(before.encoders, before.roundTripCodec, before.encoderRoundTrip, before.roundTripDetail)
        }
        mutable.update { it.copy(encoderProbeRunning = true) }

        val found = withContext(Dispatchers.Default) { enumerateEncoders() }
        val codec = CodecChoice.preferred(found)
        val trip = when {
            codec == null -> EncoderRoundTrip.Outcome(ProbeOutcome.NOT_PROBED, "")
            before.virtualDisplayProbe == ProbeOutcome.UNSUPPORTED -> EncoderRoundTrip.Outcome(
                ProbeOutcome.NOT_PROBED,
                DISPLAY_REFUSED_DETAIL,
            )

            else -> roundTrip(activity, codec)
        }

        mutable.update {
            it.copy(
                encoders = found,
                encoderProbe = if (found.isEmpty()) ProbeOutcome.UNSUPPORTED else ProbeOutcome.SUPPORTED,
                encoderRoundTrip = trip.outcome,
                roundTripDetail = trip.detail,
                roundTripCodec = codec,
                encoderProbeRunning = false,
            )
        }
        return EncoderCheck(found, codec, trip.outcome, trip.detail)
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
        val outcome = probeDisplay(activity)
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
        encoderRoundTrip = probed.encoderRoundTrip,
        roundTripDetail = probed.roundTripDetail,
    )

    companion object {
        const val DISPLAY_REFUSED_DETAIL: String =
            "This phone refused the display the check draws into, so the encoder's output could " +
                "not be checked."
    }
}
