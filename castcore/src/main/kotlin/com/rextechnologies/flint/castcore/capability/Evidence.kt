package com.rextechnologies.flint.castcore.capability

import com.rextechnologies.flint.protocol.wire.CodecId

/**
 * What a probe has and has not established.
 *
 * The middle value is the one that matters. Treating "not probed yet" as "unsupported" would have
 * Flint tell someone their phone cannot do something nobody has asked it to do, and treating it as
 * "supported" would have Flint offer a control that fails when pressed. It is neither, and it says
 * so.
 */
enum class ProbeOutcome {
    NOT_PROBED,
    SUPPORTED,
    UNSUPPORTED,
}

/**
 * What the television is, as far as anything has actually been able to tell.
 *
 * A direct port of `Flint.Core.FireTvPlatform`, member for member, so the two codebases cannot come to
 * different conclusions about the same television. The ordering here is alphabetical-by-generation
 * rather than the C# ordinal order, because nothing is serialised from it.
 */
enum class ReceiverPlatform {
    /** Nothing has identified it yet. Deliberately not installable: an undetermined platform is a guess. */
    UNKNOWN,
    FIRE_OS_5,
    FIRE_OS_6,
    FIRE_OS_7,
    FIRE_OS_8,
    FIRE_OS_14,
    FIRE_OS_16,

    /**
     * Vega OS: not Android, and not a version of anything that will become Android.
     *
     * An APK cannot be installed on it by any method, now or later. This is the one device answer Flint
     * reports with no remedy attached.
     */
    VEGA,
    ;

    /** Upper-cased at the view layer, not here. */
    val displayLabel: String
        get() = when (this) {
            UNKNOWN -> "Unknown"
            FIRE_OS_5 -> "Fire OS 5"
            FIRE_OS_6 -> "Fire OS 6"
            FIRE_OS_7 -> "Fire OS 7"
            FIRE_OS_8 -> "Fire OS 8"
            FIRE_OS_14 -> "Fire OS 14"
            FIRE_OS_16 -> "Fire OS 16"
            VEGA -> "Vega OS"
        }
}

/** Whether this platform is Android underneath, which is the same question as whether an APK installs. */
fun ReceiverPlatform.isAndroidBased(): Boolean = when (this) {
    ReceiverPlatform.FIRE_OS_5,
    ReceiverPlatform.FIRE_OS_6,
    ReceiverPlatform.FIRE_OS_7,
    ReceiverPlatform.FIRE_OS_8,
    ReceiverPlatform.FIRE_OS_14,
    ReceiverPlatform.FIRE_OS_16,
    -> true

    ReceiverPlatform.UNKNOWN, ReceiverPlatform.VEGA -> false
}

/**
 * Whether a receiver package can be installed on this platform at all.
 *
 * [ReceiverPlatform.UNKNOWN] returning false is deliberate. An undetermined platform is not a
 * capability, and reporting one would be a guess.
 */
fun ReceiverPlatform.canInstallReceiver(): Boolean = isAndroidBased()

/** What happened the last time anything tried to reach the television's ADB port. */
enum class AdbConnectionState {
    /** Not attempted, or attempted and the result is no longer current. */
    NOT_PROBED,

    /**
     * Something answered and closed the port.
     *
     * Genuinely ambiguous: a Vega device and a Fire OS device with debugging switched off look identical
     * from here, so Flint reports both possibilities rather than picking the likelier one.
     */
    REFUSED,

    /** Connected, but the television has not accepted this phone's key yet. */
    UNAUTHORIZED,

    /** Connected and authorised. */
    CONNECTED,

    /** Nothing answered at all, which is almost always a wrong or stale address. */
    TIMED_OUT,
}

/** How a television came to Flint's attention. */
enum class DiscoverySource {
    /** The plaintext line probe swept across the hotspot subnet. */
    LINE_PROBE,

    /** A multicast DNS answer on `_rexcast._tcp.local`. */
    MULTICAST_DNS,

    /** A UDP broadcast answer. */
    BROADCAST,

    /** An SSDP `M-SEARCH` answer from a DLNA renderer, which is not a Flint receiver. */
    SSDP,

    /** Typed in by hand. */
    MANUAL,
}

/**
 * A television Flint has some evidence about.
 *
 * @property address the literal address the phone reached it on. Shown verbatim in failure copy,
 *   because "nothing answered" is only actionable when it says where.
 */
data class ReceiverDevice(
    val address: String,
    val friendlyName: String = "",
    val source: DiscoverySource = DiscoverySource.MANUAL,
    val port: Int = DEFAULT_RECEIVER_PORT,
    val platform: ReceiverPlatform = ReceiverPlatform.UNKNOWN,
    val adbState: AdbConnectionState = AdbConnectionState.NOT_PROBED,
    val model: String = "",
    val androidRelease: String = "",
    /** The Android API level ADB reported, or 0 when nothing has asked. */
    val androidApiLevel: Int = 0,
    /**
     * Whether a Flint receiver answered its own port.
     *
     * Stronger evidence than anything ADB can report: it proves the receiver is installed, running and
     * willing to talk, which is the only thing the capability verdicts actually need to know.
     */
    val receiverAnswered: Boolean = false,
) {
    init {
        require(address.isNotBlank()) { "A device without an address is not a device" }
        require(port in 1..65_535)
        require(androidApiLevel >= 0)
    }

    /** An unauthorised device counts as reachable: something is there and it answered. */
    val isReachable: Boolean
        get() = adbState == AdbConnectionState.CONNECTED || adbState == AdbConnectionState.UNAUTHORIZED

    /** What to call it on screen when it has not offered a name of its own. */
    val displayName: String
        get() = friendlyName.ifBlank { model.ifBlank { address } }

    companion object {
        /** The receiver's TCP port, which carries both the cast protocol and the plaintext line probe. */
        const val DEFAULT_RECEIVER_PORT: Int = 47_855
    }
}

/**
 * What this phone can actually do, as established by probing it.
 *
 * Nothing here is inferred from the model name or the API level alone. An API level says a method
 * exists, not that this vendor's implementation of it works, and this project has already shipped
 * one encoder that satisfied every structural check while emitting frames that decoded to nothing.
 */
data class PhoneCapabilities(
    val apiLevel: Int,
    val deviceName: String,
    val screenWidth: Int,
    val screenHeight: Int,
    val densityDpi: Int,
    /** Hardware video encoders the platform reported, empty until [encoderProbe] has run. */
    val hardwareVideoEncoders: Set<CodecId> = emptySet(),
    val encoderProbe: ProbeOutcome = ProbeOutcome.NOT_PROBED,
    /**
     * Whether this phone will let Flint create the private display the second screen draws into.
     *
     * A `VirtualDisplay` this app owns and renders into needs no permission, unlike a public one,
     * and that is the whole reason the second screen is the lower-friction mode. It is still probed
     * rather than assumed: the guarantee is in the platform documentation, not in any particular
     * vendor's build of it.
     */
    val virtualDisplayProbe: ProbeOutcome = ProbeOutcome.NOT_PROBED,
    /** Whether a MediaProjection consent flow is available to ask for at all. */
    val screenCaptureConsentAvailable: Boolean = false,
    /** Playback capture arrived in API 29 and only ever captures apps that allow it. */
    val audioPlaybackCaptureSupported: Boolean = false,
) {
    init {
        require(apiLevel > 0)
        require(deviceName.isNotBlank())
        require(screenWidth > 0 && screenHeight > 0)
        require(densityDpi > 0)
        require(encoderProbe != ProbeOutcome.SUPPORTED || hardwareVideoEncoders.isNotEmpty()) {
            "A probe that found no encoder has not found support"
        }
    }

    /** The codecs to advertise in HELLO, which is the intersection of what the hardware reported. */
    val advertisedCodecs: Set<CodecId>
        get() = hardwareVideoEncoders
}

/**
 * A measured view of the network path to a receiver.
 *
 * Every value is measured. Nothing here may be estimated from link speed, radio band or any other
 * proxy. Throughput is separable from latency because latency can be measured against any open port
 * while throughput needs a receiver willing to sink traffic, so the two become knowable at different
 * moments and have to be reported independently.
 */
data class NetworkPath(
    val roundTripMs: Double,
    val jitterMs: Double,
    val throughputMbps: Double,
    val packetLossPercent: Double,
    val throughputMeasured: Boolean = true,
) {
    init {
        require(roundTripMs >= 0 && roundTripMs.isFinite())
        require(jitterMs >= 0 && jitterMs.isFinite())
        require(throughputMbps >= 0 && throughputMbps.isFinite())
        require(packetLossPercent in 0.0..100.0)
    }

    val latencySupportsMirroring: Boolean
        get() = roundTripMs <= USABLE_ROUND_TRIP_CEILING_MS

    /** False when throughput was never measured: an unknown capacity is not a failing one. */
    val throughputTooLowForMirroring: Boolean
        get() = throughputMeasured && throughputMbps < MINIMUM_MIRROR_THROUGHPUT_MBPS

    /** Throughput for display, or a plain statement that nobody has measured it. */
    val throughputLabel: String
        get() = if (throughputMeasured) formatMbps(throughputMbps) else "Not measured"

    companion object {
        /**
         * The lowest throughput that supports a watchable 1080p60 mirror with error-correction
         * overhead: 15 Mbit/s of video plus roughly 20%, rounded up to leave the link some headroom.
         */
        const val MINIMUM_MIRROR_THROUGHPUT_MBPS: Double = 20.0

        /** The round trip above which a mirror stops feeling attached to the hand holding the phone. */
        const val USABLE_ROUND_TRIP_CEILING_MS: Double = 30.0

        /** Below this even direct play stutters, because nothing is being re-encoded to fit. */
        const val MINIMUM_MEDIA_HANDOFF_MBPS: Double = 8.0

        /**
         * One decimal place, without a locale.
         *
         * `String.format` would put a comma in for half the world and then the number would not
         * match the one in the same sentence a line above it.
         */
        internal fun formatMbps(value: Double): String = "${oneDecimal(value)} Mbit/s"

        internal fun oneDecimal(value: Double): String {
            val scaled = kotlin.math.round(value * 10.0).toLong()
            return "${scaled / 10}.${scaled % 10}"
        }

        internal fun wholeNumber(value: Double): String = kotlin.math.round(value).toLong().toString()
    }
}
