package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertTrue

class BrowserLifecycleReducerTest {
    private val reducer = BrowserLifecycleReducer()

    @Test
    fun `terminal close has the documented exact ordered cleanup before target surface activation`() {
        val active = BrowserLifecycleState.active(epoch = 4)
        val transition = reducer.reduce(
            active,
            BrowserLifecycleEvent.SwitchSurface(BrowserSurfaceOwner.MIRROR),
            nowMs = 100,
        )

        assertEquals(BrowserSurfaceOwner.IDLE, transition.state.surface)
        assertEquals(
            listOf(
                BrowserLifecycleEffect.RejectBrowserCommands,
                BrowserLifecycleEffect.ResolveDialogsAsCancelled,
                BrowserLifecycleEffect.StopLoadingAndCancelWork,
                BrowserLifecycleEffect.PublishClosing,
                BrowserLifecycleEffect.DetachWebView,
                BrowserLifecycleEffect.DestroyWebView,
                BrowserLifecycleEffect.DisposeBrowserResources,
                BrowserLifecycleEffect.ReleaseBrowserOwnership,
                BrowserLifecycleEffect.PublishIdle,
                BrowserLifecycleEffect.ActivateSurface(BrowserSurfaceOwner.MIRROR),
            ),
            transition.effects,
        )
    }

    @Test
    fun `disconnect immediately disables remote input then starts bounded reconnect grace`() {
        val active = BrowserLifecycleState.active(epoch = 4)
        val disconnected = reducer.reduce(active, BrowserLifecycleEvent.HostDisconnected, nowMs = 10)

        assertEquals(BrowserLifecyclePhase.CONTROLLER_GRACE, disconnected.state.phase)
        assertFalse(disconnected.state.remoteInputEnabled)
        assertEquals(10 + BrowserLifecycleReducer.RECONNECT_GRACE_MS, disconnected.state.graceDeadlineMs)
        assertTrue(disconnected.effects.contains(BrowserLifecycleEffect.StopRemoteInput))
        assertTrue(disconnected.effects.contains(BrowserLifecycleEffect.ScheduleGraceExpiry(30_010)))

        val tooEarly = reducer.reduce(disconnected.state, BrowserLifecycleEvent.GraceExpired, nowMs = 30_009)
        assertEquals(disconnected.state, tooEarly.state)
        val expired = reducer.reduce(disconnected.state, BrowserLifecycleEvent.GraceExpired, nowMs = 30_010)
        assertEquals(BrowserLifecyclePhase.IDLE, expired.state.phase)
        assertTrue(expired.effects.contains(BrowserLifecycleEffect.DestroyWebView))
    }

    @Test
    fun `reconnect requires a fresh higher epoch and cancels the grace timer`() {
        val grace = reducer.reduce(
            BrowserLifecycleState.active(epoch = 4),
            BrowserLifecycleEvent.HostDisconnected,
            nowMs = 0,
        ).state
        val rejected = reducer.reduce(grace, BrowserLifecycleEvent.HostReconnected(epoch = 4), nowMs = 1)
        assertEquals(grace, rejected.state)

        val reconnected = reducer.reduce(grace, BrowserLifecycleEvent.HostReconnected(epoch = 5), nowMs = 1)
        assertEquals(BrowserLifecyclePhase.ACTIVE, reconnected.state.phase)
        assertTrue(reconnected.state.remoteInputEnabled)
        assertIs<BrowserLifecycleEffect.CancelGraceExpiry>(reconnected.effects.first())
    }

    @Test
    fun `driver failure finishes in explicit error only after cleanup`() {
        val transition = reducer.reduce(
            BrowserLifecycleState.active(epoch = 4),
            BrowserLifecycleEvent.DriverFailure,
            nowMs = 0,
        )
        assertEquals(BrowserLifecyclePhase.ERROR, transition.state.phase)
        assertEquals(BrowserTerminalReason.DRIVER_FAILURE, transition.state.failure)
        assertEquals(
            BrowserLifecycleEffect.PublishError(BrowserTerminalReason.DRIVER_FAILURE),
            transition.effects.last(),
        )
    }
}
