package com.rextechnologies.flint.castcore.media

/**
 * The phone's own account of how hot it is.
 *
 * The values mirror `PowerManager.THERMAL_STATUS_*` one for one, so the Android adapter is a cast and
 * nothing else. Nothing here infers temperature from frame timings: a dropped frame has a dozen
 * causes and heat is only one of them.
 */
enum class ThermalLevel {
    NONE,
    LIGHT,
    MODERATE,
    SEVERE,
    CRITICAL,
    EMERGENCY,
    SHUTDOWN,
}

/**
 * What to do about the heat, and what to say about it.
 *
 * @property bitrateCeiling the highest bitrate the controller may climb to while this level holds.
 * @property frameRateCeiling the highest frame rate to ask the encoder for.
 * @property shouldStop whether to end the session rather than keep going.
 * @property sentence what to show the person, or `null` while there is nothing worth saying.
 */
data class ThermalDecision(
    val bitrateCeiling: Int,
    val frameRateCeiling: Int,
    val shouldStop: Boolean,
    val sentence: String?,
) {
    init {
        require(bitrateCeiling > 0)
        require(frameRateCeiling in 1..120)
        require(sentence == null || sentence.isNotBlank())
    }
}

/**
 * Steps the session down as the phone heats up, and says so.
 *
 * The alternative is what a phone does on its own: the SoC throttles, the encoder misses its
 * deadline, frames arrive late, and the television stutters for reasons the person holding the phone
 * cannot see. Degrading deliberately is not better for the picture — it is better because it can be
 * explained, which is the whole difference between a product that is struggling and one that appears
 * broken.
 *
 * The ceilings are fractions of whatever the session's own maximum is rather than absolute numbers,
 * so a session that started conservatively is not told it may climb.
 */
object ThermalPolicy {
    fun decide(level: ThermalLevel, sessionMaximumBitrate: Int, sessionMaximumFrameRate: Int = 60): ThermalDecision {
        require(sessionMaximumBitrate > 0)
        require(sessionMaximumFrameRate in 1..120)

        fun ceiling(fraction: Double): Int =
            maxOf(MINIMUM_BITRATE, (sessionMaximumBitrate * fraction).toInt())

        return when (level) {
            ThermalLevel.NONE, ThermalLevel.LIGHT -> ThermalDecision(
                bitrateCeiling = sessionMaximumBitrate,
                frameRateCeiling = sessionMaximumFrameRate,
                shouldStop = false,
                sentence = null,
            )

            ThermalLevel.MODERATE -> ThermalDecision(
                bitrateCeiling = ceiling(0.7),
                frameRateCeiling = sessionMaximumFrameRate,
                shouldStop = false,
                sentence = "This phone is warm, so Flint has lowered the picture quality to keep the " +
                    "stream smooth.",
            )

            ThermalLevel.SEVERE -> ThermalDecision(
                bitrateCeiling = ceiling(0.45),
                frameRateCeiling = minOf(sessionMaximumFrameRate, 30),
                shouldStop = false,
                sentence = "This phone is hot. Flint has lowered both the picture quality and the " +
                    "frame rate; the stream will look softer until it cools down.",
            )

            ThermalLevel.CRITICAL -> ThermalDecision(
                bitrateCeiling = ceiling(0.3),
                frameRateCeiling = minOf(sessionMaximumFrameRate, 24),
                shouldStop = false,
                sentence = "This phone is close to throttling itself. Flint is sending the least it " +
                    "can and will stop if it gets any hotter.",
            )

            // At these levels Android is already shedding load on its own. Continuing would mean
            // competing with the platform for a device that has decided the answer, and the session
            // would stop anyway — just without having said why.
            ThermalLevel.EMERGENCY, ThermalLevel.SHUTDOWN -> ThermalDecision(
                bitrateCeiling = MINIMUM_BITRATE,
                frameRateCeiling = 1,
                shouldStop = true,
                sentence = "This phone is too hot to keep casting, so Flint has stopped. Let it cool " +
                    "down and start again.",
            )
        }
    }

    /** The floor the ceilings never go below, matching the bitrate controller's own minimum. */
    const val MINIMUM_BITRATE: Int = 1_000_000
}
