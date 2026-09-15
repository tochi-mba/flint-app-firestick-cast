package com.rextechnologies.flint.receiver.browser.workspace

/**
 * The arrangement of independently navigable browser pages in a workspace.
 *
 * A layout describes rectangles, not media. A pane always represents a separate page and renderer;
 * a video is merely content that one of those pages might happen to show.
 */
enum class BrowserWorkspaceLayout {
    SINGLE,
    SPLIT_HORIZONTAL,
    SPLIT_VERTICAL,
    GRID_2X2,
    ;

    internal val maximumPanes: Int
        get() = when (this) {
            SINGLE -> 1
            SPLIT_HORIZONTAL, SPLIT_VERTICAL -> 2
            GRID_2X2 -> 4
        }
}

/** Which side owns D-pad arrows at this moment. */
enum class BrowserWorkspaceInteractionMode {
    /** The workspace chrome owns arrows, Select, and the pane focus ring. */
    WORKSPACE_CHROME,

    /** The focused WebView owns ordinary keys; Back still follows the workspace Back ladder. */
    PAGE,
}

/**
 * Whether a pane currently has an allocated WebView renderer.
 *
 * [LIVE] is a residency decision, not a claim that a page is successfully playing media. Hosts
 * execute the emitted effects in order and report a [FAILED] renderer back to the reducer.
 */
enum class BrowserWorkspaceRendererResidency {
    LIVE,
    SUSPENDED,
    FAILED,
    ;

    val hasRenderer: Boolean get() = this == LIVE
}

/** A non-sensitive reason the host could not create or restore a pane renderer. */
enum class BrowserWorkspaceRendererFailure {
    CREATE_FAILED,
    RESTORE_FAILED,
    RENDERER_PROCESS_GONE,
}

/** What the page itself has actually reported, if anything. */
enum class BrowserWorkspacePlaybackObservation {
    UNKNOWN,
    PLAYING,
    PAUSED,
    ENDED,
    UNAVAILABLE,
}

/** Delivery status, deliberately separate from [BrowserWorkspacePlaybackObservation]. */
enum class BrowserWorkspacePlaybackRequestStatus {
    PENDING_DISPATCH,
    DISPATCHED,
    REJECTED,
}

/** Result of asking the renderer to apply its per-WebView mute setting. */
enum class BrowserWorkspaceMuteApplication {
    NOT_REQUESTED,
    PENDING_RENDERER,
    REQUESTED,
    APPLIED_TO_RENDERER,
    UNSUPPORTED,
    FAILED,
}

/** Result of a best-effort media play/pause dispatch. */
enum class BrowserWorkspaceMediaDispatchResult {
    DISPATCHED,
    REJECTED,
}

/** Result of a per-WebView mute application attempt. */
enum class BrowserWorkspaceMuteResult {
    APPLIED_TO_RENDERER,
    UNSUPPORTED,
    FAILED,
}

/** A best-effort request, not an assertion that playback changed. */
data class BrowserWorkspacePlaybackRequest(
    val requestId: Long,
    val status: BrowserWorkspacePlaybackRequestStatus = BrowserWorkspacePlaybackRequestStatus.PENDING_DISPATCH,
)

/**
 * Media state intentionally separates a requested control from an observed page state.
 *
 * In particular, pressing play/pause must never make the UI say "playing" until the page has
 * reported that fact. Likewise, [desiredMuted] is a preference while [muteApplication] says how
 * far the renderer application got.
 */
data class BrowserWorkspaceMediaState(
    val observedPlayback: BrowserWorkspacePlaybackObservation = BrowserWorkspacePlaybackObservation.UNKNOWN,
    val playbackRequest: BrowserWorkspacePlaybackRequest? = null,
    val desiredMuted: Boolean = false,
    val muteApplication: BrowserWorkspaceMuteApplication = BrowserWorkspaceMuteApplication.NOT_REQUESTED,
    val pendingMuteRequestId: Long? = null,
)

/** Page information safe to show in workspace chrome and save with a local TV profile. */
data class BrowserWorkspacePage(
    val url: String = "",
    val title: String = "",
    val loading: Boolean = false,
    val progressPercent: Int = 0,
    val canGoBack: Boolean = false,
    val canGoForward: Boolean = false,
    /** Monotonic within a pane. It rejects late callbacks from an earlier navigation. */
    val navigationId: Long = 0,
)

/**
 * One independently navigable page rectangle.
 *
 * [slot] is visual order (left-to-right, then top-to-bottom); it is contiguous inside a state.
 * [rendererGeneration] changes whenever a suspended or failed pane is rebuilt, so old WebView
 * callbacks cannot alter a newly restored renderer.
 */
data class BrowserWorkspacePane(
    val id: Long,
    val slot: Int,
    val page: BrowserWorkspacePage = BrowserWorkspacePage(),
    val rendererResidency: BrowserWorkspaceRendererResidency = BrowserWorkspaceRendererResidency.SUSPENDED,
    val rendererGeneration: Long = 0,
    val rendererFailure: BrowserWorkspaceRendererFailure? = null,
    val media: BrowserWorkspaceMediaState = BrowserWorkspaceMediaState(),
) {
    val isSuspended: Boolean get() = rendererResidency == BrowserWorkspaceRendererResidency.SUSPENDED
}

/**
 * The authority for a workspace.
 *
 * A local profile is a TV-owned durable profile. A connected-device profile is an in-memory
 * projection only; it is deliberately never converted to a [BrowserWorkspaceSnapshot].
 */
sealed interface BrowserWorkspaceProfile {
    val persistenceScope: BrowserWorkspacePersistenceScope

    data class LocalTv(val profileId: String) : BrowserWorkspaceProfile {
        override val persistenceScope = BrowserWorkspacePersistenceScope.PERSISTENT_ON_TV
    }

    data class ConnectedDevice(
        val connectionId: Long,
        val displayName: String = "Connected device",
    ) : BrowserWorkspaceProfile {
        override val persistenceScope = BrowserWorkspacePersistenceScope.EPHEMERAL_CONNECTION
    }
}

enum class BrowserWorkspacePersistenceScope {
    PERSISTENT_ON_TV,
    EPHEMERAL_CONNECTION,
}

/**
 * Capacity selected by the receiver only after it knows what the current hardware can sustain.
 *
 * The default controller uses [CONSERVATIVE_FIRE_TV]: two independently live pages and no grid
 * until measured PSS/video-decode evidence enables [VERIFIED_GRID]. A later host may choose a
 * stricter capacity, but it must never raise [maxLiveRenderers] without a hardware decision.
 */
data class BrowserWorkspaceCapacity(
    val maxOpenPanes: Int,
    val maxLiveRenderers: Int,
    val allowedLayouts: Set<BrowserWorkspaceLayout>,
) {
    init {
        require(maxOpenPanes in 1..BrowserWorkspaceLayout.GRID_2X2.maximumPanes) {
            "maxOpenPanes must be between 1 and 4"
        }
        require(maxLiveRenderers in 1..maxOpenPanes) {
            "maxLiveRenderers must be between 1 and maxOpenPanes"
        }
        require(BrowserWorkspaceLayout.SINGLE in allowedLayouts) {
            "SINGLE must always be available"
        }
        require(allowedLayouts.all { it.maximumPanes <= maxOpenPanes }) {
            "A layout cannot have more slots than maxOpenPanes"
        }
    }

    fun allows(layout: BrowserWorkspaceLayout, paneCount: Int): Boolean =
        layout in allowedLayouts && layoutSupportsPaneCount(layout, paneCount)

    companion object {
        const val SAFE_MAX_LIVE_RENDERERS: Int = 2

        /** Shipping-safe starting point before real Fire TV evidence enables the 2×2 grid. */
        val CONSERVATIVE_FIRE_TV = BrowserWorkspaceCapacity(
            maxOpenPanes = 2,
            maxLiveRenderers = SAFE_MAX_LIVE_RENDERERS,
            allowedLayouts = setOf(
                BrowserWorkspaceLayout.SINGLE,
                BrowserWorkspaceLayout.SPLIT_HORIZONTAL,
                BrowserWorkspaceLayout.SPLIT_VERTICAL,
            ),
        )

        /** Explicit opt-in for hardware that has passed the renderer/decode/PSS acceptance gates. */
        val VERIFIED_GRID = BrowserWorkspaceCapacity(
            maxOpenPanes = 4,
            maxLiveRenderers = SAFE_MAX_LIVE_RENDERERS,
            allowedLayouts = BrowserWorkspaceLayout.entries.toSet(),
        )
    }
}

/** Current workspace snapshot. It has no WebView, video, cookie, or VPN secret in it. */
data class BrowserWorkspaceState(
    val profile: BrowserWorkspaceProfile? = null,
    val panes: List<BrowserWorkspacePane> = emptyList(),
    val layout: BrowserWorkspaceLayout = BrowserWorkspaceLayout.SINGLE,
    val focusedPaneId: Long = NO_PANE_ID,
    val interactionMode: BrowserWorkspaceInteractionMode = BrowserWorkspaceInteractionMode.WORKSPACE_CHROME,
    /** Page-requested custom-view fullscreen; it fills only this pane's bounds. */
    val pageFullscreenPaneId: Long? = null,
    /** Explicit workspace theater mode, distinct from page-requested fullscreen. */
    val theaterPaneId: Long? = null,
    /** Most-recently focused renderer first. It is a runtime eviction detail, never persisted. */
    val rendererRecency: List<Long> = emptyList(),
    val nextPaneId: Long = 1,
    val nextMediaRequestId: Long = 1,
    val split: BrowserWorkspaceSplit = BrowserWorkspaceSplit.Even,
) {
    val focusedPane: BrowserWorkspacePane? get() = panes.firstOrNull { it.id == focusedPaneId }
    val livePaneCount: Int get() = panes.count { it.rendererResidency.hasRenderer }
    val isLocalProfile: Boolean get() = profile is BrowserWorkspaceProfile.LocalTv
    val paneIdsInSlotOrder: List<Long> get() = panes.sortedBy(BrowserWorkspacePane::slot).map(BrowserWorkspacePane::id)

    fun pane(id: Long): BrowserWorkspacePane? = panes.firstOrNull { it.id == id }

    companion object {
        const val NO_PANE_ID: Long = 0
    }
}

/** The durable subset of a local TV workspace. Renderer state and device state are excluded. */
data class BrowserWorkspaceSnapshot(
    val ownerProfileId: String,
    val layout: BrowserWorkspaceLayout,
    val focusedPaneId: Long,
    val nextPaneId: Long,
    val panes: List<BrowserWorkspaceSavedPane>,
    val split: BrowserWorkspaceSplit = BrowserWorkspaceSplit.Even,
)

data class BrowserWorkspaceSavedPane(
    val id: Long,
    val slot: Int,
    val page: BrowserWorkspacePage,
    val desiredMuted: Boolean,
)

/** Returns null for a connected-device projection so it cannot accidentally be persisted on TV. */
fun BrowserWorkspaceState.snapshotForPersistence(): BrowserWorkspaceSnapshot? {
    val local = profile as? BrowserWorkspaceProfile.LocalTv ?: return null
    return BrowserWorkspaceSnapshot(
        ownerProfileId = local.profileId,
        split = split,
        layout = layout,
        focusedPaneId = focusedPaneId,
        nextPaneId = nextPaneId,
        panes = panes.sortedBy(BrowserWorkspacePane::slot).map { pane ->
            BrowserWorkspaceSavedPane(
                id = pane.id,
                slot = pane.slot,
                page = pane.page,
                desiredMuted = pane.media.desiredMuted,
            )
        },
    )
}

internal fun layoutSupportsPaneCount(layout: BrowserWorkspaceLayout, paneCount: Int): Boolean = when (paneCount) {
    0, 1 -> layout == BrowserWorkspaceLayout.SINGLE
    2 -> layout == BrowserWorkspaceLayout.SPLIT_HORIZONTAL || layout == BrowserWorkspaceLayout.SPLIT_VERTICAL
    3, 4 -> layout == BrowserWorkspaceLayout.GRID_2X2
    else -> false
}
