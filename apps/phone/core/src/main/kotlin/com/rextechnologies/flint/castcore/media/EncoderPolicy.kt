package com.rextechnologies.flint.castcore.media

import com.rextechnologies.flint.protocol.wire.CodecId

/** How the encoder is told to produce intra frames. */
sealed interface KeyFrameStrategy {
    /**
     * An intra frame only when the receiver asks for one.
     *
     * The preferred setting, because every key frame is a bitrate spike and an unrequested one buys
     * nothing on a link that is already carrying the stream.
     */
    data object OnDemand : KeyFrameStrategy

    /**
     * A bounded interval, used only where an on-demand request has been proven unreliable.
     *
     * The documented exception. An encoder that ignores `PARAMETER_KEY_REQUEST_SYNC_FRAME` combined
     * with an unbounded group of pictures means a receiver that joins late or drops a packet never
     * gets a frame it can start from — a frozen picture, indefinitely. The Windows hardware path
     * already carries this fallback for the same reason.
     */
    data class BoundedInterval(val seconds: Int) : KeyFrameStrategy {
        init {
            require(seconds in 1..10) { "A fallback interval outside a few seconds is not a fallback" }
        }
    }
}

/**
 * Everything the platform encoder needs, decided without touching the platform.
 *
 * @property maxBFrames always zero. A B-frame cannot be emitted until a later frame has been encoded,
 *   which is latency by construction, so this is a constant rather than an option.
 */
data class EncoderConfig(
    val codec: CodecId,
    val width: Int,
    val height: Int,
    val frameRate: Int,
    val bitrateBitsPerSecond: Int,
    val keyFrameStrategy: KeyFrameStrategy,
    val lowLatencyRequested: Boolean,
    val maxBFrames: Int = 0,
) {
    init {
        require(width > 0 && height > 0)
        require(width % 2 == 0 && height % 2 == 0) { "Encoders want even dimensions" }
        require(frameRate in 1..120)
        require(bitrateBitsPerSecond > 0)
        require(maxBFrames == 0) { "No B-frames, ever" }
    }
}

/** Turns a negotiated session and a probed phone into an encoder configuration. */
object EncoderPolicy {
    /**
     * The longest edge Flint will encode.
     *
     * The Windows host caps the mirror at the same number. Above it the bitrate needed to keep the
     * picture clean stops fitting on a shared SoftAP link, and the first thing that degrades is every
     * other device on the hotspot rather than the cast.
     */
    const val MAXIMUM_LONG_EDGE: Int = 1920

    const val FRAME_RATE: Int = 60

    /** API 30 is where `FEATURE_LowLatency` and `KEY_LOW_LATENCY` exist to be asked about. */
    const val LOW_LATENCY_MINIMUM_API: Int = 30

    fun forSession(
        codec: CodecId,
        sourceWidth: Int,
        sourceHeight: Int,
        bitrateBitsPerSecond: Int,
        apiLevel: Int,
        keyFrameStrategy: KeyFrameStrategy = KeyFrameStrategy.OnDemand,
        frameRate: Int = FRAME_RATE,
    ): EncoderConfig {
        val (width, height) = scaleToFit(sourceWidth, sourceHeight, MAXIMUM_LONG_EDGE)
        return EncoderConfig(
            codec = codec,
            width = width,
            height = height,
            frameRate = frameRate,
            bitrateBitsPerSecond = bitrateBitsPerSecond,
            keyFrameStrategy = keyFrameStrategy,
            lowLatencyRequested = apiLevel >= LOW_LATENCY_MINIMUM_API,
        )
    }

    /**
     * Fits a source into the cap, keeping the aspect ratio and landing on even numbers.
     *
     * Odd dimensions are rejected outright by some encoders and quietly rounded by others, and the
     * quiet rounding is the dangerous one: the receiver is told one size and shown another.
     */
    fun scaleToFit(sourceWidth: Int, sourceHeight: Int, maximumLongEdge: Int): Pair<Int, Int> {
        require(sourceWidth > 0 && sourceHeight > 0)
        require(maximumLongEdge >= 2)
        val longEdge = maxOf(sourceWidth, sourceHeight)
        if (longEdge <= maximumLongEdge) return even(sourceWidth) to even(sourceHeight)
        val scale = maximumLongEdge.toDouble() / longEdge
        return even((sourceWidth * scale).toInt()) to even((sourceHeight * scale).toInt())
    }

    private fun even(value: Int): Int = maxOf(2, value - (value % 2))
}

/**
 * Watches whether this device's encoder actually honours a key-frame request.
 *
 * The rule in AGENTS.md is "prefer on demand; fall back to a bounded interval only where the request
 * is proven unreliable, and say so in the code where it is done". This is where it is done. The proof
 * is observational rather than a device list: a request goes out, and if no intra frame appears
 * within a bounded number of frames the strategy changes for the rest of the session and does not
 * change back, because an encoder that ignored one request will ignore the next.
 */
class KeyFrameGovernor(
    private val framesBeforeFallback: Int = FRAMES_BEFORE_FALLBACK,
    private val fallbackSeconds: Int = FALLBACK_INTERVAL_SECONDS,
) {
    init {
        require(framesBeforeFallback in 1..600)
        // Checked here rather than left to BoundedInterval's own require, which would not run until
        // the moment the fallback engages -- mid-cast, on the codec callback thread, from a
        // constructor argument that was already wrong when the session started.
        require(fallbackSeconds in 1..10) { "A fallback interval outside a few seconds is not a fallback" }
    }

    /**
     * The lock.
     *
     * The two entry points run on different threads and always have: a sync-frame request arrives on
     * the control-socket reader, and every emitted frame arrives on the codec's callback thread.
     * Both read and write the same two fields, and `strategy` is additionally read by whoever is
     * configuring the encoder. Neither field is a safe unsynchronised `var`.
     */
    private val lock = Any()

    var strategy: KeyFrameStrategy = KeyFrameStrategy.OnDemand
        get() = synchronized(lock) { field }
        private set(value) = synchronized(lock) { field = value }

    private var framesSinceRequest: Int = NO_REQUEST_OUTSTANDING

    /** Call when a sync frame has been asked for, whether by the receiver or after a reconfigure. */
    fun onSyncFrameRequested() = synchronized(lock) {
        if (strategy is KeyFrameStrategy.BoundedInterval) return@synchronized
        // Only the first request of a run starts the count. Restarting it on every request is what
        // the whole class exists to catch: an encoder that ignores the parameter, paired with a
        // receiver that re-asks every second, would reset the counter before it ever reached the
        // threshold -- so the watchdog would never fire on exactly the device it was written for.
        if (framesSinceRequest == NO_REQUEST_OUTSTANDING) framesSinceRequest = 0
    }

    /**
     * Call for every frame the encoder emits.
     *
     * @return the strategy now in force, so the caller can reconfigure on the frame it changes.
     */
    fun onFrameEmitted(keyFrame: Boolean): KeyFrameStrategy = synchronized(lock) {
        if (strategy is KeyFrameStrategy.BoundedInterval) return@synchronized strategy
        if (framesSinceRequest == NO_REQUEST_OUTSTANDING) return@synchronized strategy

        if (keyFrame) {
            framesSinceRequest = NO_REQUEST_OUTSTANDING
            return@synchronized strategy
        }

        framesSinceRequest++
        if (framesSinceRequest >= framesBeforeFallback) {
            strategy = KeyFrameStrategy.BoundedInterval(fallbackSeconds)
            framesSinceRequest = NO_REQUEST_OUTSTANDING
        }
        return@synchronized strategy
    }

    private companion object {
        /** No request is being waited on, so an emitted frame proves nothing either way. */
        const val NO_REQUEST_OUTSTANDING = -1

        /** Roughly a second and a half at sixty frames a second: long enough not to be a hiccup. */
        const val FRAMES_BEFORE_FALLBACK = 90
        const val FALLBACK_INTERVAL_SECONDS = 2
    }
}

/**
 * The rule that keeps a reconfigure from producing a frozen picture.
 *
 * Every `VIDEO_CONFIG` is a hard decoder reset on the receiver: it releases the codec, discards its
 * whole pending queue and re-arms its key-frame gate. So the very next packet after one must be an
 * intra frame, or it is counted as dropped and thrown away, and the television shows the last frame
 * it managed to decode until something else happens to produce a key frame. Rotation is the ordinary
 * way to hit this, because rotation re-sends the config.
 */
object VideoConfigPolicy {
    /** Always true. Written as a function so the call site reads as the rule rather than a constant. */
    fun requiresKeyFrameAfter(): Boolean = true
}
