package com.rextechnologies.flint.protocol.media

/** A single observation of how well the receiver is keeping up. */
data class LinkSample(
    val receiverQueueDepth: Int,
    val decodeLatencyUs: Long,
    val roundTripTimeUs: Long,
    val droppedVideoFrames: Long,
    val pendingSendBytes: Long = 0,
) {
    init {
        require(receiverQueueDepth >= 0)
        require(decodeLatencyUs >= 0)
        require(roundTripTimeUs >= 0)
        require(droppedVideoFrames >= 0)
        require(pendingSendBytes >= 0)
    }
}

enum class LinkHealth {
    /** The receiver is keeping up; the encoder may probe upward. */
    Headroom,

    /** Steady state. */
    Stable,

    /** Falling behind; reduce bitrate. */
    Congested,
}

data class BitrateDecision(
    val bitrateBitsPerSecond: Int,
    val health: LinkHealth,
    val requestKeyFrame: Boolean,
)

/**
 * Chooses an encoder bitrate from receiver feedback.
 *
 * Congestion drops multiplicatively and recovery climbs additively, because an
 * over-large stream on a SoftAP link degrades every other hotspot client too:
 * backing off fast and returning slowly is the neighbourly failure mode.
 * Nothing here measures throughput directly; it reacts only to reported
 * queue depth, latency, loss, and unflushed socket bytes.
 */
class BitrateController(
    val minimumBitrate: Int = 1_000_000,
    val maximumBitrate: Int = 20_000_000,
    initialBitrate: Int = 8_000_000,
) {
    init {
        require(minimumBitrate in 1..maximumBitrate) { "Invalid bitrate bounds" }
        require(initialBitrate in minimumBitrate..maximumBitrate) { "Initial bitrate is out of bounds" }
    }

    var currentBitrate: Int = initialBitrate
        private set

    /**
     * The most the controller may climb to while something outside the link holds it down.
     *
     * Heat is the case. A thermal step-down is a fraction of the session's own maximum, and it must
     * bound the probe upward as well as cut the current rate, or four stable samples later the
     * controller climbs straight back into the heat it was stepped down from. Never above
     * [maximumBitrate]: a ceiling can only ever lower.
     */
    var ceiling: Int = maximumBitrate
        private set

    private var lastDroppedFrames: Long = 0
    private var stableSamples: Int = 0

    fun reset(bitrate: Int = currentBitrate) {
        currentBitrate = bitrate.coerceIn(minimumBitrate, ceiling)
        lastDroppedFrames = 0
        stableSamples = 0
    }

    /** Holds the rate at or below [bitsPerSecond] until [clearCeiling]. Returns the rate now in force. */
    fun applyCeiling(bitsPerSecond: Int): Int {
        ceiling = bitsPerSecond.coerceIn(minimumBitrate, maximumBitrate)
        if (currentBitrate > ceiling) currentBitrate = ceiling
        return currentBitrate
    }

    /** Lets the controller climb to the session maximum again. The current rate is left where it is. */
    fun clearCeiling() {
        ceiling = maximumBitrate
    }

    fun onSample(sample: LinkSample): BitrateDecision {
        // A receiver restart resets its counter; treat a decrease as a fresh baseline
        // rather than as a huge negative delta.
        val newDrops = (sample.droppedVideoFrames - lastDroppedFrames).coerceAtLeast(0)
        lastDroppedFrames = sample.droppedVideoFrames

        val health = classify(sample, newDrops)
        when (health) {
            LinkHealth.Congested -> {
                currentBitrate = (currentBitrate * CONGESTION_FACTOR).toInt()
                    .coerceAtLeast(minimumBitrate)
                stableSamples = 0
            }

            LinkHealth.Headroom -> {
                stableSamples++
                if (stableSamples >= SAMPLES_BEFORE_PROBE) {
                    currentBitrate = (currentBitrate + PROBE_STEP).coerceAtMost(ceiling)
                    stableSamples = 0
                }
            }

            LinkHealth.Stable -> stableSamples = 0
        }

        // Only severe loss justifies a key frame: each one is a bitrate spike, so
        // requesting them during mild congestion makes the congestion worse.
        return BitrateDecision(currentBitrate, health, requestKeyFrame = newDrops >= KEY_FRAME_DROP_THRESHOLD)
    }

    private fun classify(sample: LinkSample, newDrops: Long): LinkHealth = when {
        newDrops > 0 ||
            sample.receiverQueueDepth >= CONGESTED_QUEUE_DEPTH ||
            sample.decodeLatencyUs >= CONGESTED_DECODE_LATENCY_US ||
            sample.roundTripTimeUs >= CONGESTED_RTT_US ||
            sample.pendingSendBytes >= CONGESTED_PENDING_BYTES -> LinkHealth.Congested

        sample.receiverQueueDepth <= HEADROOM_QUEUE_DEPTH &&
            sample.roundTripTimeUs <= HEADROOM_RTT_US &&
            sample.pendingSendBytes == 0L -> LinkHealth.Headroom

        else -> LinkHealth.Stable
    }

    private companion object {
        const val CONGESTION_FACTOR = 0.7
        const val PROBE_STEP = 500_000
        const val SAMPLES_BEFORE_PROBE = 4
        const val KEY_FRAME_DROP_THRESHOLD = 3L
        const val CONGESTED_QUEUE_DEPTH = 4
        const val CONGESTED_DECODE_LATENCY_US = 120_000L
        const val CONGESTED_RTT_US = 150_000L
        const val CONGESTED_PENDING_BYTES = 512L * 1024
        const val HEADROOM_QUEUE_DEPTH = 1
        const val HEADROOM_RTT_US = 40_000L
    }
}
