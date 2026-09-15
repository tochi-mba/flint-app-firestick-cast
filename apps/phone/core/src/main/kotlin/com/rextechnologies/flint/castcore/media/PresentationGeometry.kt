package com.rextechnologies.flint.castcore.media

/**
 * The size of the canvas a second screen draws.
 *
 * A television is landscape. The phone that drives it is usually held upright, and deriving the
 * canvas from the phone's own screen -- which is what the mirror correctly does -- produced a
 * portrait second screen with the television's picture squeezed into the middle third. So this
 * canvas is fixed at 1080p landscape, bounded by the same cap the encoder applies to everything,
 * and it does not change when the phone is turned. The receiver does not yet say what size it
 * would like; when the protocol carries that, this is the one place that reads it.
 */
object PresentationGeometry {
    const val CANVAS_WIDTH: Int = 1920
    const val CANVAS_HEIGHT: Int = 1080

    /**
     * The density the canvas is laid out at.
     *
     * Android's own bucket for a 1080p television is xhdpi, which makes the canvas 960 by 540
     * density-independent pixels: the same geometry the receiver's screens are drawn and snapshot at,
     * so text sized to read from a sofa there reads from a sofa here.
     */
    const val DENSITY_DPI: Int = 320

    /** Width and height, already even and inside the encoder's cap. */
    fun canvas(): Pair<Int, Int> =
        EncoderPolicy.scaleToFit(CANVAS_WIDTH, CANVAS_HEIGHT, EncoderPolicy.MAXIMUM_LONG_EDGE)
}

/**
 * Trips after a run of refused socket writes.
 *
 * `sendVideoPacket` answers `false` when the socket will not take a frame, and an encoder that goes
 * on producing frames into a socket that refuses every one of them is a phone warming its pocket
 * for nothing. One refusal is not a dead link -- a full kernel buffer refuses a write and takes the
 * next -- so the count is of consecutive refusals, reset by any success, and it trips exactly once.
 *
 * Single-writer: only the encoder thread calls [onWrite], which is why it holds no lock on the frame
 * path. [tripped] is read from other threads and is volatile for that reason.
 */
class SendFailureWatch(private val limit: Int = DEFAULT_LIMIT) {
    init {
        require(limit >= 1) { "A limit below one trips before anything has been refused" }
    }

    private var consecutive: Int = 0

    @Volatile
    var tripped: Boolean = false
        private set

    /** Records one write. Returns `true` on the write that reaches the limit, and never again. */
    fun onWrite(sent: Boolean): Boolean {
        if (sent) {
            consecutive = 0
            return false
        }
        consecutive++
        if (consecutive >= limit && !tripped) {
            tripped = true
            return true
        }
        return false
    }

    companion object {
        /** Half a second at sixty frames a second: long enough for a stall, short enough to notice. */
        const val DEFAULT_LIMIT: Int = 30
    }
}
