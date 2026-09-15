package com.rextechnologies.flint.castcore.screen

import com.rextechnologies.flint.castcore.copy.MediaCopy

/**
 * What the television is being asked to show right now, from this phone.
 *
 * One of these at a time. A second screen and a pushed file are both surfaces the receiver renders,
 * and sending it two at once is not an error the wire reports -- the receiver simply shows whichever
 * message arrived last, while the phone goes on encoding into a picture nobody sees. So the phone
 * keeps one value that says what it has asked for, and every transition goes through [SurfaceReducer].
 */
sealed interface ActiveSurface {
    data object Idle : ActiveSurface

    data object Presentation : ActiveSurface

    data object Mirror : ActiveSurface

    /**
     * A file playing on the television.
     *
     * @property displaced what was on the television when playback started, so that clearing the
     *   file can say what it is not restarting rather than restarting it silently.
     */
    data class Media(val displaced: Displaced = Displaced.NOTHING) : ActiveSurface

    enum class Displaced { NOTHING, PRESENTATION, MIRROR }
}

/** What somebody has asked for. */
sealed interface SurfaceRequest {
    data object StartPresentation : SurfaceRequest

    data object StartMirror : SurfaceRequest

    data object StartMedia : SurfaceRequest

    /** The screen mode's own Stop. Playback is not a screen mode and is not affected. */
    data object StopOutput : SurfaceRequest

    data object ClearMedia : SurfaceRequest

    /** The session went away, taking everything with it. */
    data object SessionLost : SurfaceRequest
}

/** What has to happen for the transition to be true on the television as well as on the phone. */
sealed interface SurfaceEffect {
    /** Stop the encoder behind a second screen or a mirror. */
    data object StopOutput : SurfaceEffect

    /** Tell the television to drop the file and return to its idle screen. */
    data object ClearMedia : SurfaceEffect

    /** One sentence for the banner. */
    data class Say(val sentence: String) : SurfaceEffect
}

data class SurfaceTransition(val next: ActiveSurface, val effects: List<SurfaceEffect>)

/**
 * The one authority on which surface the television shows.
 *
 * Illegal combinations are impossible by construction: there is no state in which a second screen
 * and a file are both being sent, because the reducer never produces one. What it does instead is
 * say what it stopped, and, on the way back, what it is not restarting.
 */
object SurfaceReducer {
    fun transition(current: ActiveSurface, request: SurfaceRequest): SurfaceTransition = when (request) {
        SurfaceRequest.StartPresentation -> startScreen(current, ActiveSurface.Presentation)
        SurfaceRequest.StartMirror -> startScreen(current, ActiveSurface.Mirror)
        SurfaceRequest.StartMedia -> startMedia(current)
        SurfaceRequest.StopOutput -> when (current) {
            ActiveSurface.Presentation, ActiveSurface.Mirror -> SurfaceTransition(ActiveSurface.Idle, emptyList())
            ActiveSurface.Idle, is ActiveSurface.Media -> unchanged(current)
        }

        SurfaceRequest.ClearMedia -> when (current) {
            is ActiveSurface.Media -> SurfaceTransition(
                ActiveSurface.Idle,
                listOfNotNull(SurfaceEffect.ClearMedia, notRestarted(current.displaced)),
            )

            ActiveSurface.Idle, ActiveSurface.Presentation, ActiveSurface.Mirror -> unchanged(current)
        }

        SurfaceRequest.SessionLost -> SurfaceTransition(ActiveSurface.Idle, emptyList())
    }

    private fun startScreen(current: ActiveSurface, target: ActiveSurface): SurfaceTransition = when (current) {
        target -> unchanged(current)
        ActiveSurface.Idle -> SurfaceTransition(target, emptyList())
        ActiveSurface.Presentation, ActiveSurface.Mirror -> SurfaceTransition(target, listOf(SurfaceEffect.StopOutput))
        is ActiveSurface.Media -> SurfaceTransition(target, listOf(SurfaceEffect.ClearMedia))
    }

    private fun startMedia(current: ActiveSurface): SurfaceTransition = when (current) {
        ActiveSurface.Idle -> SurfaceTransition(ActiveSurface.Media(), emptyList())
        is ActiveSurface.Media -> unchanged(current)
        ActiveSurface.Presentation -> SurfaceTransition(
            ActiveSurface.Media(ActiveSurface.Displaced.PRESENTATION),
            listOf(SurfaceEffect.StopOutput, SurfaceEffect.Say(MediaCopy.REPLACED_SECOND_SCREEN)),
        )

        ActiveSurface.Mirror -> SurfaceTransition(
            ActiveSurface.Media(ActiveSurface.Displaced.MIRROR),
            listOf(SurfaceEffect.StopOutput, SurfaceEffect.Say(MediaCopy.REPLACED_MIRROR)),
        )
    }

    private fun notRestarted(displaced: ActiveSurface.Displaced): SurfaceEffect? = when (displaced) {
        ActiveSurface.Displaced.NOTHING -> null
        ActiveSurface.Displaced.PRESENTATION -> SurfaceEffect.Say(MediaCopy.SECOND_SCREEN_NOT_RESTARTED)
        ActiveSurface.Displaced.MIRROR -> SurfaceEffect.Say(MediaCopy.MIRROR_NOT_RESTARTED)
    }

    private fun unchanged(current: ActiveSurface) = SurfaceTransition(current, emptyList())
}
