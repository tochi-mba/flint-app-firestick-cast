namespace Flint.App.Services;

using Flint.App.ViewModels;

/// <summary>
/// Optional controls introduced by the additive browser protocol extension.
/// </summary>
/// <remarks>
/// The base secure remote deliberately remains useful with protocol IDs 14-20. A receiver/session
/// adapter implements this interface only when tab, view and library messages are available; the UI
/// can therefore render an honest unavailable state instead of sending guessed wire payloads.
/// </remarks>
public interface IBrowserCockpitRemote
{
    /// <summary>Features the connected receiver can actually accept.</summary>
    BrowserCockpitFeatures CockpitFeatures { get; }

    /// <summary>Pane commands for a receiver that has demonstrated workspace support.</summary>
    IBrowserWorkspaceCommandSink? WorkspaceCommandSink => null;

    /// <summary>Full replacement snapshot of the TV's open tabs.</summary>
    event Action<BrowserTabsSnapshot>? TabsReceived;

    /// <summary>Current TV view settings and find result.</summary>
    event Action<BrowserViewSnapshot>? ViewReceived;

    /// <summary>Bounded TV-owned bookmarks and history.</summary>
    event Action<BrowserLibrarySnapshot>? LibraryReceived;

    /// <summary>A receiver request addressed to the active Windows-device library.</summary>
    event Action<BrowserLibraryRequest>? DeviceLibraryRequestReceived;

    /// <summary>Named persistent TV profiles and the currently selected storage owner.</summary>
    event Action<BrowserProfilesSnapshot>? ProfilesReceived;

    /// <summary>Per-TV-profile VPN/network snapshot (never includes WireGuard private keys).</summary>
    event Action<BrowserNetworkSnapshot>? NetworkReceived;

    /// <summary>Receiver-owned browser workspace snapshot (panes, layout, capabilities).</summary>
    event Action<BrowserWorkspaceSnapshot>? WorkspaceReceived;

    /// <summary>Sends a tab operation through the authenticated browser session.</summary>
    Task SendTabCommandAsync(BrowserTabRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sends a view/find operation through the authenticated browser session.</summary>
    Task SendViewCommandAsync(BrowserViewRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sends a bookmark/history operation through the authenticated browser session.</summary>
    Task SendLibraryCommandAsync(BrowserLibraryRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sends a session-only projection of the Windows-device library to the TV.</summary>
    Task SendLibraryStateAsync(BrowserLibrarySnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>Selects a storage owner or manages a named TV profile.</summary>
    Task SendProfileCommandAsync(BrowserProfileRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sets, clears, or refreshes TV-profile VPN/network settings.</summary>
    Task SendNetworkCommandAsync(BrowserNetworkRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Independently gated browser cockpit feature families.</summary>
[Flags]
public enum BrowserCockpitFeatures
{
    /// <summary>No additive cockpit messages are available.</summary>
    None = 0,
    /// <summary>Tab command/state support.</summary>
    Tabs = 1,
    /// <summary>Zoom, user-agent, dark, fullscreen and find support.</summary>
    View = 2,
    /// <summary>Bookmark/history snapshot and mutation support.</summary>
    Library = 4,
    /// <summary>Named TV profiles and a session-only connected-device profile.</summary>
    Profiles = 8,

    /// <summary>Per-TV-profile WireGuard network settings and VPN session status.</summary>
    Network = 16,

    /// <summary>Independent browser workspace panes, layout, media and focused-pane input.</summary>
    Workspace = 32,
}

/// <summary>One TV-owned tab projected into the Windows UI.</summary>
public sealed record BrowserTabSnapshotItem(
    long Id,
    string Title,
    string Url,
    int Progress,
    bool IsLoading,
    bool CanGoBack,
    bool CanGoForward,
    bool IsFrozen);

/// <summary>Atomic tab strip snapshot.</summary>
public sealed record BrowserTabsSnapshot(
    long Epoch,
    long Revision,
    long ActiveTabId,
    IReadOnlyList<BrowserTabSnapshotItem> Tabs);

/// <summary>Tab operations understood by the app layer.</summary>
public enum BrowserTabOperation { New, Close, Select, Move, Duplicate }

/// <summary>One requested tab operation.</summary>
public sealed record BrowserTabRequest(
    BrowserTabOperation Operation,
    long TabId = 0,
    string? Url = null,
    int Position = -1);

/// <summary>View controls understood by the app layer.</summary>
public enum BrowserViewOperation
{
    SetZoom,
    SetUserAgent,
    SetDarkMode,
    SetInputMode,
    SetFullscreen,
    FindStart,
    FindNext,
    FindPrevious,
    FindClear,
}

/// <summary>One requested view operation.</summary>
public sealed record BrowserViewRequest(BrowserViewOperation Operation, int Value = 0, string Text = "");

/// <summary>Receiver view state without coupling the UI to positional wire records.</summary>
public sealed record BrowserViewSnapshot(
    long Epoch,
    long Revision,
    int ZoomPercent,
    BrowserUserAgentMode UserAgent,
    BrowserDarkMode DarkMode,
    BrowserInputMode InputMode,
    bool IsFullscreen,
    bool IsEditing,
    bool IsFindActive,
    int FindCurrent,
    int FindTotal);

/// <summary>TV user-agent choices.</summary>
public enum BrowserUserAgentMode { Tv, Desktop, Mobile }

/// <summary>Dark-page choices, including an honest unsupported state.</summary>
public enum BrowserDarkMode { Off, On, Unavailable }

/// <summary>TV interaction choices.</summary>
public enum BrowserInputMode { Cursor, Focus }

/// <summary>TV library operations.</summary>
public enum BrowserLibraryOperation { AddBookmark, RemoveBookmark, ClearHistory, ClearBookmarks, RequestSnapshot }

/// <summary>One requested library operation.</summary>
public sealed record BrowserLibraryRequest(
    BrowserLibraryOperation Operation,
    string Url = "",
    string Title = "",
    long Epoch = 0,
    long CommandId = 0);

/// <summary>A bounded bookmark or history entry.</summary>
public sealed record BrowserLibraryItem(
    BrowserLibraryItemKind Kind,
    string Url,
    string Title,
    DateTimeOffset LastVisited,
    long FaviconId = 0);

/// <summary>Library entry families.</summary>
public enum BrowserLibraryItemKind { Bookmark, History }

/// <summary>Atomic TV library snapshot.</summary>
public sealed record BrowserLibrarySnapshot(
    long Epoch,
    long Revision,
    IReadOnlyList<BrowserLibraryItem> Entries);

/// <summary>Where bookmarks and history for the active profile are durably stored.</summary>
public enum BrowserProfileStorageLocation { Television, WindowsDevice }

/// <summary>One persistent, named profile reported by the television.</summary>
public sealed record BrowserTvProfile(string ProfileId, string Name);

/// <summary>The receiver-authoritative profile catalog and active storage owner.</summary>
public sealed record BrowserProfilesSnapshot(
    long Epoch,
    long Revision,
    BrowserProfileStorageLocation ActiveStorage,
    string ActiveProfileId,
    string DeviceName,
    IReadOnlyList<BrowserTvProfile> TvProfiles);

/// <summary>Profile operations understood by the app layer.</summary>
public enum BrowserProfileOperation
{
    SelectTvProfile,
    CreateTvProfile,
    RenameTvProfile,
    DeleteTvProfile,
    SelectWindowsDevice,
    RequestSnapshot,
}

/// <summary>One requested profile selection or TV-profile management operation.</summary>
public sealed record BrowserProfileRequest(
    BrowserProfileOperation Operation,
    string ProfileId = "",
    string Name = "");

/// <summary>Network/VPN operations for a named TV profile.</summary>
public enum BrowserNetworkOperation
{
    Set,
    Clear,
    RequestSnapshot,
}

/// <summary>VPN provider choices understood by the app layer.</summary>
public enum BrowserVpnProviderKind
{
    None,
    WireGuard,
}

/// <summary>Soft-fail VPN session kinds for the Windows status line.</summary>
public enum BrowserVpnSessionKind
{
    Idle,
    NeedsConsent,
    Connecting,
    Connected,
    Failed,
    Unavailable,
}

/// <summary>One host-originated network/VPN command.</summary>
public sealed record BrowserNetworkRequest(
    BrowserNetworkOperation Operation,
    string ProfileId = "",
    bool VpnEnabled = false,
    BrowserVpnProviderKind Provider = BrowserVpnProviderKind.None,
    bool AutoConnectOnBrowserStart = false,
    bool RequireVpnBeforeBrowse = false,
    string ConfigText = "");

/// <summary>Receiver network snapshot that never carries WireGuard private keys.</summary>
public sealed record BrowserNetworkSnapshot(
    long Epoch,
    long Revision,
    string ProfileId,
    bool VpnEnabled,
    BrowserVpnProviderKind Provider,
    bool AutoConnectOnBrowserStart,
    bool RequireVpnBeforeBrowse,
    bool ConfigPresent,
    bool CapabilityPreparable,
    string CapabilityReason,
    BrowserVpnSessionKind SessionState,
    string SessionDetail = "");

/// <summary>One storage choice shown by the Windows cockpit.</summary>
public sealed record BrowserProfileOption(
    string ProfileId,
    string Name,
    BrowserProfileStorageLocation Storage,
    bool IsActive,
    string DeviceName = "")
{
    /// <summary>True when this row is the Windows-account library.</summary>
    public bool IsWindowsDevice => Storage == BrowserProfileStorageLocation.WindowsDevice;

    /// <summary>True when this row is a named profile stored on the television.</summary>
    public bool IsTelevision => Storage == BrowserProfileStorageLocation.Television;

    /// <summary>An explicit statement of where bookmarks and history survive the session.</summary>
    public string StorageLabel => Storage == BrowserProfileStorageLocation.Television
        ? "Bookmarks and history stay on this TV"
        : "Bookmarks and history stay on this Windows device";

    /// <summary>Accessible selection state.</summary>
    public string StateLabel => IsActive ? "Active profile" : "Switch to this profile";
}
