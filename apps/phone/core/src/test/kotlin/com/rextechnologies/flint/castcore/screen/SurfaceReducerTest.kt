package com.rextechnologies.flint.castcore.screen

import com.rextechnologies.flint.castcore.copy.MediaCopy
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

class SurfaceReducerTest {
    private fun after(current: ActiveSurface, request: SurfaceRequest) = SurfaceReducer.transition(current, request)

    @Test
    fun `from idle, any start is just that start`() {
        assertEquals(
            SurfaceTransition(ActiveSurface.Presentation, emptyList()),
            after(ActiveSurface.Idle, SurfaceRequest.StartPresentation),
        )
        assertEquals(
            SurfaceTransition(ActiveSurface.Mirror, emptyList()),
            after(ActiveSurface.Idle, SurfaceRequest.StartMirror),
        )
        assertEquals(
            SurfaceTransition(ActiveSurface.Media(), emptyList()),
            after(ActiveSurface.Idle, SurfaceRequest.StartMedia),
        )
    }

    @Test
    fun `starting a file while a second screen is live stops the screen and says so`() {
        val transition = after(ActiveSurface.Presentation, SurfaceRequest.StartMedia)
        assertEquals(ActiveSurface.Media(ActiveSurface.Displaced.PRESENTATION), transition.next)
        assertEquals(
            listOf(SurfaceEffect.StopOutput, SurfaceEffect.Say(MediaCopy.REPLACED_SECOND_SCREEN)),
            transition.effects,
        )
    }

    @Test
    fun `starting a file while a mirror is live stops the mirror and says so`() {
        val transition = after(ActiveSurface.Mirror, SurfaceRequest.StartMedia)
        assertEquals(ActiveSurface.Media(ActiveSurface.Displaced.MIRROR), transition.next)
        assertEquals(
            listOf(SurfaceEffect.StopOutput, SurfaceEffect.Say(MediaCopy.REPLACED_MIRROR)),
            transition.effects,
        )
    }

    @Test
    fun `clearing a file that displaced a screen says the screen is not coming back on its own`() {
        val cleared = after(ActiveSurface.Media(ActiveSurface.Displaced.PRESENTATION), SurfaceRequest.ClearMedia)
        assertEquals(ActiveSurface.Idle, cleared.next)
        assertEquals(
            listOf(SurfaceEffect.ClearMedia, SurfaceEffect.Say(MediaCopy.SECOND_SCREEN_NOT_RESTARTED)),
            cleared.effects,
        )

        val plain = after(ActiveSurface.Media(), SurfaceRequest.ClearMedia)
        assertEquals(listOf(SurfaceEffect.ClearMedia), plain.effects)
    }

    @Test
    fun `starting a screen mode over a playing file clears the file first`() {
        assertEquals(
            listOf(SurfaceEffect.ClearMedia),
            after(ActiveSurface.Media(), SurfaceRequest.StartPresentation).effects,
        )
        assertEquals(ActiveSurface.Mirror, after(ActiveSurface.Media(), SurfaceRequest.StartMirror).next)
    }

    @Test
    fun `switching between the two screen modes stops the one that was running`() {
        assertEquals(
            listOf(SurfaceEffect.StopOutput),
            after(ActiveSurface.Presentation, SurfaceRequest.StartMirror).effects,
        )
        assertEquals(
            listOf(SurfaceEffect.StopOutput),
            after(ActiveSurface.Mirror, SurfaceRequest.StartPresentation).effects,
        )
    }

    @Test
    fun `the screen mode's stop leaves a playing file alone, and a repeat request changes nothing`() {
        val playing = ActiveSurface.Media()
        assertEquals(SurfaceTransition(playing, emptyList()), after(playing, SurfaceRequest.StopOutput))
        assertEquals(
            SurfaceTransition(ActiveSurface.Idle, emptyList()),
            after(ActiveSurface.Mirror, SurfaceRequest.StopOutput),
        )
        assertEquals(
            SurfaceTransition(ActiveSurface.Mirror, emptyList()),
            after(ActiveSurface.Mirror, SurfaceRequest.StartMirror),
        )
        assertEquals(
            SurfaceTransition(ActiveSurface.Idle, emptyList()),
            after(ActiveSurface.Idle, SurfaceRequest.ClearMedia),
        )
    }

    @Test
    fun `a lost session returns to idle from anywhere with nothing to do`() {
        listOf(ActiveSurface.Idle, ActiveSurface.Presentation, ActiveSurface.Mirror, ActiveSurface.Media()).forEach {
            assertEquals(SurfaceTransition(ActiveSurface.Idle, emptyList()), after(it, SurfaceRequest.SessionLost))
        }
    }

    @Test
    fun `no transition ever produces two surfaces, and every one is reachable`() {
        val surfaces = listOf(
            ActiveSurface.Idle,
            ActiveSurface.Presentation,
            ActiveSurface.Mirror,
            ActiveSurface.Media(),
            ActiveSurface.Media(ActiveSurface.Displaced.PRESENTATION),
            ActiveSurface.Media(ActiveSurface.Displaced.MIRROR),
        )
        val requests = listOf(
            SurfaceRequest.StartPresentation,
            SurfaceRequest.StartMirror,
            SurfaceRequest.StartMedia,
            SurfaceRequest.StopOutput,
            SurfaceRequest.ClearMedia,
            SurfaceRequest.SessionLost,
        )
        val reached = mutableSetOf<ActiveSurface>()
        surfaces.forEach { surface ->
            requests.forEach { request ->
                val transition = after(surface, request)
                reached += transition.next
                // A stop and a clear in the same transition would mean two surfaces were live.
                assertTrue(
                    !(SurfaceEffect.StopOutput in transition.effects && SurfaceEffect.ClearMedia in transition.effects),
                    "$surface + $request",
                )
            }
        }
        assertTrue(surfaces.all { it in reached })
    }
}
