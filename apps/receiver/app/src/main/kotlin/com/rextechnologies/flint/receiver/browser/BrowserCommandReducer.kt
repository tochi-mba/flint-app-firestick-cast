package com.rextechnologies.flint.receiver.browser

/** One visible receiver surface owns the TV at a time. */
enum class BrowserSurfaceOwner {
    IDLE,
    BROWSER,
    MIRROR,
    PLAYER,
    PRESENTATION,
}

/**
 * The small command state is deliberately independent from WebView. A command must be accepted
 * here before a coordinator may call an Android/browser port.
 */
data class BrowserCommandState(
    val surface: BrowserSurfaceOwner = BrowserSurfaceOwner.IDLE,
    val activeEpoch: Long? = null,
    val observedEpoch: Long = 0,
    val lastCommandId: Long = 0,
    val navigationId: Long = 0,
)

sealed interface BrowserCommand {
    val epoch: Long
    val commandId: Long

    data class Open(
        override val epoch: Long,
        override val commandId: Long,
        val address: BrowserAddress,
    ) : BrowserCommand

    data class Navigate(
        override val epoch: Long,
        override val commandId: Long,
        val address: BrowserAddress,
    ) : BrowserCommand

    data class Close(
        override val epoch: Long,
        override val commandId: Long,
    ) : BrowserCommand

    /** Starts a TV-owned browser without pretending a public homepage was requested. */
    data class OpenBlank(
        override val epoch: Long,
        override val commandId: Long,
    ) : BrowserCommand

    /** Ordered cockpit operation that changes no navigation by itself. */
    data class Control(
        override val epoch: Long,
        override val commandId: Long,
    ) : BrowserCommand

    /** Replaces the visible renderer after a profile switch, on the same ordered epoch. */
    data class ResetBlank(
        override val epoch: Long,
        override val commandId: Long,
    ) : BrowserCommand
}

enum class BrowserCommandRejection {
    INVALID_IDENTIFIER,
    STALE_EPOCH,
    STALE_COMMAND,
    NO_ACTIVE_BROWSER,
    SURFACE_BUSY,
    BROWSER_REQUIRES_OPEN,
}

sealed interface BrowserCommandEffect {
    data class Open(val epoch: Long, val commandId: Long, val address: BrowserAddress) : BrowserCommandEffect
    data class OpenBlank(val epoch: Long, val commandId: Long) : BrowserCommandEffect
    data class Navigate(val epoch: Long, val commandId: Long, val address: BrowserAddress) : BrowserCommandEffect
    data class Close(val epoch: Long, val commandId: Long) : BrowserCommandEffect
    data class Control(val epoch: Long, val commandId: Long) : BrowserCommandEffect
    data class ResetBlank(val epoch: Long, val commandId: Long) : BrowserCommandEffect
    data class SurfaceChanged(val surface: BrowserSurfaceOwner) : BrowserCommandEffect
    data class Rejected(val reason: BrowserCommandRejection) : BrowserCommandEffect
    data object Noop : BrowserCommandEffect
}

data class BrowserCommandTransition(
    val state: BrowserCommandState,
    val effect: BrowserCommandEffect,
)

class BrowserCommandReducer {
    fun reduce(state: BrowserCommandState, command: BrowserCommand): BrowserCommandTransition {
        if (command.epoch <= 0 || command.commandId <= 0) {
            return rejected(state, BrowserCommandRejection.INVALID_IDENTIFIER)
        }
        return when (command) {
            is BrowserCommand.Open -> open(state, command)
            is BrowserCommand.OpenBlank -> openBlank(state, command)
            is BrowserCommand.Navigate -> navigate(state, command)
            is BrowserCommand.Close -> close(state, command)
            is BrowserCommand.Control -> control(state, command)
            is BrowserCommand.ResetBlank -> resetBlank(state, command)
        }
    }

    /**
     * Records a player/mirror/presentation claim without allowing it to impersonate browser open.
     * A browser coordinator must execute terminal cleanup before an external surface may be claimed.
     */
    fun claimExternalSurface(
        state: BrowserCommandState,
        owner: BrowserSurfaceOwner,
    ): BrowserCommandTransition {
        if (owner == BrowserSurfaceOwner.BROWSER) {
            val reason = if (state.surface == BrowserSurfaceOwner.IDLE) {
                BrowserCommandRejection.BROWSER_REQUIRES_OPEN
            } else {
                BrowserCommandRejection.SURFACE_BUSY
            }
            return rejected(state, reason)
        }
        if (state.surface == BrowserSurfaceOwner.BROWSER) {
            return rejected(state, BrowserCommandRejection.SURFACE_BUSY)
        }
        if (state.surface != BrowserSurfaceOwner.IDLE && state.surface != owner) {
            return rejected(state, BrowserCommandRejection.SURFACE_BUSY)
        }
        if (state.surface == owner) {
            return BrowserCommandTransition(state, BrowserCommandEffect.Noop)
        }
        return BrowserCommandTransition(
            state.copy(surface = owner),
            BrowserCommandEffect.SurfaceChanged(owner),
        )
    }

    /**
     * Releases the surface after the host's session dies without a CLOSE.
     *
     * The transport can end at any moment — the desktop app quits, the laptop sleeps, the hotspot
     * drops — and none of those produce the CLOSE that [close] handles. Without this the surface
     * stays owned by a session that no longer exists, so the television keeps showing the dead
     * session's last page and every later OPEN is refused as `SURFACE_BUSY` until the app is
     * force-stopped. That is exactly the "why do I have to set it up again" failure, wearing a
     * different hat.
     *
     * Deliberately keeps [BrowserCommandState.observedEpoch] and
     * [BrowserCommandState.lastCommandId]: this is a cleanup, not a reset, and a command replayed
     * from the session that just died must still be refused afterwards.
     */
    fun abandon(state: BrowserCommandState): BrowserCommandTransition {
        if (state.surface != BrowserSurfaceOwner.BROWSER) {
            return BrowserCommandTransition(state, BrowserCommandEffect.Noop)
        }
        return BrowserCommandTransition(
            state.copy(surface = BrowserSurfaceOwner.IDLE, activeEpoch = null),
            BrowserCommandEffect.SurfaceChanged(BrowserSurfaceOwner.IDLE),
        )
    }

    private fun open(state: BrowserCommandState, command: BrowserCommand.Open): BrowserCommandTransition {
        // Epoch first: a replay from a dead session must stay cold even when the surface is free.
        if (command.epoch <= state.observedEpoch) {
            return rejected(state, BrowserCommandRejection.STALE_EPOCH)
        }
        // Mirror / player / presentation keep exclusive ownership. A live *browser* surface is
        // different: the host may have disconnected without CLOSE, or the TV opened a page on its
        // own, and a newer host OPEN must be able to reclaim or the desktop shows a URL that never
        // appears on the glass (SURFACE_BUSY with no wire rejection).
        if (state.surface != BrowserSurfaceOwner.IDLE && state.surface != BrowserSurfaceOwner.BROWSER) {
            return rejected(state, BrowserCommandRejection.SURFACE_BUSY)
        }
        return BrowserCommandTransition(
            state.copy(
                surface = BrowserSurfaceOwner.BROWSER,
                activeEpoch = command.epoch,
                observedEpoch = command.epoch,
                lastCommandId = command.commandId,
                navigationId = command.commandId,
            ),
            BrowserCommandEffect.Open(command.epoch, command.commandId, command.address),
        )
    }

    private fun navigate(state: BrowserCommandState, command: BrowserCommand.Navigate): BrowserCommandTransition {
        val epochRejection = activeEpochRejection(state, command.epoch)
        if (epochRejection != null) {
            return rejected(state, epochRejection)
        }
        if (command.commandId <= state.lastCommandId) {
            return rejected(state, BrowserCommandRejection.STALE_COMMAND)
        }
        return BrowserCommandTransition(
            state.copy(
                lastCommandId = command.commandId,
                navigationId = command.commandId,
            ),
            BrowserCommandEffect.Navigate(command.epoch, command.commandId, command.address),
        )
    }

    private fun close(state: BrowserCommandState, command: BrowserCommand.Close): BrowserCommandTransition {
        val epochRejection = activeEpochRejection(state, command.epoch)
        if (epochRejection != null) {
            return rejected(state, epochRejection)
        }
        if (command.commandId <= state.lastCommandId) {
            return rejected(state, BrowserCommandRejection.STALE_COMMAND)
        }
        return BrowserCommandTransition(
            BrowserCommandState(
                surface = BrowserSurfaceOwner.IDLE,
                activeEpoch = null,
                observedEpoch = command.epoch,
                lastCommandId = command.commandId,
            ),
            BrowserCommandEffect.Close(command.epoch, command.commandId),
        )
    }

    private fun openBlank(
        state: BrowserCommandState,
        command: BrowserCommand.OpenBlank,
    ): BrowserCommandTransition {
        if (command.epoch <= state.observedEpoch) {
            return rejected(state, BrowserCommandRejection.STALE_EPOCH)
        }
        if (state.surface != BrowserSurfaceOwner.IDLE && state.surface != BrowserSurfaceOwner.BROWSER) {
            return rejected(state, BrowserCommandRejection.SURFACE_BUSY)
        }
        return BrowserCommandTransition(
            state.copy(
                surface = BrowserSurfaceOwner.BROWSER,
                activeEpoch = command.epoch,
                observedEpoch = command.epoch,
                lastCommandId = command.commandId,
                navigationId = command.commandId,
            ),
            BrowserCommandEffect.OpenBlank(command.epoch, command.commandId),
        )
    }

    private fun control(state: BrowserCommandState, command: BrowserCommand.Control): BrowserCommandTransition {
        val epochRejection = activeEpochRejection(state, command.epoch)
        if (epochRejection != null) {
            return rejected(state, epochRejection)
        }
        if (command.commandId <= state.lastCommandId) {
            return rejected(state, BrowserCommandRejection.STALE_COMMAND)
        }
        return BrowserCommandTransition(
            state.copy(lastCommandId = command.commandId),
            BrowserCommandEffect.Control(command.epoch, command.commandId),
        )
    }

    private fun resetBlank(
        state: BrowserCommandState,
        command: BrowserCommand.ResetBlank,
    ): BrowserCommandTransition {
        val epochRejection = activeEpochRejection(state, command.epoch)
        if (epochRejection != null) {
            return rejected(state, epochRejection)
        }
        if (command.commandId <= state.lastCommandId) {
            return rejected(state, BrowserCommandRejection.STALE_COMMAND)
        }
        return BrowserCommandTransition(
            state.copy(lastCommandId = command.commandId, navigationId = command.commandId),
            BrowserCommandEffect.ResetBlank(command.epoch, command.commandId),
        )
    }

    private fun activeEpochRejection(
        state: BrowserCommandState,
        epoch: Long,
    ): BrowserCommandRejection? = when {
        state.surface != BrowserSurfaceOwner.BROWSER || state.activeEpoch == null ->
            BrowserCommandRejection.NO_ACTIVE_BROWSER
        state.activeEpoch != epoch -> BrowserCommandRejection.STALE_EPOCH
        else -> null
    }

    private fun rejected(
        state: BrowserCommandState,
        reason: BrowserCommandRejection,
    ): BrowserCommandTransition = BrowserCommandTransition(state, BrowserCommandEffect.Rejected(reason))
}
