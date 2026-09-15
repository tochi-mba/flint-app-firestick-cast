package com.rextechnologies.flint.mobile.platform

import android.content.Context
import android.os.Build
import com.rextechnologies.flint.castcore.capability.ProbeOutcome
import com.rextechnologies.flint.mobile.BuildConfig
import com.rextechnologies.flint.protocol.wire.CodecId

/** What was learned about this phone last time, so the same questions are not asked twice. */
data class StoredCapabilities(
    val encoders: Set<CodecId>,
    val encoderProbe: ProbeOutcome,
    val virtualDisplayProbe: ProbeOutcome,
    val encoderRoundTrip: ProbeOutcome,
    val roundTripDetail: String,
    val roundTripCodec: CodecId?,
)

/** Where the answers live between runs. An interface so the coordinator can be tested without one. */
interface CapabilityStore {
    fun load(): StoredCapabilities?

    fun save(value: StoredCapabilities)
}

/**
 * Keeps the phone's answers until the thing they were about changes.
 *
 * Whether this phone has a hardware encoder, and whether a frame survives it, is a fact about the
 * hardware and the system on it — it does not change between launches, and asking again on every
 * launch means encoding and decoding a test frame every time the app opens. It was not being kept
 * at all: the flag saying the introduction had been read was persisted while the capability answers
 * were not, so from the second launch onward every streaming mode was Blocked until somebody went
 * to Settings and ran the check by hand.
 *
 * The stamp is what keeps this honest. A new build of Flint may encode differently and a system
 * update certainly may, so an answer is only reused when both are the ones that produced it.
 * Anything else is discarded and the question is put again rather than guessed at.
 */
class PreferenceCapabilityStore(context: Context) : CapabilityStore {
    private val preferences =
        context.applicationContext.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    override fun load(): StoredCapabilities? {
        if (preferences.getString(KEY_STAMP, null) != stamp()) return null
        val encoders = preferences.getStringSet(KEY_ENCODERS, emptySet()).orEmpty()
            .mapNotNull(::codec)
            .toSet()
        return StoredCapabilities(
            encoders = encoders,
            encoderProbe = outcome(KEY_ENCODER_PROBE),
            virtualDisplayProbe = outcome(KEY_DISPLAY_PROBE),
            encoderRoundTrip = outcome(KEY_ROUND_TRIP),
            roundTripDetail = preferences.getString(KEY_ROUND_TRIP_DETAIL, "").orEmpty(),
            roundTripCodec = codec(preferences.getString(KEY_ROUND_TRIP_CODEC, null)),
        )
    }

    override fun save(value: StoredCapabilities) {
        preferences.edit()
            .putString(KEY_STAMP, stamp())
            .putStringSet(KEY_ENCODERS, value.encoders.map { it.value.toString() }.toSet())
            .putString(KEY_ENCODER_PROBE, value.encoderProbe.name)
            .putString(KEY_DISPLAY_PROBE, value.virtualDisplayProbe.name)
            .putString(KEY_ROUND_TRIP, value.encoderRoundTrip.name)
            .putString(KEY_ROUND_TRIP_DETAIL, value.roundTripDetail)
            .putString(KEY_ROUND_TRIP_CODEC, value.roundTripCodec?.value?.toString())
            .apply()
    }

    /**
     * A codec is a wire number rather than a name.
     *
     * [CodecId] is a value class over the number that goes on the wire, not an enum, and it rejects
     * anything outside an unsigned 16-bit non-zero range in its own constructor. Reading one back
     * therefore has to be able to fail: a preference file edited by hand, or written by a build
     * that numbered things differently, must come back as "nothing stored" rather than throw while
     * the app is starting.
     */
    private fun codec(stored: String?): CodecId? {
        val id = stored?.toIntOrNull() ?: return null
        return runCatching { CodecId(id) }.getOrNull()
    }

    private fun outcome(key: String): ProbeOutcome {
        val stored = preferences.getString(key, null) ?: return ProbeOutcome.NOT_PROBED
        return ProbeOutcome.entries.firstOrNull { it.name == stored } ?: ProbeOutcome.NOT_PROBED
    }

    /** This build of Flint, on this build of the system. Either changing retires every answer. */
    private fun stamp(): String = "${BuildConfig.VERSION_CODE}@${Build.FINGERPRINT}"

    private companion object {
        const val PREFERENCES_NAME = "flint-mobile-capabilities"
        const val KEY_STAMP = "stamp"
        const val KEY_ENCODERS = "encoders"
        const val KEY_ENCODER_PROBE = "encoder-probe"
        const val KEY_DISPLAY_PROBE = "display-probe"
        const val KEY_ROUND_TRIP = "round-trip"
        const val KEY_ROUND_TRIP_DETAIL = "round-trip-detail"
        const val KEY_ROUND_TRIP_CODEC = "round-trip-codec"
    }
}
