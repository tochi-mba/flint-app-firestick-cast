namespace Flint.Protocol;

// The cockpit message families — tabs, view settings, favicons, library, profiles, network
// and workspace. Split from the v1 browser messages because they are a separate compatibility
// story: v1 ids 14-20 are what every receiver understands, while everything here arrives by
// new type id and is enabled only after a receiver has demonstrably sent one.
//
// BrowserWireRules stays with the v1 file: it names every family at once, and the security
// predicate that decides what may not cross the plaintext channel must be readable as a whole.

/// <summary>Operations over the receiver-owned tab collection.</summary>
public enum BrowserTabAction
{
    /// <summary>Create a new tab; a zero tab ID asks the receiver to allocate it.</summary>
    New = 1,

    /// <summary>Close the named tab.</summary>
    Close = 2,

    /// <summary>Make the named tab active.</summary>
    Select = 3,

    /// <summary>Move the named tab one position in the receiver-selected direction.</summary>
    Move = 4,

    /// <summary>Duplicate the named tab.</summary>
    Duplicate = 5,
}

/// <summary>A host-owned, ordered operation over the receiver tab collection.</summary>
public sealed record BrowserTabCommandMessage(
    long Epoch,
    long CommandId,
    BrowserTabAction Action,
    long TabId,
    string? Url = null) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserTabCommand;
}

/// <summary>One bounded row in a browser tab-state snapshot.</summary>
public sealed record BrowserTabStateEntry(
    long TabId,
    BrowserLoadState LoadState,
    int Progress,
    bool CanGoBack,
    bool CanGoForward,
    bool Frozen,
    long FaviconId,
    string Url,
    string Title);

/// <summary>A receiver-owned, bounded snapshot of every browser tab.</summary>
public sealed record BrowserTabStateMessage(
    long Epoch,
    long Revision,
    long ActiveTabId,
    ValueList<BrowserTabStateEntry> Tabs) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserTabState;
}

/// <summary>Browser view-setting and find-in-page operations.</summary>
public enum BrowserViewAction
{
    /// <summary>Set the text zoom percentage from the command value.</summary>
    SetZoom = 1,

    /// <summary>Select a user-agent mode from the command value.</summary>
    SetUa = 2,

    /// <summary>Select a dark-mode policy from the command value.</summary>
    SetDark = 3,

    /// <summary>Select cursor or focus input from the command value.</summary>
    SetInputMode = 4,

    /// <summary>Enter or leave fullscreen from a zero/one command value.</summary>
    SetFullscreen = 5,

    /// <summary>Begin find-in-page using the command text.</summary>
    FindStart = 6,

    /// <summary>Move to the next find result.</summary>
    FindNext = 7,

    /// <summary>Move to the previous find result.</summary>
    FindPrev = 8,

    /// <summary>Clear the current find operation.</summary>
    FindClear = 9,

    /// <summary>Select a search engine and, for custom, its HTTPS template.</summary>
    SetSearchEngine = 10,
}

/// <summary>User-agent profile applied by the receiver WebView.</summary>
public enum BrowserUserAgentMode
{
    /// <summary>Television-oriented user agent.</summary>
    Tv = 1,

    /// <summary>Desktop-class user agent.</summary>
    Desktop = 2,

    /// <summary>Mobile user agent.</summary>
    Mobile = 3,
}

/// <summary>Receiver page-colour policy.</summary>
public enum BrowserDarkMode
{
    /// <summary>Follow the receiver system theme.</summary>
    FollowSystem = 1,

    /// <summary>Prefer the page's light presentation.</summary>
    Light = 2,

    /// <summary>Request supported algorithmic or page dark presentation.</summary>
    Dark = 3,
}

/// <summary>How the TV D-pad interacts with the current page.</summary>
public enum BrowserInteractionMode
{
    /// <summary>Move an on-screen pointer.</summary>
    Cursor = 1,

    /// <summary>Traverse the page's focus order.</summary>
    Focus = 2,
}

/// <summary>Search engines understood by the shared browser contract.</summary>
public enum BrowserSearchEngine
{
    /// <summary>DuckDuckGo preset.</summary>
    DuckDuckGo = 1,

    /// <summary>Google preset.</summary>
    Google = 2,

    /// <summary>Bing preset.</summary>
    Bing = 3,

    /// <summary>A custom HTTPS template carried only by SetSearchEngine.</summary>
    Custom = 4,
}

/// <summary>A host-owned, ordered browser view-setting operation.</summary>
public sealed record BrowserViewCommandMessage(
    long Epoch,
    long CommandId,
    BrowserViewAction Action,
    int Value = 0,
    string Text = "") : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserViewCommand;
}

/// <summary>A bounded receiver browser-view snapshot.</summary>
public sealed record BrowserViewStateMessage(
    long Epoch,
    long Revision,
    int ZoomPercent,
    BrowserUserAgentMode UaMode,
    BrowserDarkMode DarkMode,
    BrowserInteractionMode InputMode,
    bool Fullscreen,
    bool MediaPlaying,
    bool EditingFocused,
    bool FindActive,
    int FindCurrent,
    int FindTotal,
    BrowserSearchEngine SearchEngine) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserViewState;
}

/// <summary>A bounded PNG favicon published by the receiver.</summary>
public sealed record BrowserFaviconMessage(
    long Epoch,
    long FaviconId,
    int Width,
    int Height,
    BinaryData Png) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserFavicon;
}

/// <summary>Receiver-owned browser-library operations.</summary>
public enum BrowserLibraryAction
{
    /// <summary>Add or update a bookmark for the supplied URL and title.</summary>
    AddBookmark = 1,

    /// <summary>Remove the bookmark identified by URL.</summary>
    RemoveBookmark = 2,

    /// <summary>Clear only browsing history.</summary>
    ClearHistory = 3,

    /// <summary>Clear only bookmarks.</summary>
    ClearBookmarks = 4,

    /// <summary>Ask the receiver to publish its current library snapshot.</summary>
    RequestSnapshot = 5,

}

/// <summary>Kind tag carried by every browser-library row.</summary>
public enum BrowserLibraryEntryKind
{
    /// <summary>User-saved bookmark.</summary>
    Bookmark = 1,

    /// <summary>Recently visited page.</summary>
    History = 2,
}

/// <summary>A host-owned, ordered operation over the receiver browser library.</summary>
public sealed record BrowserLibraryCommandMessage(
    long Epoch,
    long CommandId,
    BrowserLibraryAction Action,
    string Url = "",
    string Title = "") : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserLibraryCommand;
}

/// <summary>One bounded row in a receiver browser-library snapshot.</summary>
public sealed record BrowserLibraryEntry(
    BrowserLibraryEntryKind Kind,
    long FaviconId,
    long LastVisitedMilliseconds,
    string Url,
    string Title);

/// <summary>A receiver-owned, bounded bookmark and history snapshot.</summary>
public sealed record BrowserLibraryStateMessage(
    long Epoch,
    long Revision,
    ValueList<BrowserLibraryEntry> Bookmarks,
    ValueList<BrowserLibraryEntry> History) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserLibraryState;
}

/// <summary>Browser profile selection and TV profile-management operations.</summary>
public enum BrowserProfileAction
{
    /// <summary>Select one persistent profile stored on the television.</summary>
    SelectTvProfile = 1,

    /// <summary>Create a persistent TV profile; the receiver allocates its stable identifier.</summary>
    CreateTvProfile = 2,

    /// <summary>Rename one persistent TV profile.</summary>
    RenameTvProfile = 3,

    /// <summary>Delete one persistent TV profile.</summary>
    DeleteTvProfile = 4,

    /// <summary>Use the connected device profile without persisting its data on the television.</summary>
    SelectDevice = 5,

    /// <summary>Request the current profile-source and TV-profile catalog snapshot.</summary>
    RequestSnapshot = 6,
}

/// <summary>Where the active browser profile is durably stored.</summary>
public enum BrowserProfileSource
{
    /// <summary>The active profile is persistent on this television.</summary>
    Tv = 1,

    /// <summary>The active profile belongs to the connected device and is session-only on the TV.</summary>
    Device = 2,
}

/// <summary>One bounded persistent TV browser profile.</summary>
public sealed record BrowserProfileEntry(string ProfileId, string Name);

/// <summary>An ordered browser profile selection or TV profile-management command.</summary>
public sealed record BrowserProfileCommandMessage(
    long Epoch,
    long CommandId,
    BrowserProfileAction Action,
    string ProfileId = "",
    string Name = "") : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserProfileCommand;
}

/// <summary>A bounded snapshot of the active source and persistent profiles on the television.</summary>
public sealed record BrowserProfileStateMessage(
    long Epoch,
    long Revision,
    BrowserProfileSource ActiveSource,
    string ActiveProfileId,
    string DeviceName,
    ValueList<BrowserProfileEntry> Profiles) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserProfileState;
}

/// <summary>Host-to-receiver browser network/VPN operations.</summary>
public enum BrowserNetworkAction
{
    /// <summary>Store or replace network settings for one TV profile.</summary>
    Set = 1,

    /// <summary>Clear stored network settings for one TV profile.</summary>
    Clear = 2,

    /// <summary>Request the current network/VPN snapshot for a profile or the active TV profile.</summary>
    RequestSnapshot = 3,
}

/// <summary>VPN provider carried by browser network settings.</summary>
public enum BrowserVpnProvider
{
    /// <summary>No VPN provider configured.</summary>
    None = 0,

    /// <summary>WireGuard configuration text.</summary>
    WireGuard = 1,
}

/// <summary>Soft-fail VPN session state published to the host.</summary>
public enum BrowserVpnSessionState
{
    /// <summary>No VPN session is active.</summary>
    Idle = 0,

    /// <summary>The system VPN consent prompt is required.</summary>
    NeedsConsent = 1,

    /// <summary>A tunnel connect is in progress.</summary>
    Connecting = 2,

    /// <summary>A tunnel is up.</summary>
    Connected = 3,

    /// <summary>The last connect attempt failed with a safe detail.</summary>
    Failed = 4,

    /// <summary>The device cannot prepare a VPN session.</summary>
    Unavailable = 5,
}

/// <summary>An ordered browser network/VPN settings command.</summary>
/// <remarks>
/// <see cref="ConfigText"/> is opaque WireGuard configuration and must never be logged.
/// Clear and RequestSnapshot must leave VPN fields empty/false/None.
/// </remarks>
public sealed record BrowserNetworkCommandMessage(
    long Epoch,
    long CommandId,
    BrowserNetworkAction Action,
    string ProfileId = "",
    bool VpnEnabled = false,
    BrowserVpnProvider Provider = BrowserVpnProvider.None,
    bool AutoConnectOnBrowserStart = false,
    bool RequireVpnBeforeBrowse = false,
    string ConfigText = "") : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserNetworkCommand;
}

/// <summary>A bounded network/VPN snapshot that never echoes full config text.</summary>
public sealed record BrowserNetworkStateMessage(
    long Epoch,
    long Revision,
    string ProfileId,
    bool VpnEnabled,
    BrowserVpnProvider Provider,
    bool AutoConnectOnBrowserStart,
    bool RequireVpnBeforeBrowse,
    bool ConfigPresent,
    bool CapabilityPreparable,
    string CapabilityReason,
    BrowserVpnSessionState SessionState,
    string SessionDetail = "") : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserNetworkState;
}

/// <summary>Browser workspace command actions.</summary>
public enum BrowserWorkspaceCommandAction
{
    /// <summary>Focus one independently browsing pane.</summary>
    Focus = 1,

    /// <summary>Open another independently browsing pane.</summary>
    OpenPane = 2,

    /// <summary>Close one independently browsing pane.</summary>
    ClosePane = 3,

    /// <summary>Change the visible workspace arrangement.</summary>
    SetLayout = 4,

    /// <summary>Navigate one pane to a new URL.</summary>
    Navigate = 5,

    /// <summary>Reload one pane.</summary>
    Reload = 6,

    /// <summary>Move one pane back in history.</summary>
    Back = 7,

    /// <summary>Move one pane forward in history.</summary>
    Forward = 8,

    /// <summary>Request a mute state for one pane.</summary>
    SetMute = 9,

    /// <summary>Request play/pause for one pane.</summary>
    PlayPause = 10,

    /// <summary>Change whether D-pad input targets chrome or the focused page.</summary>
    SetInteraction = 11,

    /// <summary>Enlarge the focused pane into theatre mode.</summary>
    EnterTheater = 12,

    /// <summary>Leave theatre mode.</summary>
    ExitTheater = 13,

    /// <summary>Ask the receiver to publish its current workspace snapshot.</summary>
    RequestSnapshot = 14,

    /// <summary>Move one pane to another slot without recreating its renderer.</summary>
    MovePane = 15,
}

/// <summary>Visible workspace arrangements.</summary>
public enum BrowserWorkspaceWireLayout
{
    /// <summary>One pane fills the workspace.</summary>
    Single = 1,

    /// <summary>Two panes side by side.</summary>
    TwoColumns = 2,

    /// <summary>Two panes stacked vertically.</summary>
    TwoRows = 3,

    /// <summary>Four independently browsing panes.</summary>
    FourGrid = 4,
}

/// <summary>Which side owns D-pad input in a workspace.</summary>
public enum BrowserWorkspaceWireInteractionMode
{
    /// <summary>Workspace chrome owns arrows and pane focus.</summary>
    WorkspaceChrome = 1,

    /// <summary>The focused page owns ordinary keys.</summary>
    Page = 2,
}

/// <summary>Renderer residency for one workspace pane.</summary>
public enum BrowserWorkspaceWirePaneResidency
{
    /// <summary>The pane has a live renderer.</summary>
    Live = 1,

    /// <summary>The receiver intentionally suspended the pane.</summary>
    Suspended = 2,

    /// <summary>The renderer or navigation failed.</summary>
    Failed = 3,
}

/// <summary>Workspace input kinds.</summary>
public enum BrowserWorkspaceInputKind
{
    /// <summary>A portable semantic key.</summary>
    Key = 1,

    /// <summary>Explicit text for the focused pane.</summary>
    Text = 2,
}

/// <summary>Observed playback state for one workspace pane.</summary>
public enum BrowserWorkspaceWireObservedPlayback
{
    /// <summary>No playback state was reported.</summary>
    Unknown = 1,

    /// <summary>Playback is active.</summary>
    Playing = 2,

    /// <summary>Playback is paused.</summary>
    Paused = 3,

    /// <summary>Playback ended.</summary>
    Ended = 4,

    /// <summary>Playback is unavailable on this page.</summary>
    Unavailable = 5,
}

/// <summary>Per-WebView mute application status for one workspace pane.</summary>
public enum BrowserWorkspaceWireMuteApplication
{
    /// <summary>No mute request was made.</summary>
    NotRequested = 0,

    /// <summary>A mute request is waiting for the renderer.</summary>
    PendingRenderer = 1,

    /// <summary>A mute request was sent to the renderer.</summary>
    Requested = 2,

    /// <summary>The renderer applied the mute setting.</summary>
    AppliedToRenderer = 3,

    /// <summary>The renderer cannot mute this page.</summary>
    Unsupported = 4,

    /// <summary>The mute request failed.</summary>
    Failed = 5,
}

/// <summary>An ordered host-to-receiver browser workspace command.</summary>
public sealed record BrowserWorkspaceCommandMessage(
    long Epoch,
    long CommandId,
    long ExpectedRevision,
    BrowserWorkspaceCommandAction Action,
    long PaneId = 0,
    byte Value = 0,
    string Url = "") : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserWorkspaceCommand;
}

/// <summary>One bounded pane row in a browser workspace snapshot.</summary>
public sealed record BrowserWorkspacePaneStateEntry(
    long PaneId,
    byte Slot,
    BrowserWorkspaceWirePaneResidency Residency,
    string Url,
    string Title,
    bool Loading,
    byte Progress,
    bool CanGoBack,
    bool CanGoForward,
    bool DesiredMuted,
    BrowserWorkspaceWireMuteApplication MuteApplication,
    BrowserWorkspaceWireObservedPlayback ObservedPlayback);

/// <summary>A bounded receiver-owned browser workspace snapshot.</summary>
public sealed record BrowserWorkspaceStateMessage(
    long Epoch,
    long Revision,
    BrowserWorkspaceWireLayout Layout,
    long FocusedPaneId,
    BrowserWorkspaceWireInteractionMode InteractionMode,
    long PageFullscreenPaneId,
    long TheaterPaneId,
    byte MaxLiveRenderers,
    byte MaxOpenPanes,
    ValueList<BrowserWorkspacePaneStateEntry> Panes) : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserWorkspaceState;
}

/// <summary>An ordered host-to-receiver browser workspace input event.</summary>
public sealed record BrowserWorkspaceInputMessage(
    long Epoch,
    long CommandId,
    long ExpectedRevision,
    long PaneId,
    BrowserWorkspaceInputKind Kind,
    BrowserSemanticKey? Key = null,
    string Text = "") : WireMessage
{
    /// <inheritdoc />
    public override int TypeId => (int)WireMessageType.BrowserWorkspaceInput;
}
