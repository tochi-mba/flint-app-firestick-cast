package com.rextechnologies.flint.castcore.media

import com.rextechnologies.flint.castcore.copy.ScreenCopy
import com.rextechnologies.flint.protocol.media.LinkHealth
import com.rextechnologies.flint.protocol.text.Decimal

/**
 * What the session is doing about the link and the heat, as numbers somebody can read out.
 *
 * Every value here is measured or decided; none is estimated. The one figure deliberately absent is
 * latency: nothing on a phone has measured one, and a number on a diagnostics row is read as one.
 *
 * @property targetBitrate what the encoder has been asked for, in bits per second.
 * @property bitrateCeiling the most the controller may climb to right now. Equal to
 *   [sessionMaximumBitrate] unless heat has lowered it.
 * @property sessionMaximumBitrate the most this session will ever ask for.
 * @property receiverQueueDepth frames waiting in the television's decoder at the last report.
 * @property pendingSendBytes bytes handed to the socket that the kernel had not yet taken.
 * @property droppedFramesDelta frames the television dropped since its previous report.
 * @property lastDecision the controller's last verdict, as a sentence fragment.
 * @property thermalLevel the phone's own account of how hot it is.
 */
data class SessionDiagnostics(
    val targetBitrate: Int,
    val bitrateCeiling: Int,
    val sessionMaximumBitrate: Int,
    val receiverQueueDepth: Int,
    val pendingSendBytes: Long,
    val droppedFramesDelta: Long,
    val lastDecision: String,
    val thermalLevel: ThermalLevel,
) {
    init {
        require(targetBitrate > 0 && bitrateCeiling > 0 && sessionMaximumBitrate > 0)
        require(bitrateCeiling <= sessionMaximumBitrate) { "A ceiling can only ever lower" }
        require(receiverQueueDepth >= 0 && pendingSendBytes >= 0 && droppedFramesDelta >= 0)
        require(lastDecision.isNotBlank())
    }

    /** Whether something other than the link is holding the rate down. */
    val ceilingApplied: Boolean
        get() = bitrateCeiling < sessionMaximumBitrate

    companion object {
        /** The diagnostics of a session that has just started and heard nothing yet. */
        fun initial(
            targetBitrate: Int,
            bitrateCeiling: Int,
            sessionMaximumBitrate: Int,
            thermalLevel: ThermalLevel,
        ): SessionDiagnostics =
            SessionDiagnostics(
                targetBitrate = targetBitrate,
                bitrateCeiling = bitrateCeiling,
                sessionMaximumBitrate = sessionMaximumBitrate,
                receiverQueueDepth = 0,
                pendingSendBytes = 0,
                droppedFramesDelta = 0,
                lastDecision = NOTHING_HEARD,
                thermalLevel = thermalLevel,
            )

        const val NOTHING_HEARD: String = "Nothing reported yet"

        /** "Congested: 5.6 Mbit/s", which is the decision and its consequence in one breath. */
        fun decisionLine(health: LinkHealth, bitrateBitsPerSecond: Int): String =
            "${ScreenCopy.linkHealthWord(health)}: ${megabits(bitrateBitsPerSecond)}"

        fun megabits(bitsPerSecond: Int): String =
            "${Decimal.oneDecimal(bitsPerSecond / BITS_PER_MEGABIT)} Mbit/s"

        /** Bytes for a row: whole kilobytes below a megabyte, one decimal of megabytes above. */
        fun bytes(count: Long): String = when {
            count < BYTES_PER_KIBIBYTE -> "$count B"
            count < BYTES_PER_MEBIBYTE -> "${count / BYTES_PER_KIBIBYTE} KiB"
            else -> "${Decimal.oneDecimal(count / BYTES_PER_MEBIBYTE.toDouble())} MiB"
        }

        /** The word a person sees for a thermal level, none of them a number. */
        fun thermalWord(level: ThermalLevel): String = when (level) {
            ThermalLevel.NONE -> "Normal"
            ThermalLevel.LIGHT -> "Slightly warm"
            ThermalLevel.MODERATE -> "Warm"
            ThermalLevel.SEVERE -> "Hot"
            ThermalLevel.CRITICAL -> "Very hot"
            ThermalLevel.EMERGENCY -> "Emergency"
            ThermalLevel.SHUTDOWN -> "Shutting down"
        }

        private const val BITS_PER_MEGABIT = 1_000_000.0
        private const val BYTES_PER_KIBIBYTE = 1_024L
        private const val BYTES_PER_MEBIBYTE = 1_024L * 1_024L
    }
}
