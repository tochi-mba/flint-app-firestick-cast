package com.rextechnologies.flint.receiver.browser

enum class BrowserLifecyclePhase {
    IDLE,
    ACTIVE,
    CONTROLLER_GRACE,
    CLOSING,
    ERROR,
}

enum class BrowserTerminalReason {
    DRIVER_FAILURE,
    EXPLICIT_CLOSE,
    CONTROLLER_GRACE_EXPIRED,
    SURFACE_SWITCH,
    SERVICE_STOPPED,
}

data class BrowserLifecycleState(
    val phase: BrowserLifecyclePhase = BrowserLifecyclePhase.IDLE,
    val surface: BrowserSurfaceOwner = BrowserSurfaceOwner.IDLE,
    val epoch: Long? = null,
    val remoteInputEnabled: Boolean = false,
    val graceDeadlineMs: Long? = null,
    val failure: BrowserTerminalReason? = null,
) {
    companion object {
        fun active(epoch: Long): BrowserLifecycleState {
            require(epoch > 0) { "Browser epoch must be positive" }
            return BrowserLifecycleState(
                phase = BrowserLifecyclePhase.ACTIVE,
                surface = BrowserSurfaceOwner.BROWSER,
                epoch = epoch,
                remoteInputEnabled = true,
            )
        }
    }
}

sealed interface BrowserLifecycleEvent {
    data class SwitchSurface(val target: BrowserSurfaceOwner) : BrowserLifecycleEvent
    data object HostDisconnected : BrowserLifecycleEvent
    data class HostReconnected(val epoch: Long) : BrowserLifecycleEvent
    data object GraceExpired : BrowserLifecycleEvent
    data object Close : BrowserLifecycleEvent
    data object DriverFailure : BrowserLifecycleEvent
    data object ServiceStopped : BrowserLifecycleEvent
}

sealed interface BrowserLifecycleEffect {
    data object RejectBrowserCommands : BrowserLifecycleEffect
    data object ResolveDialogsAsCancelled : BrowserLifecycleEffect
    data object StopLoadingAndCancelWork : BrowserLifecycleEffect
    data object PublishClosing : BrowserLifecycleEffect
    data object DetachWebView : BrowserLifecycleEffect
    data object DestroyWebView : BrowserLifecycleEffect
    data object DisposeBrowserResources : BrowserLifecycleEffect
    data object ReleaseBrowserOwnership : BrowserLifecycleEffect
    data object PublishIdle : BrowserLifecycleEffect
    data object StopRemoteInput : BrowserLifecycleEffect
    data object CancelGraceExpiry : BrowserLifecycleEffect
    data class ScheduleGraceExpiry(val deadlineMs: Long) : BrowserLifecycleEffect
    data class ActivateSurface(val surface: BrowserSurfaceOwner) : BrowserLifecycleEffect
    data class PublishError(val reason: BrowserTerminalReason) : BrowserLifecycleEffect
}

data class BrowserLifecycleTransition(
    val state: BrowserLifecycleState,
    val effects: List<BrowserLifecycleEffect>,
)

/**
 * Owns browser terminal ordering. An executor performs the effects in order; this pure reducer
 * never retains an Activity, WebView, socket, timer, or browser payload.
 */
class BrowserLifecycleReducer {
    companion object {
        const val RECONNECT_GRACE_MS: Long = 30_000
    }

    fun reduce(
        state: BrowserLifecycleState,
        event: BrowserLifecycleEvent,
        nowMs: Long,
    ): BrowserLifecycleTransition = when (event) {
        is BrowserLifecycleEvent.SwitchSurface -> switchSurface(state, event.target)
        BrowserLifecycleEvent.HostDisconnected -> disconnected(state, nowMs)
        is BrowserLifecycleEvent.HostReconnected -> reconnected(state, event.epoch)
        BrowserLifecycleEvent.GraceExpired -> graceExpired(state, nowMs)
        BrowserLifecycleEvent.Close -> terminalIdle(state)
        BrowserLifecycleEvent.DriverFailure -> driverFailure(state)
        BrowserLifecycleEvent.ServiceStopped -> terminalIdle(state)
    }

    private fun switchSurface(
        state: BrowserLifecycleState,
        target: BrowserSurfaceOwner,
    ): BrowserLifecycleTransition {
        if (!isBrowserLive(state) || target == BrowserSurfaceOwner.BROWSER) return noChange(state)
        return terminalIdle(state, target)
    }

    private fun disconnected(state: BrowserLifecycleState, nowMs: Long): BrowserLifecycleTransition {
        if (state.phase != BrowserLifecyclePhase.ACTIVE || nowMs < 0) return noChange(state)
        val deadline = deadlineAfter(nowMs)
        return BrowserLifecycleTransition(
            state.copy(
                phase = BrowserLifecyclePhase.CONTROLLER_GRACE,
                remoteInputEnabled = false,
                graceDeadlineMs = deadline,
            ),
            listOf(
                BrowserLifecycleEffect.StopRemoteInput,
                BrowserLifecycleEffect.ScheduleGraceExpiry(deadline),
            ),
        )
    }

    private fun reconnected(state: BrowserLifecycleState, epoch: Long): BrowserLifecycleTransition {
        val currentEpoch = state.epoch
        if (state.phase != BrowserLifecyclePhase.CONTROLLER_GRACE || currentEpoch == null || epoch <= currentEpoch) {
            return noChange(state)
        }
        return BrowserLifecycleTransition(
            state.copy(
                phase = BrowserLifecyclePhase.ACTIVE,
                epoch = epoch,
                remoteInputEnabled = true,
                graceDeadlineMs = null,
            ),
            listOf(BrowserLifecycleEffect.CancelGraceExpiry),
        )
    }

    private fun graceExpired(state: BrowserLifecycleState, nowMs: Long): BrowserLifecycleTransition {
        val deadline = state.graceDeadlineMs
        if (state.phase != BrowserLifecyclePhase.CONTROLLER_GRACE || deadline == null || nowMs < deadline) {
            return noChange(state)
        }
        return terminalIdle(state)
    }

    private fun driverFailure(state: BrowserLifecycleState): BrowserLifecycleTransition {
        if (!isBrowserLive(state)) return noChange(state)
        val effects = cleanupEffects() + BrowserLifecycleEffect.PublishError(BrowserTerminalReason.DRIVER_FAILURE)
        return BrowserLifecycleTransition(
            BrowserLifecycleState(
                phase = BrowserLifecyclePhase.ERROR,
                failure = BrowserTerminalReason.DRIVER_FAILURE,
            ),
            effects,
        )
    }

    private fun terminalIdle(
        state: BrowserLifecycleState,
        target: BrowserSurfaceOwner? = null,
    ): BrowserLifecycleTransition {
        if (!isBrowserLive(state)) return noChange(state)
        val effects = buildList {
            addAll(cleanupEffects())
            add(BrowserLifecycleEffect.PublishIdle)
            if (target != null && target != BrowserSurfaceOwner.IDLE) {
                add(BrowserLifecycleEffect.ActivateSurface(target))
            }
        }
        return BrowserLifecycleTransition(BrowserLifecycleState(), effects)
    }

    private fun cleanupEffects(): List<BrowserLifecycleEffect> = listOf(
        BrowserLifecycleEffect.RejectBrowserCommands,
        BrowserLifecycleEffect.ResolveDialogsAsCancelled,
        BrowserLifecycleEffect.StopLoadingAndCancelWork,
        BrowserLifecycleEffect.PublishClosing,
        BrowserLifecycleEffect.DetachWebView,
        BrowserLifecycleEffect.DestroyWebView,
        BrowserLifecycleEffect.DisposeBrowserResources,
        BrowserLifecycleEffect.ReleaseBrowserOwnership,
    )

    private fun isBrowserLive(state: BrowserLifecycleState): Boolean =
        state.phase == BrowserLifecyclePhase.ACTIVE || state.phase == BrowserLifecyclePhase.CONTROLLER_GRACE

    private fun noChange(state: BrowserLifecycleState): BrowserLifecycleTransition =
        BrowserLifecycleTransition(state, emptyList())

    private fun deadlineAfter(nowMs: Long): Long =
        if (nowMs > Long.MAX_VALUE - RECONNECT_GRACE_MS) Long.MAX_VALUE else nowMs + RECONNECT_GRACE_MS
}
