package com.rextechnologies.flint.protocol.wire

import com.rextechnologies.flint.protocol.BinaryData

/** Stable v2 browser caps enforced before a value reaches WebView, UI, or an image decoder. */
object BrowserWireLimits {
    const val MAX_URL_BYTES = 4 * 1024
    const val MAX_TEXT_BYTES = 4 * 1024
    const val MAX_TITLE_BYTES = 512
    const val MAX_DETAIL_BYTES = 1024
    const val MAX_ORIGIN_BYTES = 2 * 1024
    const val MAX_DIALOG_BYTES = 4 * 1024
    const val MAX_PREVIEW_BYTES = 768 * 1024
    const val MAX_PREVIEW_WIDTH = 960
    const val MAX_PREVIEW_HEIGHT = 540
    const val MAX_INTERACTIVE_PREVIEW_FPS = 5
    const val MAX_IDLE_PREVIEW_FPS = 1
    const val MAX_DIALOG_TIMEOUT_MILLISECONDS = 60_000
    const val MAX_TABS = 8
    const val MAX_FAVICON_BYTES = 16 * 1024
    const val MAX_FAVICON_DIMENSION = 64
    const val MAX_FIND_TEXT_BYTES = 512
    const val MIN_ZOOM_PERCENT = 50
    const val MAX_ZOOM_PERCENT = 200
    const val MAX_BOOKMARKS = 0xff
    const val MAX_HISTORY_ENTRIES = 0xff
    const val MAX_PROFILE_ID_BYTES = 64
    const val MAX_PROFILE_NAME_BYTES = 64
    const val MAX_DEVICE_PROFILE_NAME_BYTES = 64
    const val MAX_TV_PROFILES = 8
    const val MAX_CONFIG_UTF8_BYTES = 64 * 1024
    const val MAX_WORKSPACE_PANES = 8
}

/** Receiver browser capability reported only after the future TLS BrowserSession authenticates. */
enum class BrowserCapabilityStatus(val id: Int) {
    AVAILABLE(1),
    SECURE_ENDPOINT_UNAVAILABLE(2),
    WEBVIEW_UNAVAILABLE(3),
    UNSUPPORTED_PLATFORM(4),
    DISTRIBUTION_RESTRICTED(5),
    ;

    companion object {
        fun fromId(id: Int): BrowserCapabilityStatus? = entries.firstOrNull { it.id == id }
    }
}

/** A non-secret receiver capability snapshot. It never carries certificate/token/cookie material. */
data class BrowserCapabilityMessage(
    val status: BrowserCapabilityStatus,
    val secureEndpointPort: Int,
    val apiLevel: Int,
    val webViewVersion: String,
    val previewSupported: Boolean,
    val previewMaxWidth: Int,
    val previewMaxHeight: Int,
    val interactivePreviewFramesPerSecond: Int,
    val idlePreviewFramesPerSecond: Int,
    val previewMaxBytes: Int,
    val detail: String = "",
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_CAPABILITY.id
}

/** Browser navigation/lifecycle command kinds. */
enum class BrowserCommandAction(val id: Int) {
    OPEN(1),
    NAVIGATE(2),
    BACK(3),
    FORWARD(4),
    RELOAD(5),
    STOP(6),
    CLOSE(7),
    SET_PREVIEW_ENABLED(8),
    CLEAR_DATA(9),
    ;

    companion object {
        fun fromId(id: Int): BrowserCommandAction? = entries.firstOrNull { it.id == id }
    }
}

/** A host-owned, positive, ordered browser command. */
data class BrowserCommandMessage(
    val epoch: Long,
    val commandId: Long,
    val action: BrowserCommandAction,
    val url: String? = null,
    val previewEnabled: Boolean? = null,
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_COMMAND.id
}

/** Portable pointer actions. No Android or Windows input constants cross the wire. */
enum class BrowserPointerAction(val id: Int) {
    MOVE(1),
    DOWN(2),
    UP(3),
    CANCEL(4),
    ;

    companion object {
        fun fromId(id: Int): BrowserPointerAction? = entries.firstOrNull { it.id == id }
    }
}

/** The entire semantic-key allow-list. */
enum class BrowserSemanticKey(val id: Int) {
    UP(1),
    DOWN(2),
    LEFT(3),
    RIGHT(4),
    SELECT(5),
    BACK(6),
    TAB(7),
    SHIFT_TAB(8),
    ESCAPE(9),
    PAGE_UP(10),
    PAGE_DOWN(11),
    HOME(12),
    END(13),
    REFRESH(14),
    ;

    companion object {
        fun fromId(id: Int): BrowserSemanticKey? = entries.firstOrNull { it.id == id }
    }
}

/** One portable browser input union variant. */
sealed interface BrowserInputEvent {
    val eventId: Int
}

/** Pointer input tied to a current preview navigation/frame reference. */
data class BrowserPointerInput(
    val action: BrowserPointerAction,
    val navigationId: Long,
    val frameId: Long,
    /** Fixed normalised x coordinate: 0..65,535. */
    val x: Int,
    /** Fixed normalised y coordinate: 0..65,535. */
    val y: Int,
    /** Only primary bit 1 or zero is legal. */
    val buttons: Int = 0,
) : BrowserInputEvent {
    override val eventId: Int = 1
}

/** Scroll input tied to a current preview navigation/frame reference. */
data class BrowserScrollInput(
    val navigationId: Long,
    val frameId: Long,
    val x: Int,
    val y: Int,
    val deltaX: Int,
    val deltaY: Int,
) : BrowserInputEvent {
    override val eventId: Int = 2
}

/** A semantic key from the explicit allow-list. */
data class BrowserSemanticKeyInput(val key: BrowserSemanticKey) : BrowserInputEvent {
    override val eventId: Int = 3
}

/** Explicit user text that must never be returned in browser state, logs, or telemetry. */
data class BrowserTextInput(val text: String) : BrowserInputEvent {
    override val eventId: Int = 4
}

/** A host-owned, positive, ordered browser input event. */
data class BrowserInputMessage(
    val epoch: Long,
    val sequence: Long,
    val event: BrowserInputEvent,
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_INPUT.id
}

/** Safe page load state; never DOM/cookies/headers/form values/source. */
enum class BrowserLoadState(val id: Int) {
    IDLE(1),
    LOADING(2),
    LOADED(3),
    FAILED(4),
    CLOSED(5),
    ;

    companion object {
        fun fromId(id: Int): BrowserLoadState? = entries.firstOrNull { it.id == id }
    }
}

/** Passive preview availability for the current secure browser session. */
enum class BrowserPreviewState(val id: Int) {
    DISABLED(1),
    ENABLED(2),
    UNAVAILABLE(3),
    ;

    companion object {
        fun fromId(id: Int): BrowserPreviewState? = entries.firstOrNull { it.id == id }
    }
}

/** Bounded safe page-status snapshot from receiver to host. */
data class BrowserStateMessage(
    val epoch: Long,
    val revision: Long,
    val navigationId: Long,
    val lastAcceptedCommandId: Long,
    val lastAcceptedInputSequence: Long,
    val loadState: BrowserLoadState,
    val url: String,
    val title: String,
    val progress: Int,
    val canGoBack: Boolean,
    val canGoForward: Boolean,
    val viewportWidth: Int,
    val viewportHeight: Int,
    val previewState: BrowserPreviewState,
    val errorDetail: String = "",
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_STATE.id
}

/** Latest-only bounded JPEG preview. The transport owns capacity-one delivery, not this value. */
data class BrowserPreviewMessage(
    val epoch: Long,
    val navigationId: Long,
    val frameId: Long,
    val width: Int,
    val height: Int,
    val jpeg: BinaryData,
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_PREVIEW.id
}

/** Native dialog types a secure remote can observe and explicitly answer. */
enum class BrowserDialogType(val id: Int) {
    ALERT(1),
    CONFIRM(2),
    PROMPT(3),
    BEFORE_UNLOAD(4),
    ;

    companion object {
        fun fromId(id: Int): BrowserDialogType? = entries.firstOrNull { it.id == id }
    }
}

/** An expiring native browser dialog observation. */
data class BrowserDialogMessage(
    val epoch: Long,
    val dialogId: Long,
    val type: BrowserDialogType,
    val origin: String,
    val message: String,
    val defaultValue: String,
    val timeoutMilliseconds: Int,
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_DIALOG.id
}

/** Explicit secure reply to the one active native dialog. */
data class BrowserDialogReplyMessage(
    val epoch: Long,
    val dialogId: Long,
    val accepted: Boolean,
    val promptText: String? = null,
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_DIALOG_REPLY.id
}

/** Operations over the receiver-owned tab collection. */
enum class BrowserTabAction(val id: Int) {
    NEW(1),
    CLOSE(2),
    SELECT(3),
    MOVE(4),
    DUPLICATE(5),
    ;

    companion object {
        fun fromId(id: Int): BrowserTabAction? = entries.firstOrNull { it.id == id }
    }
}

/** One bounded tab row in a browser tab-state snapshot. */
data class BrowserTabStateEntry(
    val tabId: Long,
    val loadState: BrowserLoadState,
    val progress: Int,
    val canGoBack: Boolean,
    val canGoForward: Boolean,
    val frozen: Boolean,
    val faviconId: Long,
    val url: String,
    val title: String,
)

/** Ordered host command over the receiver-owned tab collection. */
data class BrowserTabCommandMessage(
    val epoch: Long,
    val commandId: Long,
    val action: BrowserTabAction,
    val tabId: Long,
    val url: String? = null,
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_TAB_COMMAND.id
}

/** Bounded receiver-owned tab collection snapshot. */
data class BrowserTabStateMessage(
    val epoch: Long,
    val revision: Long,
    val activeTabId: Long,
    val tabs: List<BrowserTabStateEntry>,
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_TAB_STATE.id
}

/** Browser view-setting and find-in-page operations. */
enum class BrowserViewAction(val id: Int) {
    SET_ZOOM(1),
    SET_UA(2),
    SET_DARK(3),
    SET_INPUT_MODE(4),
    SET_FULLSCREEN(5),
    FIND_START(6),
    FIND_NEXT(7),
    FIND_PREV(8),
    FIND_CLEAR(9),
    SET_SEARCH_ENGINE(10),
    ;

    companion object {
        fun fromId(id: Int): BrowserViewAction? = entries.firstOrNull { it.id == id }
    }
}

enum class BrowserUserAgentMode(val id: Int) {
    TV(1),
    DESKTOP(2),
    MOBILE(3),
    ;

    companion object {
        fun fromId(id: Int): BrowserUserAgentMode? = entries.firstOrNull { it.id == id }
    }
}

enum class BrowserDarkMode(val id: Int) {
    FOLLOW_SYSTEM(1),
    LIGHT(2),
    DARK(3),
    ;

    companion object {
        fun fromId(id: Int): BrowserDarkMode? = entries.firstOrNull { it.id == id }
    }
}

enum class BrowserInteractionMode(val id: Int) {
    CURSOR(1),
    FOCUS(2),
    ;

    companion object {
        fun fromId(id: Int): BrowserInteractionMode? = entries.firstOrNull { it.id == id }
    }
}

enum class BrowserSearchEngine(val id: Int) {
    DUCKDUCKGO(1),
    GOOGLE(2),
    BING(3),
    CUSTOM(4),
    ;

    companion object {
        fun fromId(id: Int): BrowserSearchEngine? = entries.firstOrNull { it.id == id }
    }
}

/** Ordered host command for page-view settings and find-in-page. */
data class BrowserViewCommandMessage(
    val epoch: Long,
    val commandId: Long,
    val action: BrowserViewAction,
    val value: Int,
    val text: String = "",
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_VIEW_COMMAND.id
}

/** Bounded receiver browser-view snapshot. */
data class BrowserViewStateMessage(
    val epoch: Long,
    val revision: Long,
    val zoomPercent: Int,
    val userAgentMode: BrowserUserAgentMode,
    val darkMode: BrowserDarkMode,
    val inputMode: BrowserInteractionMode,
    val fullscreen: Boolean,
    val mediaPlaying: Boolean,
    /** Reports focus presence only. It never carries the field's value. */
    val editingFocused: Boolean,
    val findActive: Boolean,
    val findCurrent: Int,
    val findTotal: Int,
    val searchEngine: BrowserSearchEngine,
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_VIEW_STATE.id
}

/** Bounded PNG favicon tied to a receiver-owned id. */
data class BrowserFaviconMessage(
    val epoch: Long,
    val faviconId: Long,
    val width: Int,
    val height: Int,
    val png: BinaryData,
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_FAVICON.id
}

enum class BrowserLibraryAction(val id: Int) {
    ADD_BOOKMARK(1),
    REMOVE_BOOKMARK(2),
    CLEAR_HISTORY(3),
    CLEAR_BOOKMARKS(4),
    REQUEST_SNAPSHOT(5),
    ;

    companion object {
        fun fromId(id: Int): BrowserLibraryAction? = entries.firstOrNull { it.id == id }
    }
}

enum class BrowserLibraryEntryKind(val id: Int) {
    BOOKMARK(1),
    HISTORY(2),
    ;

    companion object {
        fun fromId(id: Int): BrowserLibraryEntryKind? = entries.firstOrNull { it.id == id }
    }
}

/** One bounded row in a receiver browser-library snapshot. */
data class BrowserLibraryEntry(
    val kind: BrowserLibraryEntryKind,
    val faviconId: Long,
    val lastVisitedMs: Long,
    val url: String,
    val title: String,
)

/** Ordered host command over the receiver-owned bookmark/history library. */
data class BrowserLibraryCommandMessage(
    val epoch: Long,
    val commandId: Long,
    val action: BrowserLibraryAction,
    val url: String = "",
    val title: String = "",
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_LIBRARY_COMMAND.id
}

/** Bounded receiver-owned bookmark and history snapshot. */
data class BrowserLibraryStateMessage(
    val epoch: Long,
    val revision: Long,
    val bookmarks: List<BrowserLibraryEntry>,
    val history: List<BrowserLibraryEntry>,
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_LIBRARY_STATE.id
}

enum class BrowserProfileAction(val id: Int) {
    SELECT_TV_PROFILE(1),
    CREATE_TV_PROFILE(2),
    RENAME_TV_PROFILE(3),
    DELETE_TV_PROFILE(4),
    SELECT_DEVICE(5),
    REQUEST_SNAPSHOT(6),
    ;

    companion object {
        fun fromId(id: Int): BrowserProfileAction? = entries.firstOrNull { it.id == id }
    }
}

enum class BrowserProfileSource(val id: Int) {
    TV(1),
    DEVICE(2),
    ;

    companion object {
        fun fromId(id: Int): BrowserProfileSource? = entries.firstOrNull { it.id == id }
    }
}

/** One bounded persistent browser profile stored on the television. */
data class BrowserProfileEntry(
    val profileId: String,
    val name: String,
)

/** Ordered profile selection or TV profile-management command. */
data class BrowserProfileCommandMessage(
    val epoch: Long,
    val commandId: Long,
    val action: BrowserProfileAction,
    val profileId: String = "",
    val name: String = "",
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_PROFILE_COMMAND.id
}

/** Active profile source and bounded catalog of persistent TV profiles. */
data class BrowserProfileStateMessage(
    val epoch: Long,
    val revision: Long,
    val activeSource: BrowserProfileSource,
    val activeProfileId: String,
    val deviceName: String,
    val profiles: List<BrowserProfileEntry>,
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_PROFILE_STATE.id
}

/** Host-to-receiver browser network/VPN operations. */
enum class BrowserNetworkAction(val id: Int) {
    SET(1),
    CLEAR(2),
    REQUEST_SNAPSHOT(3),
    ;

    companion object {
        fun fromId(id: Int): BrowserNetworkAction? = entries.firstOrNull { it.id == id }
    }
}

/** VPN provider carried by browser network settings. */
enum class BrowserVpnProvider(val id: Int) {
    NONE(0),
    WIREGUARD(1),
    ;

    companion object {
        fun fromId(id: Int): BrowserVpnProvider? = entries.firstOrNull { it.id == id }
    }
}

/** Soft-fail VPN session state published to the host. */
enum class BrowserVpnSessionState(val id: Int) {
    IDLE(0),
    NEEDS_CONSENT(1),
    CONNECTING(2),
    CONNECTED(3),
    FAILED(4),
    UNAVAILABLE(5),
    ;

    companion object {
        fun fromId(id: Int): BrowserVpnSessionState? = entries.firstOrNull { it.id == id }
    }
}

/**
 * An ordered browser network/VPN settings command.
 *
 * [configText] is opaque WireGuard configuration and must never be logged.
 * Clear and RequestSnapshot must leave VPN fields empty/false/None.
 */
data class BrowserNetworkCommandMessage(
    val epoch: Long,
    val commandId: Long,
    val action: BrowserNetworkAction,
    val profileId: String = "",
    val vpnEnabled: Boolean = false,
    val provider: BrowserVpnProvider = BrowserVpnProvider.NONE,
    val autoConnectOnBrowserStart: Boolean = false,
    val requireVpnBeforeBrowse: Boolean = false,
    val configText: String = "",
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_NETWORK_COMMAND.id
}

/** A bounded network/VPN snapshot that never echoes full config text. */
data class BrowserNetworkStateMessage(
    val epoch: Long,
    val revision: Long,
    val profileId: String,
    val vpnEnabled: Boolean,
    val provider: BrowserVpnProvider,
    val autoConnectOnBrowserStart: Boolean,
    val requireVpnBeforeBrowse: Boolean,
    val configPresent: Boolean,
    val capabilityPreparable: Boolean,
    val capabilityReason: String,
    val sessionState: BrowserVpnSessionState,
    val sessionDetail: String = "",
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_NETWORK_STATE.id
}

enum class BrowserWorkspaceCommandAction(val id: Int) {
    FOCUS(1),
    OPEN_PANE(2),
    CLOSE_PANE(3),
    SET_LAYOUT(4),
    NAVIGATE(5),
    RELOAD(6),
    BACK(7),
    FORWARD(8),
    SET_MUTE(9),
    PLAY_PAUSE(10),
    SET_INTERACTION(11),
    ENTER_THEATER(12),
    EXIT_THEATER(13),
    REQUEST_SNAPSHOT(14),
    MOVE_PANE(15),
    ;

    companion object {
        fun fromId(id: Int): BrowserWorkspaceCommandAction? = entries.firstOrNull { it.id == id }
    }
}

enum class BrowserWorkspaceWireLayout(val id: Int) {
    SINGLE(1),
    TWO_COLUMNS(2),
    TWO_ROWS(3),
    FOUR_GRID(4),
    ;

    companion object {
        fun fromId(id: Int): BrowserWorkspaceWireLayout? = entries.firstOrNull { it.id == id }
    }
}

enum class BrowserWorkspaceWireInteractionMode(val id: Int) {
    WORKSPACE_CHROME(1),
    PAGE(2),
    ;

    companion object {
        fun fromId(id: Int): BrowserWorkspaceWireInteractionMode? = entries.firstOrNull { it.id == id }
    }
}

enum class BrowserWorkspaceWirePaneResidency(val id: Int) {
    LIVE(1),
    SUSPENDED(2),
    FAILED(3),
    ;

    companion object {
        fun fromId(id: Int): BrowserWorkspaceWirePaneResidency? = entries.firstOrNull { it.id == id }
    }
}

enum class BrowserWorkspaceInputKind(val id: Int) {
    KEY(1),
    TEXT(2),
    ;

    companion object {
        fun fromId(id: Int): BrowserWorkspaceInputKind? = entries.firstOrNull { it.id == id }
    }
}

enum class BrowserWorkspaceWireObservedPlayback(val id: Int) {
    UNKNOWN(1),
    PLAYING(2),
    PAUSED(3),
    ENDED(4),
    UNAVAILABLE(5),
    ;

    companion object {
        fun fromId(id: Int): BrowserWorkspaceWireObservedPlayback? = entries.firstOrNull { it.id == id }
    }
}

enum class BrowserWorkspaceWireMuteApplication(val id: Int) {
    NOT_REQUESTED(0),
    PENDING_RENDERER(1),
    REQUESTED(2),
    APPLIED_TO_RENDERER(3),
    UNSUPPORTED(4),
    FAILED(5),
    ;

    companion object {
        fun fromId(id: Int): BrowserWorkspaceWireMuteApplication? = entries.firstOrNull { it.id == id }
    }
}

data class BrowserWorkspaceCommandMessage(
    val epoch: Long,
    val commandId: Long,
    val expectedRevision: Long,
    val action: BrowserWorkspaceCommandAction,
    val paneId: Long = 0,
    val value: Int = 0,
    val url: String = "",
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_WORKSPACE_COMMAND.id
}

data class BrowserWorkspacePaneStateEntry(
    val paneId: Long,
    val slot: Int,
    val residency: BrowserWorkspaceWirePaneResidency,
    val url: String,
    val title: String,
    val loading: Boolean,
    val progress: Int,
    val canGoBack: Boolean,
    val canGoForward: Boolean,
    val desiredMuted: Boolean,
    val muteApplication: BrowserWorkspaceWireMuteApplication,
    val observedPlayback: BrowserWorkspaceWireObservedPlayback,
)

data class BrowserWorkspaceStateMessage(
    val epoch: Long,
    val revision: Long,
    val layout: BrowserWorkspaceWireLayout,
    val focusedPaneId: Long,
    val interactionMode: BrowserWorkspaceWireInteractionMode,
    val pageFullscreenPaneId: Long,
    val theaterPaneId: Long,
    val maxLiveRenderers: Int,
    val maxOpenPanes: Int,
    val panes: List<BrowserWorkspacePaneStateEntry>,
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_WORKSPACE_STATE.id
}

data class BrowserWorkspaceInputMessage(
    val epoch: Long,
    val commandId: Long,
    val expectedRevision: Long,
    val paneId: Long,
    val kind: BrowserWorkspaceInputKind,
    val key: BrowserSemanticKey? = null,
    val text: String = "",
) : WireMessage {
    override val typeId: Int = WireMessageType.BROWSER_WORKSPACE_INPUT.id
}

/** Browser compatibility predicates used by legacy-route guards and v2 codecs. */
object BrowserWireRules {
    fun isBrowserMessage(message: WireMessage): Boolean = message is BrowserCapabilityMessage ||
        message is BrowserCommandMessage ||
        message is BrowserInputMessage ||
        message is BrowserStateMessage ||
        message is BrowserPreviewMessage ||
        message is BrowserDialogMessage ||
        message is BrowserDialogReplyMessage ||
        message is BrowserTabCommandMessage ||
        message is BrowserTabStateMessage ||
        message is BrowserViewCommandMessage ||
        message is BrowserViewStateMessage ||
        message is BrowserFaviconMessage ||
        message is BrowserLibraryCommandMessage ||
        message is BrowserLibraryStateMessage ||
        message is BrowserProfileCommandMessage ||
        message is BrowserProfileStateMessage ||
        message is BrowserNetworkCommandMessage ||
        message is BrowserNetworkStateMessage ||
        message is BrowserWorkspaceCommandMessage ||
        message is BrowserWorkspaceStateMessage ||
        message is BrowserWorkspaceInputMessage || message is BrowserWorkspaceResizeMessage || message is BrowserWorkspaceGeometryMessage

    fun isWorkspaceMessage(message: WireMessage): Boolean =
        message is BrowserWorkspaceCommandMessage ||
            message is BrowserWorkspaceStateMessage ||
            message is BrowserWorkspaceInputMessage || message is BrowserWorkspaceResizeMessage || message is BrowserWorkspaceGeometryMessage

    fun isBrowserType(typeId: Int): Boolean = typeId in WireMessageType.BROWSER_CAPABILITY.id..
        WireMessageType.BROWSER_WORKSPACE_GEOMETRY.id

    fun isForbiddenOnOrdinaryChannel(message: WireMessage): Boolean =
        isBrowserMessage(message) || (message is SurfaceMessage && message.mode == SurfaceMode.BROWSER)

    fun isAllowedAtVersion(protocolVersion: Int, message: WireMessage): Boolean {
        if ((message is BrowserWorkspaceResizeMessage || message is BrowserWorkspaceGeometryMessage) && protocolVersion < 4) return false
        if (isWorkspaceMessage(message) && protocolVersion < 3) return false
        return protocolVersion >= 2 || (!isBrowserMessage(message) &&
            (message !is SurfaceMessage || message.mode != SurfaceMode.BROWSER))
    }
}
