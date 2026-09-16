package com.rextechnologies.flint.receiver.browser.workspace

import com.rextechnologies.flint.receiver.browser.BrowserUrlPolicy
import com.rextechnologies.flint.receiver.browser.BrowserUrlResult

/** Intents and host callbacks understood by [BrowserWorkspaceReducer]. */
sealed interface BrowserWorkspaceAction {
    /** Switches authority. A saved snapshot is accepted only for its matching local TV profile. */
    data class ActivateProfile(
        val profile: BrowserWorkspaceProfile,
        val snapshot: BrowserWorkspaceSnapshot? = null,
    ) : BrowserWorkspaceAction

    /** Clears the active device-owned projection only when this exact connection goes away. */
    data class DisconnectDevice(val connectionId: Long) : BrowserWorkspaceAction

    /** Leaves the profile picker / browser surface. Local state is persisted; device state is wiped. */
    data object DeactivateProfile : BrowserWorkspaceAction

    /** The Activity host changed; all renderer instances must be recreated with new guards. */
    data object RebuildRenderers : BrowserWorkspaceAction

    data class OpenPane(val url: String? = null) : BrowserWorkspaceAction
    data class ClosePane(val paneId: Long) : BrowserWorkspaceAction
    data class FocusPane(val paneId: Long) : BrowserWorkspaceAction
    data class MovePane(val paneId: Long, val targetSlot: Int) : BrowserWorkspaceAction
    data class SetSplit(val split: BrowserWorkspaceSplit) : BrowserWorkspaceAction
    data class SetLayout(val layout: BrowserWorkspaceLayout) : BrowserWorkspaceAction
    data class SetInteractionMode(val mode: BrowserWorkspaceInteractionMode) : BrowserWorkspaceAction

    /** Starts an app-originated navigation in an already resident WebView. */
    data class NavigatePane(val paneId: Long, val url: String) : BrowserWorkspaceAction
    data class ReloadPane(val paneId: Long) : BrowserWorkspaceAction
    data class GoBack(val paneId: Long) : BrowserWorkspaceAction
    data class GoForward(val paneId: Long) : BrowserWorkspaceAction

    /** A tagged callback from a WebView. Older generations and navigations are ignored. */
    data class PageStateReported(
        val paneId: Long,
        val rendererGeneration: Long,
        val page: BrowserWorkspacePage,
    ) : BrowserWorkspaceAction

    data class RendererFailed(
        val paneId: Long,
        val rendererGeneration: Long,
        val reason: BrowserWorkspaceRendererFailure,
    ) : BrowserWorkspaceAction

    /** Page-requested custom-view fullscreen. It remains scoped to this pane. */
    data class SetPageFullscreen(
        val paneId: Long,
        val rendererGeneration: Long,
        val active: Boolean,
    ) : BrowserWorkspaceAction

    /** Explicit UI theater mode; never inferred from an HTML video fullscreen callback. */
    data class EnterTheaterMode(val paneId: Long) : BrowserWorkspaceAction
    data object ExitTheaterMode : BrowserWorkspaceAction

    /** Best-effort only: the reducer does not alter observed playback in response to this action. */
    data class RequestMediaPlayPause(val paneId: Long) : BrowserWorkspaceAction
    data class SetPaneMuted(val paneId: Long, val muted: Boolean) : BrowserWorkspaceAction
    data class PlaybackRequestCompleted(
        val paneId: Long,
        val rendererGeneration: Long,
        val requestId: Long,
        val result: BrowserWorkspaceMediaDispatchResult,
    ) : BrowserWorkspaceAction

    data class PlaybackObserved(
        val paneId: Long,
        val rendererGeneration: Long,
        val observation: BrowserWorkspacePlaybackObservation,
    ) : BrowserWorkspaceAction

    data class MuteApplicationCompleted(
        val paneId: Long,
        val rendererGeneration: Long,
        val requestId: Long,
        val result: BrowserWorkspaceMuteResult,
    ) : BrowserWorkspaceAction
}

/** Why a user-facing operation could not be safely completed. */
enum class BrowserWorkspaceRefusal {
    NO_ACTIVE_PROFILE,
    PANE_CAPACITY_REACHED,
    UNKNOWN_PANE,
    EXCLUSIVE_PRESENTATION_ACTIVE,
    RENDERER_CAPACITY_BLOCKED,
    RENDERER_NOT_RESIDENT,
    LAYOUT_NOT_AVAILABLE,
    LAYOUT_DOES_NOT_FIT_PANES,
    INVALID_SLOT,
    PANE_ID_EXHAUSTED,
    MEDIA_REQUEST_ID_EXHAUSTED,
    INVALID_SNAPSHOT,
    INVALID_URL,
    SNAPSHOT_EXCEEDS_CAPACITY,
    DEVICE_PROFILE_CANNOT_RESTORE_SNAPSHOT,
}

/** Why a live renderer is being released. The host must finish this effect before a later create. */
enum class BrowserWorkspaceRendererReleaseReason {
    LIVE_RENDERER_CAP,
    PANE_CLOSED,
    PROFILE_TORN_DOWN,
}

/**
 * Work for the Android binding. Effects are ordered: in particular a [FreezeRenderer] precedes a
 * later [CreateRenderer] or [RestoreRenderer] when the live-renderer cap would otherwise be
 * exceeded.
 */
sealed interface BrowserWorkspaceEffect {
    data class CreateRenderer(
        val paneId: Long,
        val rendererGeneration: Long,
        val initialUrl: String?,
        val slot: Int,
    ) : BrowserWorkspaceEffect

    data class RestoreRenderer(
        val paneId: Long,
        val rendererGeneration: Long,
        val url: String,
        val slot: Int,
    ) : BrowserWorkspaceEffect

    data class FreezeRenderer(
        val paneId: Long,
        val rendererGeneration: Long,
        val reason: BrowserWorkspaceRendererReleaseReason,
    ) : BrowserWorkspaceEffect

    /** Drops a renderer and any host-kept saved state, even when the pane was already suspended. */
    data class DestroyPane(val paneId: Long, val rendererGeneration: Long) : BrowserWorkspaceEffect

    data class NavigateRenderer(
        val paneId: Long,
        val rendererGeneration: Long,
        val navigationId: Long,
        val url: String,
    ) : BrowserWorkspaceEffect

    data class ReloadRenderer(
        val paneId: Long,
        val rendererGeneration: Long,
        val navigationId: Long,
    ) : BrowserWorkspaceEffect

    data class GoBackRenderer(
        val paneId: Long,
        val rendererGeneration: Long,
        val navigationId: Long,
    ) : BrowserWorkspaceEffect

    data class GoForwardRenderer(
        val paneId: Long,
        val rendererGeneration: Long,
        val navigationId: Long,
    ) : BrowserWorkspaceEffect

    data class FocusChanged(val paneId: Long) : BrowserWorkspaceEffect
    data class LayoutChanged(
        val layout: BrowserWorkspaceLayout,
        val paneIdsInSlotOrder: List<Long>,
    ) : BrowserWorkspaceEffect

    data class InteractionModeChanged(val mode: BrowserWorkspaceInteractionMode) : BrowserWorkspaceEffect
    data class PageFullscreenChanged(val paneId: Long, val active: Boolean) : BrowserWorkspaceEffect
    data class TheaterModeChanged(val paneId: Long?) : BrowserWorkspaceEffect

    data class RequestMediaPlayPause(
        val paneId: Long,
        val rendererGeneration: Long,
        val requestId: Long,
    ) : BrowserWorkspaceEffect

    /** Applies WebView-local mute only; this must never call global Android audio mute. */
    data class ApplyPaneMute(
        val paneId: Long,
        val rendererGeneration: Long,
        val requestId: Long,
        val muted: Boolean,
    ) : BrowserWorkspaceEffect

    data class RendererFailureObserved(
        val paneId: Long,
        val reason: BrowserWorkspaceRendererFailure,
    ) : BrowserWorkspaceEffect

    data class PersistLocalWorkspace(
        val profileId: String,
        val snapshot: BrowserWorkspaceSnapshot,
    ) : BrowserWorkspaceEffect

    /** Purges the session-only projection after its renderers have been destroyed. */
    data class DiscardEphemeralWorkspace(val connectionId: Long) : BrowserWorkspaceEffect

    data class ProfileChanged(val profile: BrowserWorkspaceProfile?) : BrowserWorkspaceEffect
    data class Refused(val reason: BrowserWorkspaceRefusal) : BrowserWorkspaceEffect
}

data class BrowserWorkspaceTransition(
    val state: BrowserWorkspaceState,
    val effects: List<BrowserWorkspaceEffect>,
)

/**
 * Pure state machine for a browser workspace.
 *
 * The reducer never creates a WebView, navigates a page, touches profile storage, or infers media
 * playback. Those actions are all represented by ordered [BrowserWorkspaceEffect]s, which keeps
 * hardware-dependent work at the Android host boundary and makes the policy exhaustively testable.
 */
class BrowserWorkspaceReducer(
    val capacity: BrowserWorkspaceCapacity = BrowserWorkspaceCapacity.CONSERVATIVE_FIRE_TV,
) {
    fun reduce(
        state: BrowserWorkspaceState,
        action: BrowserWorkspaceAction,
    ): BrowserWorkspaceTransition = when (action) {
        is BrowserWorkspaceAction.ActivateProfile -> activateProfile(state, action)
        is BrowserWorkspaceAction.DisconnectDevice -> disconnectDevice(state, action.connectionId)
        BrowserWorkspaceAction.DeactivateProfile -> deactivateProfile(state)
        BrowserWorkspaceAction.RebuildRenderers -> rebuildRenderers(state)
        is BrowserWorkspaceAction.OpenPane -> openPane(state, action.url)
        is BrowserWorkspaceAction.ClosePane -> closePane(state, action.paneId)
        is BrowserWorkspaceAction.FocusPane -> focusPane(state, action.paneId)
        is BrowserWorkspaceAction.MovePane -> movePane(state, action.paneId, action.targetSlot)
        is BrowserWorkspaceAction.SetSplit -> setSplit(state, action.split)
        is BrowserWorkspaceAction.SetLayout -> setLayout(state, action.layout)
        is BrowserWorkspaceAction.SetInteractionMode -> setInteractionMode(state, action.mode)
        is BrowserWorkspaceAction.NavigatePane -> navigatePane(state, action.paneId, action.url)
        is BrowserWorkspaceAction.ReloadPane -> reloadPane(state, action.paneId)
        is BrowserWorkspaceAction.GoBack -> goBack(state, action.paneId)
        is BrowserWorkspaceAction.GoForward -> goForward(state, action.paneId)
        is BrowserWorkspaceAction.PageStateReported -> pageStateReported(state, action)
        is BrowserWorkspaceAction.RendererFailed -> rendererFailed(state, action)
        is BrowserWorkspaceAction.SetPageFullscreen -> setPageFullscreen(state, action)
        is BrowserWorkspaceAction.EnterTheaterMode -> enterTheaterMode(state, action.paneId)
        BrowserWorkspaceAction.ExitTheaterMode -> exitTheaterMode(state)
        is BrowserWorkspaceAction.RequestMediaPlayPause -> requestMediaPlayPause(state, action.paneId)
        is BrowserWorkspaceAction.SetPaneMuted -> setPaneMuted(state, action.paneId, action.muted)
        is BrowserWorkspaceAction.PlaybackRequestCompleted -> playbackRequestCompleted(state, action)
        is BrowserWorkspaceAction.PlaybackObserved -> playbackObserved(state, action)
        is BrowserWorkspaceAction.MuteApplicationCompleted -> muteApplicationCompleted(state, action)
    }

    private fun activateProfile(
        state: BrowserWorkspaceState,
        action: BrowserWorkspaceAction.ActivateProfile,
    ): BrowserWorkspaceTransition {
        if (state.profile == action.profile) return unchanged(state)
        if (action.profile is BrowserWorkspaceProfile.ConnectedDevice && action.snapshot != null) {
            return refuse(state, BrowserWorkspaceRefusal.DEVICE_PROFILE_CANNOT_RESTORE_SNAPSHOT)
        }

        val incoming = when (val profile = action.profile) {
            is BrowserWorkspaceProfile.LocalTv -> if (action.snapshot != null) {
                restoreLocalSnapshot(profile, action.snapshot)
            } else {
                BrowserWorkspaceState(profile = profile)
            }
            is BrowserWorkspaceProfile.ConnectedDevice -> BrowserWorkspaceState(profile = profile)
        } ?: return refuse(
            state,
            if (action.snapshot?.panes?.size ?: 0 > capacity.maxOpenPanes) {
                BrowserWorkspaceRefusal.SNAPSHOT_EXCEEDS_CAPACITY
            } else {
                BrowserWorkspaceRefusal.INVALID_SNAPSHOT
            },
        )

        val effects = teardownEffects(state).toMutableList()
        var next = incoming
        val restoreOrder = next.panes.sortedBy { if (it.id == next.focusedPaneId) 0 else 1 }
        for (pane in restoreOrder.take(capacity.maxLiveRenderers).reversed()) {
            val residence = makePaneLive(next, pane.id) ?: continue
            next = residence.state
            effects += residence.effects
        }
        next.focusedPane?.let { effects += BrowserWorkspaceEffect.FocusChanged(it.id) }
        effects += BrowserWorkspaceEffect.LayoutChanged(next.layout, next.paneIdsInSlotOrder)
        effects += BrowserWorkspaceEffect.ProfileChanged(next.profile)
        return BrowserWorkspaceTransition(next, effects)
    }

    private fun disconnectDevice(state: BrowserWorkspaceState, connectionId: Long): BrowserWorkspaceTransition {
        val device = state.profile as? BrowserWorkspaceProfile.ConnectedDevice
        if (device?.connectionId != connectionId) return unchanged(state)
        val effects = teardownEffects(state).toMutableList()
        effects += BrowserWorkspaceEffect.ProfileChanged(null)
        return BrowserWorkspaceTransition(BrowserWorkspaceState(), effects)
    }

    private fun rebuildRenderers(state: BrowserWorkspaceState): BrowserWorkspaceTransition {
        var next = state.copy(
            pageFullscreenPaneId = null,
            interactionMode = BrowserWorkspaceInteractionMode.WORKSPACE_CHROME,
        )
        val effects = mutableListOf<BrowserWorkspaceEffect>()
        for (pane in state.panes.filter { it.rendererResidency.hasRenderer }) {
            next = next.withPane(pane.id) {
                it.copy(
                    rendererResidency = BrowserWorkspaceRendererResidency.SUSPENDED,
                    media = BrowserWorkspaceMediaState(desiredMuted = it.media.desiredMuted),
                )
            }
            val residence = makePaneLive(next, pane.id) ?: continue
            next = residence.state
            effects += residence.effects
        }
        return BrowserWorkspaceTransition(next, effects)
    }

    private fun deactivateProfile(state: BrowserWorkspaceState): BrowserWorkspaceTransition {
        if (state.profile == null && state.panes.isEmpty()) return unchanged(state)
        val effects = teardownEffects(state).toMutableList()
        effects += BrowserWorkspaceEffect.ProfileChanged(null)
        return BrowserWorkspaceTransition(BrowserWorkspaceState(), effects)
    }

    private fun openPane(state: BrowserWorkspaceState, url: String?): BrowserWorkspaceTransition {
        if (state.profile == null) return refuse(state, BrowserWorkspaceRefusal.NO_ACTIVE_PROFILE)
        if (!url.isNullOrEmpty() && BrowserUrlPolicy().evaluate(url) !is BrowserUrlResult.Accepted) {
            return refuse(state, BrowserWorkspaceRefusal.INVALID_URL)
        }
        if (state.panes.size >=
            capacity.maxOpenPanes
        ) {
            return refuse(state, BrowserWorkspaceRefusal.PANE_CAPACITY_REACHED)
        }
        if (hasExclusivePresentation(state)) return refuse(state, BrowserWorkspaceRefusal.EXCLUSIVE_PRESENTATION_ACTIVE)
        if (state.nextPaneId <= BrowserWorkspaceState.NO_PANE_ID || state.nextPaneId == Long.MAX_VALUE) {
            return refuse(state, BrowserWorkspaceRefusal.PANE_ID_EXHAUSTED)
        }

        val id = state.nextPaneId
        val pane = BrowserWorkspacePane(
            id = id,
            slot = state.panes.size,
            page = BrowserWorkspacePage(url = url.orEmpty()),
            rendererResidency = BrowserWorkspaceRendererResidency.LIVE,
            rendererGeneration = 1,
        )
        var next = state.copy(
            panes = state.panes + pane,
            layout = layoutForCount(state.panes.size + 1, state.layout),
            focusedPaneId = id,
            interactionMode = BrowserWorkspaceInteractionMode.WORKSPACE_CHROME,
            nextPaneId = id + 1,
        )
        val budget = enforceLiveBudget(next, keepPaneId = id)
            ?: return refuse(state, BrowserWorkspaceRefusal.RENDERER_CAPACITY_BLOCKED)
        next = budget.state.touch(id)
        val effects = budget.effects.toMutableList()
        effects += BrowserWorkspaceEffect.CreateRenderer(id, pane.rendererGeneration, url, pane.slot)
        effects += BrowserWorkspaceEffect.LayoutChanged(next.layout, next.paneIdsInSlotOrder)
        effects += BrowserWorkspaceEffect.FocusChanged(id)
        return BrowserWorkspaceTransition(next, effects)
    }

    private fun closePane(state: BrowserWorkspaceState, paneId: Long): BrowserWorkspaceTransition {
        val closing = state.pane(paneId) ?: return unchanged(state)
        val index = state.panes.indexOfFirst { it.id == paneId }
        val effects = mutableListOf<BrowserWorkspaceEffect>()
        var next = state
        if (state.pageFullscreenPaneId == paneId) {
            next = next.copy(pageFullscreenPaneId = null)
            effects += BrowserWorkspaceEffect.PageFullscreenChanged(paneId, active = false)
        }
        if (state.theaterPaneId == paneId) {
            next = next.copy(theaterPaneId = null)
            effects += BrowserWorkspaceEffect.TheaterModeChanged(null)
        }
        effects += BrowserWorkspaceEffect.DestroyPane(closing.id, closing.rendererGeneration)

        val remaining = next.panes.filterNot { it.id == paneId }.compactSlots()
        if (remaining.isEmpty()) {
            next = next.copy(
                panes = emptyList(),
                layout = BrowserWorkspaceLayout.SINGLE,
                focusedPaneId = BrowserWorkspaceState.NO_PANE_ID,
                interactionMode = BrowserWorkspaceInteractionMode.WORKSPACE_CHROME,
                pageFullscreenPaneId = null,
                theaterPaneId = null,
                rendererRecency = next.rendererRecency.filterNot { it == paneId },
            )
            effects += BrowserWorkspaceEffect.LayoutChanged(next.layout, emptyList())
            return BrowserWorkspaceTransition(next, effects)
        }

        val successor = if (state.focusedPaneId == paneId) {
            remaining.getOrNull(index) ?: remaining.last()
        } else {
            null
        }
        next = next.copy(
            panes = remaining,
            layout = layoutForCount(remaining.size, next.layout),
            rendererRecency = next.rendererRecency.filterNot { it == paneId },
        )
        if (successor != null) {
            next = next.copy(
                focusedPaneId = successor.id,
                interactionMode = BrowserWorkspaceInteractionMode.WORKSPACE_CHROME,
            )
            val residence = makePaneLive(next, successor.id)
                ?: return BrowserWorkspaceTransition(
                    next,
                    effects + BrowserWorkspaceEffect.Refused(BrowserWorkspaceRefusal.RENDERER_CAPACITY_BLOCKED),
                )
            next = residence.state
            effects += residence.effects
            effects += BrowserWorkspaceEffect.FocusChanged(successor.id)
        }
        effects += BrowserWorkspaceEffect.LayoutChanged(next.layout, next.paneIdsInSlotOrder)
        return BrowserWorkspaceTransition(next, effects)
    }

    private fun focusPane(state: BrowserWorkspaceState, paneId: Long): BrowserWorkspaceTransition {
        val target = state.pane(paneId) ?: return refuse(state, BrowserWorkspaceRefusal.UNKNOWN_PANE)
        if (state.focusedPaneId == paneId && target.rendererResidency.hasRenderer &&
            state.interactionMode == BrowserWorkspaceInteractionMode.WORKSPACE_CHROME
        ) {
            return unchanged(state)
        }
        if (hasExclusivePresentation(state) && state.focusedPaneId != paneId) {
            return refuse(state, BrowserWorkspaceRefusal.EXCLUSIVE_PRESENTATION_ACTIVE)
        }

        var next = state.copy(
            focusedPaneId = paneId,
            interactionMode = BrowserWorkspaceInteractionMode.WORKSPACE_CHROME,
        )
        val residence = makePaneLive(next, target.id)
            ?: return refuse(state, BrowserWorkspaceRefusal.RENDERER_CAPACITY_BLOCKED)
        next = residence.state
        val effects = residence.effects.toMutableList()
        effects += BrowserWorkspaceEffect.FocusChanged(paneId)
        return BrowserWorkspaceTransition(next, effects)
    }

    private fun movePane(state: BrowserWorkspaceState, paneId: Long, targetSlot: Int): BrowserWorkspaceTransition {
        if (state.pane(paneId) == null) return refuse(state, BrowserWorkspaceRefusal.UNKNOWN_PANE)
        if (targetSlot !in state.panes.indices) return refuse(state, BrowserWorkspaceRefusal.INVALID_SLOT)
        if (hasExclusivePresentation(state)) return refuse(state, BrowserWorkspaceRefusal.EXCLUSIVE_PRESENTATION_ACTIVE)
        val sourceSlot = state.panes.indexOfFirst { it.id == paneId }
        if (sourceSlot == targetSlot) return unchanged(state)
        val reordered = state.panes.toMutableList().also { panes ->
            val pane = panes.removeAt(sourceSlot)
            panes.add(targetSlot, pane)
        }.mapIndexed { index, pane -> pane.copy(slot = index) }
        val next = state.copy(panes = reordered)
        return BrowserWorkspaceTransition(
            next,
            listOf(BrowserWorkspaceEffect.LayoutChanged(next.layout, next.paneIdsInSlotOrder)),
        )
    }

    private fun setSplit(state: BrowserWorkspaceState, split: BrowserWorkspaceSplit): BrowserWorkspaceTransition {
        if (hasExclusivePresentation(state)) return refuse(state, BrowserWorkspaceRefusal.EXCLUSIVE_PRESENTATION_ACTIVE)
        if (state.profile == null || state.split == split) return unchanged(state)
        val next = state.copy(split = split)
        return BrowserWorkspaceTransition(
            next,
            listOf(BrowserWorkspaceEffect.LayoutChanged(next.layout, next.paneIdsInSlotOrder)),
        )
    }

    private fun setLayout(state: BrowserWorkspaceState, layout: BrowserWorkspaceLayout): BrowserWorkspaceTransition {
        if (!capacity.allows(layout, state.panes.size)) {
            return refuse(
                state,
                if (layout !in capacity.allowedLayouts) {
                    BrowserWorkspaceRefusal.LAYOUT_NOT_AVAILABLE
                } else {
                    BrowserWorkspaceRefusal.LAYOUT_DOES_NOT_FIT_PANES
                },
            )
        }
        if (hasExclusivePresentation(state)) return refuse(state, BrowserWorkspaceRefusal.EXCLUSIVE_PRESENTATION_ACTIVE)
        if (state.layout == layout) return unchanged(state)
        val next = state.copy(layout = layout)
        return BrowserWorkspaceTransition(
            next,
            listOf(BrowserWorkspaceEffect.LayoutChanged(next.layout, next.paneIdsInSlotOrder)),
        )
    }

    private fun setInteractionMode(
        state: BrowserWorkspaceState,
        mode: BrowserWorkspaceInteractionMode,
    ): BrowserWorkspaceTransition {
        if (state.interactionMode == mode) return unchanged(state)
        if (mode == BrowserWorkspaceInteractionMode.PAGE && state.focusedPane?.rendererResidency?.hasRenderer != true) {
            return refuse(state, BrowserWorkspaceRefusal.RENDERER_NOT_RESIDENT)
        }
        val next = state.copy(interactionMode = mode)
        return BrowserWorkspaceTransition(next, listOf(BrowserWorkspaceEffect.InteractionModeChanged(mode)))
    }

    private fun navigatePane(state: BrowserWorkspaceState, paneId: Long, url: String): BrowserWorkspaceTransition {
        val pane = state.pane(paneId) ?: return refuse(state, BrowserWorkspaceRefusal.UNKNOWN_PANE)
        if (BrowserUrlPolicy().evaluate(
                url,
            ) !is BrowserUrlResult.Accepted
        ) {
            return refuse(state, BrowserWorkspaceRefusal.INVALID_URL)
        }
        if (!pane.rendererResidency.hasRenderer) return refuse(state, BrowserWorkspaceRefusal.RENDERER_NOT_RESIDENT)
        val navigation =
            beginNavigation(state, paneId, url) ?: return refuse(state, BrowserWorkspaceRefusal.PANE_ID_EXHAUSTED)
        return BrowserWorkspaceTransition(
            navigation.state,
            listOf(
                BrowserWorkspaceEffect.NavigateRenderer(paneId, pane.rendererGeneration, navigation.navigationId, url),
            ),
        )
    }

    private fun reloadPane(state: BrowserWorkspaceState, paneId: Long): BrowserWorkspaceTransition {
        val pane = state.pane(paneId) ?: return refuse(state, BrowserWorkspaceRefusal.UNKNOWN_PANE)
        if (!pane.rendererResidency.hasRenderer) return refuse(state, BrowserWorkspaceRefusal.RENDERER_NOT_RESIDENT)
        val navigation =
            beginNavigation(state, paneId, pane.page.url)
                ?: return refuse(state, BrowserWorkspaceRefusal.PANE_ID_EXHAUSTED)
        return BrowserWorkspaceTransition(
            navigation.state,
            listOf(BrowserWorkspaceEffect.ReloadRenderer(paneId, pane.rendererGeneration, navigation.navigationId)),
        )
    }

    private fun goBack(state: BrowserWorkspaceState, paneId: Long): BrowserWorkspaceTransition {
        val pane = state.pane(paneId) ?: return refuse(state, BrowserWorkspaceRefusal.UNKNOWN_PANE)
        if (!pane.rendererResidency.hasRenderer) return refuse(state, BrowserWorkspaceRefusal.RENDERER_NOT_RESIDENT)
        if (!pane.page.canGoBack) return unchanged(state)
        val navigation =
            beginNavigation(state, paneId, pane.page.url)
                ?: return refuse(state, BrowserWorkspaceRefusal.PANE_ID_EXHAUSTED)
        return BrowserWorkspaceTransition(
            navigation.state,
            listOf(BrowserWorkspaceEffect.GoBackRenderer(paneId, pane.rendererGeneration, navigation.navigationId)),
        )
    }

    private fun goForward(state: BrowserWorkspaceState, paneId: Long): BrowserWorkspaceTransition {
        val pane = state.pane(paneId) ?: return refuse(state, BrowserWorkspaceRefusal.UNKNOWN_PANE)
        if (!pane.rendererResidency.hasRenderer) return refuse(state, BrowserWorkspaceRefusal.RENDERER_NOT_RESIDENT)
        if (!pane.page.canGoForward) return unchanged(state)
        val navigation =
            beginNavigation(state, paneId, pane.page.url)
                ?: return refuse(state, BrowserWorkspaceRefusal.PANE_ID_EXHAUSTED)
        return BrowserWorkspaceTransition(
            navigation.state,
            listOf(BrowserWorkspaceEffect.GoForwardRenderer(paneId, pane.rendererGeneration, navigation.navigationId)),
        )
    }

    private fun beginNavigation(
        state: BrowserWorkspaceState,
        paneId: Long,
        url: String,
    ): NavigationStart? {
        val pane = state.pane(paneId) ?: return null
        val navigationId = pane.page.navigationId.nextPositiveOrNull() ?: return null
        return NavigationStart(
            state = state.withPane(paneId) {
                it.copy(
                    page = it.page.copy(url = url, loading = true, progressPercent = 0, navigationId = navigationId),
                )
            },
            navigationId = navigationId,
        )
    }

    private fun pageStateReported(
        state: BrowserWorkspaceState,
        action: BrowserWorkspaceAction.PageStateReported,
    ): BrowserWorkspaceTransition {
        val pane = state.pane(action.paneId) ?: return unchanged(state)
        if (!pane.rendererResidency.hasRenderer ||
            pane.rendererGeneration != action.rendererGeneration
        ) {
            return unchanged(state)
        }
        // A callback from an older navigation must never overwrite newly requested page chrome.
        if (action.page.navigationId < pane.page.navigationId) return unchanged(state)
        val normalized = action.page.copy(
            progressPercent = action.page.progressPercent.coerceIn(0, 100),
            navigationId = action.page.navigationId.coerceAtLeast(pane.page.navigationId),
        )
        return BrowserWorkspaceTransition(state.withPane(action.paneId) { it.copy(page = normalized) }, emptyList())
    }

    private fun rendererFailed(
        state: BrowserWorkspaceState,
        action: BrowserWorkspaceAction.RendererFailed,
    ): BrowserWorkspaceTransition {
        val pane = state.pane(action.paneId) ?: return unchanged(state)
        val stale = pane.rendererGeneration != action.rendererGeneration || !pane.rendererResidency.hasRenderer
        if (stale) return unchanged(state)
        var next = state.withPane(action.paneId) {
            it.copy(
                rendererResidency = BrowserWorkspaceRendererResidency.FAILED,
                rendererFailure = action.reason,
                // Dying mid-navigation, its last page report is dropped as stale; left alone, Windows
                // shows a stopped pane as loading at 0% for as long as it stays open.
                page = it.page.copy(loading = false, progressPercent = 0),
                media = it.media.copy(
                    muteApplication = if (it.media.desiredMuted) {
                        BrowserWorkspaceMuteApplication.PENDING_RENDERER
                    } else {
                        BrowserWorkspaceMuteApplication.NOT_REQUESTED
                    },
                    pendingMuteRequestId = null,
                ),
            )
        }
        val effects = mutableListOf<BrowserWorkspaceEffect>(
            BrowserWorkspaceEffect.DestroyPane(action.paneId, action.rendererGeneration),
            BrowserWorkspaceEffect.RendererFailureObserved(action.paneId, action.reason),
        )
        if (next.pageFullscreenPaneId == action.paneId) {
            next = next.copy(pageFullscreenPaneId = null)
            effects += BrowserWorkspaceEffect.PageFullscreenChanged(action.paneId, active = false)
        }
        if (next.theaterPaneId == action.paneId) {
            next = next.copy(theaterPaneId = null)
            effects += BrowserWorkspaceEffect.TheaterModeChanged(null)
        }
        if (next.focusedPaneId == action.paneId && next.interactionMode == BrowserWorkspaceInteractionMode.PAGE) {
            next = next.copy(interactionMode = BrowserWorkspaceInteractionMode.WORKSPACE_CHROME)
            effects += BrowserWorkspaceEffect.InteractionModeChanged(BrowserWorkspaceInteractionMode.WORKSPACE_CHROME)
        }
        return BrowserWorkspaceTransition(next, effects)
    }

    private fun setPageFullscreen(
        state: BrowserWorkspaceState,
        action: BrowserWorkspaceAction.SetPageFullscreen,
    ): BrowserWorkspaceTransition {
        val pane = state.pane(action.paneId) ?: return unchanged(state)
        if (!pane.rendererResidency.hasRenderer ||
            pane.rendererGeneration != action.rendererGeneration
        ) {
            return unchanged(state)
        }
        if (!action.active) {
            if (state.pageFullscreenPaneId != action.paneId) return unchanged(state)
            val next = state.copy(pageFullscreenPaneId = null)
            return BrowserWorkspaceTransition(
                next,
                listOf(BrowserWorkspaceEffect.PageFullscreenChanged(action.paneId, active = false)),
            )
        }
        if (state.pageFullscreenPaneId == action.paneId) return unchanged(state)
        if (state.focusedPaneId != action.paneId || state.pageFullscreenPaneId != null) {
            return refuse(state, BrowserWorkspaceRefusal.EXCLUSIVE_PRESENTATION_ACTIVE)
        }

        var next = state.copy(
            pageFullscreenPaneId = action.paneId,
            interactionMode = BrowserWorkspaceInteractionMode.PAGE,
        )
        val effects = mutableListOf<BrowserWorkspaceEffect>()
        state.pageFullscreenPaneId?.let { prior ->
            effects += BrowserWorkspaceEffect.PageFullscreenChanged(prior, active = false)
        }
        // Theater mode is an explicit workspace presentation. A page callback must not silently
        // turn it into global fullscreen, so leave theater before showing pane-local fullscreen.
        if (state.theaterPaneId != null) {
            next = next.copy(theaterPaneId = null)
            effects += BrowserWorkspaceEffect.TheaterModeChanged(null)
        }
        effects += BrowserWorkspaceEffect.PageFullscreenChanged(action.paneId, active = true)
        return BrowserWorkspaceTransition(next, effects)
    }

    private fun enterTheaterMode(state: BrowserWorkspaceState, paneId: Long): BrowserWorkspaceTransition {
        val pane = state.pane(paneId) ?: return refuse(state, BrowserWorkspaceRefusal.UNKNOWN_PANE)
        if (!pane.rendererResidency.hasRenderer) return refuse(state, BrowserWorkspaceRefusal.RENDERER_NOT_RESIDENT)
        if (state.pageFullscreenPaneId !=
            null
        ) {
            return refuse(state, BrowserWorkspaceRefusal.EXCLUSIVE_PRESENTATION_ACTIVE)
        }
        if (state.theaterPaneId == paneId) return unchanged(state)
        var next = state.copy(theaterPaneId = paneId, focusedPaneId = paneId)
        val effects = mutableListOf<BrowserWorkspaceEffect>()
        state.panes.filter { it.id != paneId && it.rendererResidency.hasRenderer }.forEach { other ->
            next = next.withPane(other.id) { it.copy(rendererResidency = BrowserWorkspaceRendererResidency.SUSPENDED) }
            effects += BrowserWorkspaceEffect.FreezeRenderer(
                other.id,
                other.rendererGeneration,
                BrowserWorkspaceRendererReleaseReason.LIVE_RENDERER_CAP,
            )
        }
        return BrowserWorkspaceTransition(
            next,
            effects +
                listOf(BrowserWorkspaceEffect.TheaterModeChanged(paneId), BrowserWorkspaceEffect.FocusChanged(paneId)),
        )
    }

    private fun exitTheaterMode(state: BrowserWorkspaceState): BrowserWorkspaceTransition {
        if (state.theaterPaneId == null) return unchanged(state)
        var next = state.copy(theaterPaneId = null)
        val effects = mutableListOf<BrowserWorkspaceEffect>(BrowserWorkspaceEffect.TheaterModeChanged(null))
        for (pane in next.panes.filter { it.id != next.focusedPaneId }) {
            if (next.livePaneCount >= capacity.maxLiveRenderers) break
            val residence = makePaneLive(next, pane.id) ?: continue
            next = residence.state
            effects += residence.effects
        }
        return BrowserWorkspaceTransition(next, effects)
    }

    private fun requestMediaPlayPause(state: BrowserWorkspaceState, paneId: Long): BrowserWorkspaceTransition {
        val pane = state.pane(paneId) ?: return refuse(state, BrowserWorkspaceRefusal.UNKNOWN_PANE)
        if (!pane.rendererResidency.hasRenderer) return refuse(state, BrowserWorkspaceRefusal.RENDERER_NOT_RESIDENT)
        val requestId =
            nextMediaRequestIdOrNull(state) ?: return refuse(state, BrowserWorkspaceRefusal.MEDIA_REQUEST_ID_EXHAUSTED)
        val next = state.withPane(paneId) {
            it.copy(media = it.media.copy(playbackRequest = BrowserWorkspacePlaybackRequest(requestId)))
        }.copy(nextMediaRequestId = requestId + 1)
        return BrowserWorkspaceTransition(
            next,
            listOf(BrowserWorkspaceEffect.RequestMediaPlayPause(paneId, pane.rendererGeneration, requestId)),
        )
    }

    private fun setPaneMuted(state: BrowserWorkspaceState, paneId: Long, muted: Boolean): BrowserWorkspaceTransition {
        val pane = state.pane(paneId) ?: return refuse(state, BrowserWorkspaceRefusal.UNKNOWN_PANE)
        val media = pane.media
        if (media.desiredMuted == muted &&
            (
                media.muteApplication == BrowserWorkspaceMuteApplication.APPLIED_TO_RENDERER ||
                    media.muteApplication == BrowserWorkspaceMuteApplication.REQUESTED ||
                    media.muteApplication == BrowserWorkspaceMuteApplication.NOT_REQUESTED
                )
        ) {
            return unchanged(state)
        }
        if (!pane.rendererResidency.hasRenderer) {
            val next = state.withPane(paneId) {
                it.copy(
                    media = it.media.copy(
                        desiredMuted = muted,
                        muteApplication = if (muted) {
                            BrowserWorkspaceMuteApplication.PENDING_RENDERER
                        } else {
                            BrowserWorkspaceMuteApplication.NOT_REQUESTED
                        },
                        pendingMuteRequestId = null,
                    ),
                )
            }
            return BrowserWorkspaceTransition(next, emptyList())
        }
        val requestId =
            nextMediaRequestIdOrNull(state) ?: return refuse(state, BrowserWorkspaceRefusal.MEDIA_REQUEST_ID_EXHAUSTED)
        val next = state.withPane(paneId) {
            it.copy(
                media = it.media.copy(
                    desiredMuted = muted,
                    muteApplication = BrowserWorkspaceMuteApplication.REQUESTED,
                    pendingMuteRequestId = requestId,
                ),
            )
        }.copy(nextMediaRequestId = requestId + 1)
        return BrowserWorkspaceTransition(
            next,
            listOf(BrowserWorkspaceEffect.ApplyPaneMute(paneId, pane.rendererGeneration, requestId, muted)),
        )
    }

    private fun playbackRequestCompleted(
        state: BrowserWorkspaceState,
        action: BrowserWorkspaceAction.PlaybackRequestCompleted,
    ): BrowserWorkspaceTransition {
        val pane = state.pane(action.paneId) ?: return unchanged(state)
        val request = pane.media.playbackRequest ?: return unchanged(state)
        if (!pane.rendererResidency.hasRenderer || pane.rendererGeneration != action.rendererGeneration ||
            request.requestId != action.requestId
        ) {
            return unchanged(state)
        }
        val status = when (action.result) {
            BrowserWorkspaceMediaDispatchResult.DISPATCHED -> BrowserWorkspacePlaybackRequestStatus.DISPATCHED
            BrowserWorkspaceMediaDispatchResult.REJECTED -> BrowserWorkspacePlaybackRequestStatus.REJECTED
        }
        return BrowserWorkspaceTransition(
            state.withPane(action.paneId) {
                it.copy(media = it.media.copy(playbackRequest = request.copy(status = status)))
            },
            emptyList(),
        )
    }

    private fun playbackObserved(
        state: BrowserWorkspaceState,
        action: BrowserWorkspaceAction.PlaybackObserved,
    ): BrowserWorkspaceTransition {
        val pane = state.pane(action.paneId) ?: return unchanged(state)
        if (!pane.rendererResidency.hasRenderer ||
            pane.rendererGeneration != action.rendererGeneration
        ) {
            return unchanged(state)
        }
        return BrowserWorkspaceTransition(
            state.withPane(action.paneId) {
                it.copy(media = it.media.copy(observedPlayback = action.observation))
            },
            emptyList(),
        )
    }

    private fun muteApplicationCompleted(
        state: BrowserWorkspaceState,
        action: BrowserWorkspaceAction.MuteApplicationCompleted,
    ): BrowserWorkspaceTransition {
        val pane = state.pane(action.paneId) ?: return unchanged(state)
        if (!pane.rendererResidency.hasRenderer || pane.rendererGeneration != action.rendererGeneration ||
            pane.media.pendingMuteRequestId != action.requestId
        ) {
            return unchanged(state)
        }
        val application = when (action.result) {
            BrowserWorkspaceMuteResult.APPLIED_TO_RENDERER -> BrowserWorkspaceMuteApplication.APPLIED_TO_RENDERER
            BrowserWorkspaceMuteResult.UNSUPPORTED -> BrowserWorkspaceMuteApplication.UNSUPPORTED
            BrowserWorkspaceMuteResult.FAILED -> BrowserWorkspaceMuteApplication.FAILED
        }
        return BrowserWorkspaceTransition(
            state.withPane(action.paneId) {
                it.copy(media = it.media.copy(muteApplication = application, pendingMuteRequestId = null))
            },
            emptyList(),
        )
    }

    /** Makes [paneId] resident, freeing least-recently-focused eligible live renderers first. */
    private fun makePaneLive(state: BrowserWorkspaceState, paneId: Long): ResidenceResult? {
        val pane = state.pane(paneId) ?: return null
        if (pane.rendererResidency.hasRenderer) return ResidenceResult(state.touch(paneId), emptyList())
        val generation = pane.rendererGeneration.nextPositiveOrNull() ?: return null
        var next = state.withPane(paneId) {
            it.copy(
                rendererResidency = BrowserWorkspaceRendererResidency.LIVE,
                rendererGeneration = generation,
                rendererFailure = null,
            )
        }
        val budget = enforceLiveBudget(next, paneId) ?: return null
        next = budget.state.touch(paneId)
        val effects = budget.effects.toMutableList()
        effects += BrowserWorkspaceEffect.RestoreRenderer(paneId, generation, pane.page.url, pane.slot)
        val mute = queueMuteForLivePane(next, paneId)
        next = mute.state
        effects += mute.effects
        return ResidenceResult(next, effects)
    }

    /**
     * Converts LRU live panes to suspended panes. Page fullscreen is never silently frozen.
     *
     * The returned effects are deliberately first in any transition that then creates/restores a
     * renderer, so the Android host can keep physical WebViews at the same cap as this state.
     */
    private fun enforceLiveBudget(state: BrowserWorkspaceState, keepPaneId: Long): BudgetResult? {
        var next = state
        val effects = mutableListOf<BrowserWorkspaceEffect>()
        var liveCount = next.livePaneCount
        if (liveCount <= capacity.maxLiveRenderers) return BudgetResult(next, effects)

        val recency = state.rendererRecency
        val candidates = state.panes
            .filter { pane ->
                pane.rendererResidency.hasRenderer &&
                    pane.id != keepPaneId &&
                    pane.id != state.pageFullscreenPaneId &&
                    pane.id != state.theaterPaneId
            }
            .sortedByDescending { pane ->
                val position = recency.indexOf(pane.id)
                if (position < 0) Int.MAX_VALUE else position
            }

        for (candidate in candidates) {
            if (liveCount <= capacity.maxLiveRenderers) break
            next = next.withPane(candidate.id) { pane ->
                pane.copy(
                    rendererResidency = BrowserWorkspaceRendererResidency.SUSPENDED,
                    media = pane.media.copy(
                        muteApplication = if (pane.media.desiredMuted) {
                            BrowserWorkspaceMuteApplication.PENDING_RENDERER
                        } else {
                            BrowserWorkspaceMuteApplication.NOT_REQUESTED
                        },
                        pendingMuteRequestId = null,
                    ),
                )
            }
            effects += BrowserWorkspaceEffect.FreezeRenderer(
                candidate.id,
                candidate.rendererGeneration,
                BrowserWorkspaceRendererReleaseReason.LIVE_RENDERER_CAP,
            )
            liveCount -= 1
        }
        return if (liveCount <= capacity.maxLiveRenderers) BudgetResult(next, effects) else null
    }

    private fun queueMuteForLivePane(state: BrowserWorkspaceState, paneId: Long): MuteQueueResult {
        val pane = state.pane(paneId) ?: return MuteQueueResult(state, emptyList())
        if (!pane.rendererResidency.hasRenderer || !pane.media.desiredMuted) return MuteQueueResult(state, emptyList())
        val requestId = nextMediaRequestIdOrNull(state) ?: return MuteQueueResult(
            state.withPane(paneId) {
                it.copy(media = it.media.copy(muteApplication = BrowserWorkspaceMuteApplication.FAILED))
            },
            emptyList(),
        )
        val next = state.withPane(paneId) {
            it.copy(
                media = it.media.copy(
                    muteApplication = BrowserWorkspaceMuteApplication.REQUESTED,
                    pendingMuteRequestId = requestId,
                ),
            )
        }.copy(nextMediaRequestId = requestId + 1)
        return MuteQueueResult(
            next,
            listOf(BrowserWorkspaceEffect.ApplyPaneMute(paneId, pane.rendererGeneration, requestId, muted = true)),
        )
    }

    private fun restoreLocalSnapshot(
        profile: BrowserWorkspaceProfile.LocalTv,
        snapshot: BrowserWorkspaceSnapshot,
    ): BrowserWorkspaceState? {
        if (snapshot.ownerProfileId != profile.profileId ||
            snapshot.nextPaneId <= BrowserWorkspaceState.NO_PANE_ID
        ) {
            return null
        }
        if (snapshot.panes.size > capacity.maxOpenPanes ||
            snapshot.panes.map(BrowserWorkspaceSavedPane::id).toSet().size != snapshot.panes.size
        ) {
            return null
        }
        val saved = snapshot.panes.sortedBy(BrowserWorkspaceSavedPane::slot)
        if (saved.any { it.id <= BrowserWorkspaceState.NO_PANE_ID || it.slot < 0 } ||
            saved.map(BrowserWorkspaceSavedPane::slot) != saved.indices.toList()
        ) {
            return null
        }
        if (!capacity.allows(snapshot.layout, saved.size)) return null
        val maximumId = saved.maxOfOrNull(BrowserWorkspaceSavedPane::id) ?: BrowserWorkspaceState.NO_PANE_ID
        if (snapshot.nextPaneId <= maximumId) return null
        val focused = snapshot.focusedPaneId.takeIf { id -> saved.any { it.id == id } }
            ?: saved.firstOrNull()?.id
            ?: BrowserWorkspaceState.NO_PANE_ID
        return BrowserWorkspaceState(
            profile = profile,
            panes = saved.map { savedPane ->
                BrowserWorkspacePane(
                    id = savedPane.id,
                    slot = savedPane.slot,
                    page = BrowserWorkspacePage(url = savedPane.page.url, title = savedPane.page.title),
                    rendererResidency = BrowserWorkspaceRendererResidency.SUSPENDED,
                    media = BrowserWorkspaceMediaState(
                        desiredMuted = savedPane.desiredMuted,
                        muteApplication = if (savedPane.desiredMuted) {
                            BrowserWorkspaceMuteApplication.PENDING_RENDERER
                        } else {
                            BrowserWorkspaceMuteApplication.NOT_REQUESTED
                        },
                    ),
                )
            },
            layout = snapshot.layout,
            split = snapshot.split,
            focusedPaneId = focused,
            nextPaneId = snapshot.nextPaneId,
        )
    }

    private fun teardownEffects(state: BrowserWorkspaceState): List<BrowserWorkspaceEffect> {
        val effects = mutableListOf<BrowserWorkspaceEffect>()
        state.pageFullscreenPaneId?.let { effects += BrowserWorkspaceEffect.PageFullscreenChanged(it, active = false) }
        state.theaterPaneId?.let { effects += BrowserWorkspaceEffect.TheaterModeChanged(null) }
        state.panes.forEach { pane -> effects += BrowserWorkspaceEffect.DestroyPane(pane.id, pane.rendererGeneration) }
        when (val profile = state.profile) {
            is BrowserWorkspaceProfile.LocalTv -> state.snapshotForPersistence()?.let { snapshot ->
                effects += BrowserWorkspaceEffect.PersistLocalWorkspace(profile.profileId, snapshot)
            }
            is BrowserWorkspaceProfile.ConnectedDevice -> {
                effects += BrowserWorkspaceEffect.DiscardEphemeralWorkspace(profile.connectionId)
            }
            null -> Unit
        }
        return effects
    }

    private fun layoutForCount(count: Int, prior: BrowserWorkspaceLayout): BrowserWorkspaceLayout = when (count) {
        0, 1 -> BrowserWorkspaceLayout.SINGLE
        2 -> if (prior == BrowserWorkspaceLayout.SPLIT_VERTICAL) {
            BrowserWorkspaceLayout.SPLIT_VERTICAL
        } else {
            BrowserWorkspaceLayout.SPLIT_HORIZONTAL
        }
        3, 4 -> BrowserWorkspaceLayout.GRID_2X2
        else -> error("capacity guarantees no more than four panes")
    }

    private fun hasExclusivePresentation(state: BrowserWorkspaceState): Boolean =
        state.pageFullscreenPaneId != null || state.theaterPaneId != null

    private fun BrowserWorkspaceState.withPane(
        paneId: Long,
        change: (BrowserWorkspacePane) -> BrowserWorkspacePane,
    ): BrowserWorkspaceState = copy(panes = panes.map { if (it.id == paneId) change(it) else it })

    private fun BrowserWorkspaceState.touch(paneId: Long): BrowserWorkspaceState = copy(
        rendererRecency =
        listOf(paneId) + rendererRecency.filterNot { it == paneId || panes.none { pane -> pane.id == it } },
    )

    private fun List<BrowserWorkspacePane>.compactSlots(): List<BrowserWorkspacePane> =
        sortedBy(BrowserWorkspacePane::slot).mapIndexed { index, pane -> pane.copy(slot = index) }

    private fun nextMediaRequestIdOrNull(state: BrowserWorkspaceState): Long? =
        state.nextMediaRequestId.takeIf { it > 0 && it != Long.MAX_VALUE }

    private fun Long.nextPositiveOrNull(): Long? = takeIf { it in 0 until Long.MAX_VALUE }?.plus(1)

    private fun unchanged(state: BrowserWorkspaceState) = BrowserWorkspaceTransition(state, emptyList())

    private fun refuse(state: BrowserWorkspaceState, reason: BrowserWorkspaceRefusal) =
        BrowserWorkspaceTransition(state, listOf(BrowserWorkspaceEffect.Refused(reason)))

    private data class BudgetResult(
        val state: BrowserWorkspaceState,
        val effects: List<BrowserWorkspaceEffect>,
    )

    private data class ResidenceResult(
        val state: BrowserWorkspaceState,
        val effects: List<BrowserWorkspaceEffect>,
    )

    private data class MuteQueueResult(
        val state: BrowserWorkspaceState,
        val effects: List<BrowserWorkspaceEffect>,
    )

    private data class NavigationStart(
        val state: BrowserWorkspaceState,
        val navigationId: Long,
    )
}
