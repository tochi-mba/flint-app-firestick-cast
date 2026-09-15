package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * Driving a pointer with four buttons.
 *
 * This is the part Silk gets wrong: a constant-speed cursor is either too slow to cross a page or
 * too fast to hit a link. The answer is a tap that nudges precisely and a hold that accelerates,
 * plus edges that scroll instead of stopping dead.
 *
 * Pure arithmetic, so the feel is pinned by tests rather than discovered on a television.
 */
class BrowserCursorEngineTest {
    private val engine = BrowserCursorEngine()
    private val viewport = CursorViewport(1920, 1080)

    @Test
    fun `the cursor starts in the middle of the page`() {
        val state = engine.centre(viewport)

        assertEquals(960, state.x)
        assertEquals(540, state.y)
    }

    @Test
    fun `a tap moves a small, exact distance`() {
        // Aiming at a link is the case a TV cursor usually fails. One press must be predictable and
        // small enough to land on something.
        val start = engine.centre(viewport)

        val nudged = engine.tap(start, CursorDirection.RIGHT, viewport)

        assertEquals(start.x + BrowserCursorEngine.TAP_PIXELS, nudged.state.x)
        assertEquals(start.y, nudged.state.y)
        assertEquals(0, nudged.scrollX)
        assertEquals(0, nudged.scrollY)
    }

    @Test
    fun `holding a direction accelerates rather than crawling`() {
        var state = engine.centre(viewport).let { engine.tap(it, CursorDirection.RIGHT, viewport).state }
        val distances = mutableListOf<Int>()

        repeat(12) {
            val before = state.x
            val step = engine.hold(state, FRAME_MILLIS, viewport)
            state = step.state
            distances += state.x - before
        }

        assertTrue(distances.first() < distances.last(), "cursor never sped up: $distances")
        assertTrue(
            distances.zipWithNext().all { (a, b) -> b >= a },
            "acceleration was not monotonic: $distances",
        )
    }

    @Test
    fun `acceleration stops at a speed a person can still aim with`() {
        var state = engine.tap(engine.centre(viewport), CursorDirection.RIGHT, viewport).state
        repeat(200) { state = engine.hold(state, FRAME_MILLIS, viewport).state }

        val before = state.x
        state = engine.hold(state, FRAME_MILLIS, viewport).state

        assertTrue(
            state.x - before <= BrowserCursorEngine.MAX_PIXELS_PER_FRAME,
            "cursor ran away at ${state.x - before}px per frame",
        )
    }

    @Test
    fun `the cursor never leaves the page`() {
        var state = engine.centre(viewport).copy(x = viewport.width - 2)
        state = engine.tap(state, CursorDirection.RIGHT, viewport).state
        repeat(400) { state = engine.hold(state, FRAME_MILLIS, viewport).state }

        assertTrue(state.x in 0 until viewport.width, "x escaped to ${state.x}")
        assertTrue(state.y in 0 until viewport.height, "y escaped to ${state.y}")
    }

    @Test
    fun `pushing past the edge scrolls the page instead of stopping dead`() {
        // Without this a page taller than the screen is unreachable: the cursor pins to the bottom
        // and nothing else happens, which is the single most common complaint about TV cursors.
        var state = engine.centre(viewport).copy(y = viewport.height - 1)
        state = engine.tap(state, CursorDirection.DOWN, viewport).state

        val step = engine.hold(state, FRAME_MILLIS, viewport)

        // Negative Y is Avalonia "scroll down" — the touch synthesizer moves the finger upward.
        assertTrue(step.scrollY < 0, "no downward scroll at the bottom edge: ${step.scrollY}")
        assertEquals(0, step.scrollX)
    }

    @Test
    fun `a tap already against the edge scrolls without waiting for a hold`() {
        val state = engine.centre(viewport).copy(y = viewport.height - 1)

        val step = engine.tap(state, CursorDirection.DOWN, viewport)

        assertTrue(step.scrollY < 0, "edge tap did not scroll: ${step.scrollY}")
        assertEquals(CursorDirection.DOWN, step.state.direction)
    }

    @Test
    fun `scrolling at an edge builds up the longer it is held`() {
        var state = engine.tap(
            engine.centre(viewport).copy(y = viewport.height - 1),
            CursorDirection.DOWN,
            viewport,
        ).state
        val first = engine.hold(state, FRAME_MILLIS, viewport)
        state = first.state
        repeat(20) { state = engine.hold(state, FRAME_MILLIS, viewport).state }

        val later = engine.hold(state, FRAME_MILLIS, viewport)

        assertTrue(
            later.scrollY < first.scrollY,
            "scroll did not ramp: ${first.scrollY} -> ${later.scrollY}",
        )
    }

    @Test
    fun `an edge only scrolls in the direction being pushed`() {
        val state = engine.tap(
            engine.centre(viewport).copy(y = viewport.height - 1),
            CursorDirection.UP,
            viewport,
        ).state

        val step = engine.hold(state, FRAME_MILLIS, viewport)

        assertEquals(0, step.scrollY)
    }

    @Test
    fun `releasing forgets the built-up speed so the next tap is precise again`() {
        var state = engine.tap(engine.centre(viewport), CursorDirection.RIGHT, viewport).state
        repeat(30) { state = engine.hold(state, FRAME_MILLIS, viewport).state }

        val released = engine.release(state)

        assertNull(released.direction)
        assertEquals(0L, released.heldMillis)

        val next = engine.tap(released, CursorDirection.RIGHT, viewport)
        assertEquals(released.x + BrowserCursorEngine.TAP_PIXELS, next.state.x)
    }

    @Test
    fun `changing direction restarts the ramp rather than inheriting the old speed`() {
        var state = engine.tap(engine.centre(viewport), CursorDirection.RIGHT, viewport).state
        repeat(40) { state = engine.hold(state, FRAME_MILLIS, viewport).state }

        val turned = engine.tap(state, CursorDirection.DOWN, viewport)

        assertEquals(0L, turned.state.heldMillis)
        assertEquals(CursorDirection.DOWN, turned.state.direction)
    }

    @Test
    fun `a viewport that has not been measured yet cannot move the cursor`() {
        val unmeasured = CursorViewport(0, 0)
        val state = engine.centre(unmeasured)

        val step = engine.hold(
            engine.tap(state, CursorDirection.RIGHT, unmeasured).state,
            FRAME_MILLIS,
            unmeasured,
        )

        assertEquals(0, step.state.x)
        assertEquals(0, step.state.y)
        assertEquals(0, step.scrollX)
    }

    @Test
    fun `a resized page keeps the cursor inside it`() {
        val state = engine.centre(viewport)

        val clamped = engine.clampTo(state, CursorViewport(640, 360))

        assertTrue(clamped.x < 640)
        assertTrue(clamped.y < 360)
    }

    private companion object {
        /** One frame at 60Hz, the rate the surface repeats a held direction at. */
        const val FRAME_MILLIS = 16L
    }
}
