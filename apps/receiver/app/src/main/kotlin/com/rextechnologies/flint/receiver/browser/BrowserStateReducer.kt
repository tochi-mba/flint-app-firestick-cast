package com.rextechnologies.flint.receiver.browser

import java.nio.charset.StandardCharsets

enum class BrowserPhase {
    IDLE,
    OPENING,
    LOADING,
    READY,
    CLOSING,
    ERROR,
}

/** Stable, non-page-detail failures that may cross the browser boundary. */
enum class BrowserFailure {
    BLOCKED_URL,
    SITE_NOT_FOUND,
    NO_CONNECTION,
    SITE_ERROR,
    CERTIFICATE_REJECTED,
    POPUP_DENIED,
    PERMISSION_DENIED,
    RENDERER_STOPPED,
    DRIVER_FAILURE,
    FEATURE_NOT_ENABLED,
}

/** Bounded display text from a page. Control characters cannot reach receiver chrome. */
data class BrowserPageTitle(val value: String) {
    companion object {
        const val MAX_UTF8_BYTES = 512

        fun fromPage(raw: String): BrowserPageTitle? {
            if (hasUnpairedSurrogate(raw)) {
                return null
            }
            val normalized = raw
                .map { character ->
                    if (character.isWhitespace() ||
                        Character.isISOControl(character)
                    ) {
                        ' '
                    } else {
                        character
                    }
                }
                .joinToString(separator = "")
                .trim()
                .replace(Regex(" {2,}"), " ")
            return BrowserPageTitle(truncateUtf8(normalized, MAX_UTF8_BYTES))
        }

        private fun truncateUtf8(value: String, maximumBytes: Int): String {
            if (value.toByteArray(StandardCharsets.UTF_8).size <= maximumBytes) {
                return value
            }
            val result = StringBuilder()
            var used = 0
            var index = 0
            while (index < value.length) {
                val codePoint = value.codePointAt(index)
                val piece = String(Character.toChars(codePoint))
                val bytes = piece.toByteArray(StandardCharsets.UTF_8).size
                if (used + bytes > maximumBytes) {
                    break
                }
                result.append(piece)
                used += bytes
                index += Character.charCount(codePoint)
            }
            return result.toString()
        }

        private fun hasUnpairedSurrogate(value: String): Boolean {
            var index = 0
            while (index < value.length) {
                when {
                    Character.isHighSurrogate(value[index]) -> {
                        if (index + 1 == value.length || !Character.isLowSurrogate(value[index + 1])) return true
                        index += 2
                    }
                    Character.isLowSurrogate(value[index]) -> return true
                    else -> index += 1
                }
            }
            return false
        }
    }
}

data class BrowserState(
    val phase: BrowserPhase = BrowserPhase.IDLE,
    val revision: Long = 0,
    val epoch: Long? = null,
    val navigationId: Long = 0,
    val lastAcceptedCommandId: Long = 0,
    val lastAcceptedInputSequence: Long = 0,
    val address: BrowserAddress? = null,
    val title: BrowserPageTitle? = null,
    val progressPercent: Int = 0,
    val canGoBack: Boolean = false,
    val canGoForward: Boolean = false,
    val failure: BrowserFailure? = null,
)

sealed interface BrowserStateEvent {
    data class OpenAccepted(val epoch: Long, val commandId: Long, val address: BrowserAddress) : BrowserStateEvent
    data class BlankOpened(val epoch: Long, val commandId: Long) : BrowserStateEvent
    data class NavigationAccepted(val epoch: Long, val commandId: Long, val address: BrowserAddress) : BrowserStateEvent
    data class Progress(val epoch: Long, val navigationId: Long, val percent: Int) : BrowserStateEvent
    data class Title(val epoch: Long, val navigationId: Long, val title: String) : BrowserStateEvent
    data class PageFinished(
        val epoch: Long,
        val navigationId: Long,
        val canGoBack: Boolean,
        val canGoForward: Boolean,
    ) : BrowserStateEvent
    data class Failed(val epoch: Long, val navigationId: Long, val failure: BrowserFailure) : BrowserStateEvent
    data class CloseAccepted(val epoch: Long, val commandId: Long) : BrowserStateEvent
    data class ControlAccepted(val epoch: Long, val commandId: Long) : BrowserStateEvent
    data class BlankReset(val epoch: Long, val commandId: Long) : BrowserStateEvent
    data class Closed(val epoch: Long, val commandId: Long) : BrowserStateEvent
}

/**
 * Reduces WebView observations to a monotonic, bounded state. Every stale observation returns the
 * original instance so callers cannot accidentally publish a new revision for ignored work.
 */
class BrowserStateReducer {
    fun reduce(state: BrowserState, event: BrowserStateEvent): BrowserState = when (event) {
        is BrowserStateEvent.OpenAccepted -> openAccepted(state, event)
        is BrowserStateEvent.BlankOpened -> blankOpened(state, event)
        is BrowserStateEvent.NavigationAccepted -> navigationAccepted(state, event)
        is BrowserStateEvent.Progress -> progress(state, event)
        is BrowserStateEvent.Title -> title(state, event)
        is BrowserStateEvent.PageFinished -> pageFinished(state, event)
        is BrowserStateEvent.Failed -> failed(state, event)
        is BrowserStateEvent.CloseAccepted -> closeAccepted(state, event)
        is BrowserStateEvent.ControlAccepted -> controlAccepted(state, event)
        is BrowserStateEvent.BlankReset -> blankReset(state, event)
        is BrowserStateEvent.Closed -> closed(state, event)
    }

    private fun openAccepted(state: BrowserState, event: BrowserStateEvent.OpenAccepted): BrowserState {
        if (event.epoch <= 0 || event.commandId <= 0) return state
        // First open requires IDLE. A later Open with a strictly newer epoch is a host reclaim of
        // an orphan/live browser (disconnect without CLOSE, or TV-opened page) and must replace
        // the visible page — otherwise the wire accepts Open while the glass keeps the old one.
        if (state.phase != BrowserPhase.IDLE) {
            val currentEpoch = state.epoch ?: return state
            if (event.epoch <= currentEpoch) return state
        }
        return state.advance(
            phase = BrowserPhase.OPENING,
            epoch = event.epoch,
            navigationId = event.commandId,
            lastAcceptedCommandId = event.commandId,
            address = event.address,
            title = null,
            progressPercent = 0,
            canGoBack = false,
            canGoForward = false,
            failure = null,
        )
    }

    private fun blankOpened(state: BrowserState, event: BrowserStateEvent.BlankOpened): BrowserState {
        if (event.epoch <= 0 || event.commandId <= 0) return state
        if (state.phase != BrowserPhase.IDLE) {
            val currentEpoch = state.epoch ?: return state
            if (event.epoch <= currentEpoch) return state
        }
        return state.advance(
            phase = BrowserPhase.READY,
            epoch = event.epoch,
            navigationId = event.commandId,
            lastAcceptedCommandId = event.commandId,
            address = null,
            title = null,
            progressPercent = 0,
            canGoBack = false,
            canGoForward = false,
            failure = null,
        )
    }

    private fun navigationAccepted(state: BrowserState, event: BrowserStateEvent.NavigationAccepted): BrowserState {
        if (!isCurrent(state, event.epoch) || event.commandId <= state.lastAcceptedCommandId) return state
        if (state.phase !in
            setOf(BrowserPhase.OPENING, BrowserPhase.LOADING, BrowserPhase.READY, BrowserPhase.ERROR)
        ) {
            return state
        }
        return state.advance(
            phase = BrowserPhase.LOADING,
            navigationId = event.commandId,
            lastAcceptedCommandId = event.commandId,
            address = event.address,
            title = null,
            progressPercent = 0,
            canGoBack = false,
            canGoForward = false,
            failure = null,
        )
    }

    private fun progress(state: BrowserState, event: BrowserStateEvent.Progress): BrowserState {
        if (!isCurrentNavigation(state, event.epoch, event.navigationId) || event.percent !in 0..100) return state
        if (state.phase !in setOf(BrowserPhase.OPENING, BrowserPhase.LOADING)) return state
        return state.advance(progressPercent = event.percent)
    }

    private fun title(state: BrowserState, event: BrowserStateEvent.Title): BrowserState {
        if (!isCurrentNavigation(state, event.epoch, event.navigationId)) return state
        if (state.phase !in setOf(BrowserPhase.OPENING, BrowserPhase.LOADING, BrowserPhase.READY)) return state
        val safeTitle = BrowserPageTitle.fromPage(event.title) ?: return state
        return state.advance(title = safeTitle)
    }

    private fun pageFinished(state: BrowserState, event: BrowserStateEvent.PageFinished): BrowserState {
        if (!isCurrentNavigation(state, event.epoch, event.navigationId)) return state
        if (state.phase !in setOf(BrowserPhase.OPENING, BrowserPhase.LOADING)) return state
        return state.advance(
            phase = BrowserPhase.READY,
            // WebView often fires onPageFinished before onProgressChanged reaches 100. Leaving the
            // last mid-load percent on the wire made Windows show "Loading… 21%" after Loaded.
            progressPercent = 100,
            canGoBack = event.canGoBack,
            canGoForward = event.canGoForward,
        )
    }

    private fun failed(state: BrowserState, event: BrowserStateEvent.Failed): BrowserState {
        if (!isCurrentNavigation(state, event.epoch, event.navigationId)) return state
        if (state.phase in setOf(BrowserPhase.IDLE, BrowserPhase.CLOSING)) return state
        return state.advance(phase = BrowserPhase.ERROR, failure = event.failure)
    }

    private fun closeAccepted(state: BrowserState, event: BrowserStateEvent.CloseAccepted): BrowserState {
        if (!isCurrent(state, event.epoch) || event.commandId <= state.lastAcceptedCommandId) return state
        if (state.phase == BrowserPhase.IDLE || state.phase == BrowserPhase.CLOSING) return state
        return state.advance(phase = BrowserPhase.CLOSING, lastAcceptedCommandId = event.commandId)
    }

    private fun controlAccepted(state: BrowserState, event: BrowserStateEvent.ControlAccepted): BrowserState {
        if (!isCurrent(state, event.epoch) || event.commandId <= state.lastAcceptedCommandId) return state
        if (state.phase == BrowserPhase.IDLE || state.phase == BrowserPhase.CLOSING) return state
        return state.advance(lastAcceptedCommandId = event.commandId)
    }

    private fun blankReset(state: BrowserState, event: BrowserStateEvent.BlankReset): BrowserState {
        if (!isCurrent(state, event.epoch) || event.commandId <= state.lastAcceptedCommandId) return state
        if (state.phase == BrowserPhase.IDLE || state.phase == BrowserPhase.CLOSING) return state
        return state.advance(
            phase = BrowserPhase.READY,
            navigationId = event.commandId,
            lastAcceptedCommandId = event.commandId,
            address = null,
            title = null,
            progressPercent = 0,
            canGoBack = false,
            canGoForward = false,
            failure = null,
        )
    }

    private fun closed(state: BrowserState, event: BrowserStateEvent.Closed): BrowserState {
        if (!isCurrent(state, event.epoch) || state.phase != BrowserPhase.CLOSING) return state
        if (event.commandId != state.lastAcceptedCommandId) return state
        return BrowserState(
            revision = nextRevision(state.revision) ?: return state,
            lastAcceptedCommandId = event.commandId,
        )
    }

    private fun isCurrent(state: BrowserState, epoch: Long): Boolean = epoch > 0 && state.epoch == epoch

    private fun isCurrentNavigation(state: BrowserState, epoch: Long, navigationId: Long): Boolean =
        isCurrent(state, epoch) && navigationId > 0 && state.navigationId == navigationId

    private fun BrowserState.advance(
        phase: BrowserPhase = this.phase,
        epoch: Long? = this.epoch,
        navigationId: Long = this.navigationId,
        lastAcceptedCommandId: Long = this.lastAcceptedCommandId,
        address: BrowserAddress? = this.address,
        title: BrowserPageTitle? = this.title,
        progressPercent: Int = this.progressPercent,
        canGoBack: Boolean = this.canGoBack,
        canGoForward: Boolean = this.canGoForward,
        failure: BrowserFailure? = this.failure,
    ): BrowserState {
        val revision = nextRevision(revision) ?: return this
        return copy(
            phase = phase,
            revision = revision,
            epoch = epoch,
            navigationId = navigationId,
            lastAcceptedCommandId = lastAcceptedCommandId,
            address = address,
            title = title,
            progressPercent = progressPercent,
            canGoBack = canGoBack,
            canGoForward = canGoForward,
            failure = failure,
        )
    }

    private fun nextRevision(current: Long): Long? = if (current == Long.MAX_VALUE) null else current + 1
}
