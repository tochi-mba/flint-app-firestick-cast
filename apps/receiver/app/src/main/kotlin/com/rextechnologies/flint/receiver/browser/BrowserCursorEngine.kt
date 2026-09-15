package com.rextechnologies.flint.receiver.browser

/** Which way the viewer is pushing the D-pad. */
enum class CursorDirection { UP, DOWN, LEFT, RIGHT }

/** The page area the cursor lives in, in device pixels. */
data class CursorViewport(val width: Int, val height: Int) {
    val measured: Boolean get() = width > 0 && height > 0
}

/**
 * Where the cursor is and how long it has been travelling.
 *
 * [heldMillis] is the whole acceleration model: it resets on release and on a change of direction,
 * so speed is a function of one continuous push rather than of session history.
 */
data class CursorState(
    val x: Int = 0,
    val y: Int = 0,
    val direction: CursorDirection? = null,
    val heldMillis: Long = 0,
)

/** One frame of cursor movement, plus any page scrolling the edge asked for. */
data class CursorStep(
    val state: CursorState,
    val scrollX: Int = 0,
    val scrollY: Int = 0,
)

/**
 * A pointer that four buttons can actually aim.
 *
 * The problem with a D-pad cursor is that one speed cannot serve both jobs: slow enough to land on a
 * link is far too slow to cross a page, and fast enough to cross a page cannot hit anything. So a
 * tap is a small fixed nudge for aiming, and a hold ramps from that up to a travel speed. Pressing
 * on at an edge scrolls the page rather than pinning the cursor against the side, which is what
 * makes a page taller than the screen reachable at all.
 *
 * Deliberately pure arithmetic. The feel of this is the feel of the whole browser, and it is pinned
 * by tests instead of being tuned by hand on a television.
 */
class BrowserCursorEngine {
    companion object {
        /** One press. Small enough to aim a link, large enough to be worth pressing. */
        const val TAP_PIXELS = 24

        /** Where a held direction starts, before any acceleration. */
        const val MIN_PIXELS_PER_FRAME = 6

        /** The ceiling. Above this the cursor is faster than a person can track. */
        const val MAX_PIXELS_PER_FRAME = 34

        /** How long a hold takes to reach full speed. */
        const val RAMP_MILLIS = 700L

        /** How close to the edge counts as pushing against it. */
        const val EDGE_MARGIN_PIXELS = 48

        /** The first scroll step when the cursor reaches an edge. */
        const val MIN_SCROLL_PIXELS = 24

        /** The fastest an edge will scroll, so a long press cannot fling a page uncontrollably. */
        const val MAX_SCROLL_PIXELS = 96
    }

    fun centre(viewport: CursorViewport): CursorState =
        if (!viewport.measured) {
            CursorState()
        } else {
            CursorState(x = viewport.width / 2, y = viewport.height / 2)
        }

    /**
     * One deliberate press.
     *
     * Also arms the hold: it records the direction and resets the ramp, so turning mid-travel starts
     * slow again rather than inheriting speed built up going the other way.
     *
     * When the press lands against an edge, the step carries a scroll delta. Fire TV remotes often
     * emit discrete down/up pairs while "held", so edge scrolling cannot wait for the frame loop.
     */
    fun tap(state: CursorState, direction: CursorDirection, viewport: CursorViewport): CursorStep {
        if (!viewport.measured) {
            return CursorStep(state.copy(direction = direction, heldMillis = 0))
        }
        val moved = move(state, direction, TAP_PIXELS, viewport)
            .copy(direction = direction, heldMillis = 0)
        return if (isAgainstEdge(moved, direction, viewport)) {
            val (scrollX, scrollY) = edgeScroll(direction, MIN_SCROLL_PIXELS)
            CursorStep(moved, scrollX = scrollX, scrollY = scrollY)
        } else {
            CursorStep(moved)
        }
    }

    /** One frame of a held direction. */
    fun hold(state: CursorState, elapsedMillis: Long, viewport: CursorViewport): CursorStep {
        val direction = state.direction ?: return CursorStep(state)
        if (!viewport.measured) {
            return CursorStep(state)
        }

        val held = state.heldMillis + elapsedMillis.coerceAtLeast(0)
        val advanced = state.copy(heldMillis = held)

        if (isAgainstEdge(advanced, direction, viewport)) {
            val distance = ramp(held, MIN_SCROLL_PIXELS, MAX_SCROLL_PIXELS)
            val (scrollX, scrollY) = edgeScroll(direction, distance)
            return CursorStep(state = advanced, scrollX = scrollX, scrollY = scrollY)
        }

        val distance = ramp(held, MIN_PIXELS_PER_FRAME, MAX_PIXELS_PER_FRAME)
        return CursorStep(move(advanced, direction, distance, viewport))
    }

    /** Lets go. The next tap is precise again. */
    fun release(state: CursorState): CursorState = state.copy(direction = null, heldMillis = 0)

    /** Keeps the cursor inside a page that changed size under it. */
    fun clampTo(state: CursorState, viewport: CursorViewport): CursorState {
        if (!viewport.measured) return state
        return state.copy(
            x = state.x.coerceIn(0, viewport.width - 1),
            y = state.y.coerceIn(0, viewport.height - 1),
        )
    }

    private fun move(
        state: CursorState,
        direction: CursorDirection,
        distance: Int,
        viewport: CursorViewport,
    ): CursorState {
        val x = when (direction) {
            CursorDirection.LEFT -> state.x - distance
            CursorDirection.RIGHT -> state.x + distance
            else -> state.x
        }
        val y = when (direction) {
            CursorDirection.UP -> state.y - distance
            CursorDirection.DOWN -> state.y + distance
            else -> state.y
        }
        return state.copy(
            x = x.coerceIn(0, viewport.width - 1),
            y = y.coerceIn(0, viewport.height - 1),
        )
    }

    /**
     * Whether the cursor has run out of page in the direction being pushed.
     *
     * A margin rather than the exact edge, so scrolling begins as the cursor arrives instead of
     * after a frame spent pinned against the side.
     */
    private fun isAgainstEdge(
        state: CursorState,
        direction: CursorDirection,
        viewport: CursorViewport,
    ): Boolean = when (direction) {
        CursorDirection.UP -> state.y <= EDGE_MARGIN_PIXELS
        CursorDirection.DOWN -> state.y >= viewport.height - 1 - EDGE_MARGIN_PIXELS
        CursorDirection.LEFT -> state.x <= EDGE_MARGIN_PIXELS
        CursorDirection.RIGHT -> state.x >= viewport.width - 1 - EDGE_MARGIN_PIXELS
    }

    /**
     * Page scroll deltas in the host/Avalonia convention used by [BrowserNativeInput.Scroll]:
     * positive Y scrolls up (finger travels down in the touch synthesizer). Pushing the cursor
     * toward an edge must scroll the page the same way — down at the bottom reveals content below.
     */
    private fun edgeScroll(direction: CursorDirection, distance: Int): Pair<Int, Int> = when (direction) {
        CursorDirection.UP -> 0 to distance
        CursorDirection.DOWN -> 0 to -distance
        CursorDirection.LEFT -> distance to 0
        CursorDirection.RIGHT -> -distance to 0
    }

    /**
     * Eased acceleration from [minimum] to [maximum] over [RAMP_MILLIS].
     *
     * Quadratic ease-out: quick to leave the floor so a hold feels responsive, then flattening so
     * the top speed arrives gradually rather than as a jump.
     */
    private fun ramp(heldMillis: Long, minimum: Int, maximum: Int): Int {
        val progress = (heldMillis.toDouble() / RAMP_MILLIS).coerceIn(0.0, 1.0)
        val eased = 1.0 - (1.0 - progress) * (1.0 - progress)
        return (minimum + (maximum - minimum) * eased).toInt().coerceIn(minimum, maximum)
    }
}
