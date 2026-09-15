namespace Flint.App.ViewModels;

/// <summary>
/// A receiver-owned browser workspace projected into the Windows cockpit.
/// </summary>
/// <remarks>
/// These types intentionally do not use a wire type. They form the stable UI boundary that a
/// versioned cockpit binding can adapt to once the receiver protocol supports workspaces. Keeping
/// that adaptation at the edge prevents a half-negotiated receiver from making the desktop claim
/// that panes, media controls, or input targeting exist.
/// </remarks>
public sealed record BrowserWorkspaceSnapshot(
    long Epoch,
    long Revision,
    BrowserWorkspaceCapabilities Capabilities,
    BrowserWorkspaceLayout Layout,
    string? FocusedPaneId,
    IReadOnlyList<BrowserWorkspacePaneSnapshot> Panes,
    bool CanResize = false,
    int ColumnSplit = 5000,
    int RowSplit = 5000,
    bool IsWorkspaceMode = true,
    bool SupportsCustomization = false)
{
    /// <summary>An explicit unavailable snapshot used while no compatible receiver is connected.</summary>
    public static BrowserWorkspaceSnapshot Unavailable { get; } = new(
        Epoch: 0,
        Revision: 0,
        Capabilities: BrowserWorkspaceCapabilities.Unavailable,
        Layout: BrowserWorkspaceLayout.Single,
        FocusedPaneId: null,
        Panes: []);
}

/// <summary>Capabilities the receiver has actually advertised for its browser workspace.</summary>
public sealed record BrowserWorkspaceCapabilities(
    bool IsAvailable,
    int MaximumVisiblePanes,
    BrowserWorkspaceLayoutSet SupportedLayouts,
    bool CanCreatePane,
    bool CanClosePane,
    bool CanRequestPaneFocus,
    bool CanSendFocusedPaneInput,
    bool CanRequestMediaControl,
    bool CanRequestTheaterMode)
{
    /// <summary>No controls are enabled until a receiver advertises workspace support.</summary>
    public static BrowserWorkspaceCapabilities Unavailable { get; } = new(
        IsAvailable: false,
        MaximumVisiblePanes: 0,
        SupportedLayouts: BrowserWorkspaceLayoutSet.None,
        CanCreatePane: false,
        CanClosePane: false,
        CanRequestPaneFocus: false,
        CanSendFocusedPaneInput: false,
        CanRequestMediaControl: false,
        CanRequestTheaterMode: false);

    /// <summary>Whether the receiver explicitly supports the supplied layout.</summary>
    public bool Supports(BrowserWorkspaceLayout layout) =>
        IsAvailable && SupportedLayouts.HasFlag(layout.ToCapability());
}

/// <summary>The visible arrangement of independently browsing TV panes.</summary>
public enum BrowserWorkspaceLayout
{
    /// <summary>One pane fills the workspace.</summary>
    Single,

    /// <summary>Two panes side by side.</summary>
    TwoColumns,

    /// <summary>Two panes stacked vertically.</summary>
    TwoRows,

    /// <summary>Four independently browsing panes.</summary>
    FourGrid,
}

/// <summary>Receiver-advertised workspace layout choices.</summary>
[Flags]
public enum BrowserWorkspaceLayoutSet
{
    /// <summary>No layout support was advertised.</summary>
    None = 0,

    /// <summary>One pane fills the workspace.</summary>
    Single = 1 << 0,

    /// <summary>Two side-by-side panes.</summary>
    TwoColumns = 1 << 1,

    /// <summary>Two vertically stacked panes.</summary>
    TwoRows = 1 << 2,

    /// <summary>A two-by-two grid.</summary>
    FourGrid = 1 << 3,
}

/// <summary>Converts a layout into the capability that must be present before it is offered.</summary>
public static class BrowserWorkspaceLayoutExtensions
{
    /// <summary>Gets the corresponding advertised capability.</summary>
    public static BrowserWorkspaceLayoutSet ToCapability(this BrowserWorkspaceLayout layout) => layout switch
    {
        BrowserWorkspaceLayout.Single => BrowserWorkspaceLayoutSet.Single,
        BrowserWorkspaceLayout.TwoColumns => BrowserWorkspaceLayoutSet.TwoColumns,
        BrowserWorkspaceLayout.TwoRows => BrowserWorkspaceLayoutSet.TwoRows,
        BrowserWorkspaceLayout.FourGrid => BrowserWorkspaceLayoutSet.FourGrid,
        _ => BrowserWorkspaceLayoutSet.None,
    };

    /// <summary>Gets the number of panes a layout can visibly place.</summary>
    public static int VisiblePaneCapacity(this BrowserWorkspaceLayout layout) => layout switch
    {
        BrowserWorkspaceLayout.Single => 1,
        BrowserWorkspaceLayout.TwoColumns or BrowserWorkspaceLayout.TwoRows => 2,
        BrowserWorkspaceLayout.FourGrid => 4,
        _ => 0,
    };

    /// <summary>
    /// Same pane-count rule the TV reducer uses: 0/1 → single only; 2 → splits; 3/4 → grid.
    /// </summary>
    /// <summary>Uniform-grid columns for the desktop mosaic stage.</summary>
    public static int MosaicColumns(this BrowserWorkspaceLayout layout) => layout switch
    {
        BrowserWorkspaceLayout.TwoColumns or BrowserWorkspaceLayout.FourGrid => 2,
        _ => 1,
    };

    /// <summary>Uniform-grid rows for the desktop mosaic stage.</summary>
    public static int MosaicRows(this BrowserWorkspaceLayout layout) => layout switch
    {
        BrowserWorkspaceLayout.TwoRows or BrowserWorkspaceLayout.FourGrid => 2,
        _ => 1,
    };

    public static bool FitsPaneCount(this BrowserWorkspaceLayout layout, int paneCount) => paneCount switch
    {
        0 or 1 => layout == BrowserWorkspaceLayout.Single,
        2 => layout is BrowserWorkspaceLayout.TwoColumns or BrowserWorkspaceLayout.TwoRows,
        3 or 4 => layout == BrowserWorkspaceLayout.FourGrid,
        _ => false,
    };

    /// <summary>A short label that says arrangement rather than a video-specific term.</summary>
    public static string Label(this BrowserWorkspaceLayout layout) => layout switch
    {
        BrowserWorkspaceLayout.Single => "1",
        BrowserWorkspaceLayout.TwoColumns => "2 COL",
        BrowserWorkspaceLayout.TwoRows => "2 ROW",
        BrowserWorkspaceLayout.FourGrid => "4",
        _ => "—",
    };

    /// <summary>Longer name for tooltips and status lines.</summary>
    public static string DisplayName(this BrowserWorkspaceLayout layout) => layout switch
    {
        BrowserWorkspaceLayout.Single => "One pane",
        BrowserWorkspaceLayout.TwoColumns => "Side by side",
        BrowserWorkspaceLayout.TwoRows => "Stacked",
        BrowserWorkspaceLayout.FourGrid => "Four-pane grid",
        _ => "Unavailable",
    };
}

/// <summary>A single independently browsing page shown in a receiver workspace.</summary>
public sealed record BrowserWorkspacePaneSnapshot(
    string PaneId,
    int Slot,
    string Url,
    string Title,
    int Progress,
    BrowserWorkspacePaneState State,
    BrowserWorkspaceMediaSnapshot Media,
    bool IsPageFullscreen = false,
    bool CanGoBack = false,
    bool CanGoForward = false)
{
    /// <summary>Normalizes receiver values before they become visible state.</summary>
    public BrowserWorkspacePaneSnapshot Normalize() => this with
    {
        PaneId = PaneId?.Trim() ?? string.Empty,
        Slot = Math.Max(0, Slot),
        Url = Url?.Trim() ?? string.Empty,
        Title = Title?.Trim() ?? string.Empty,
        Progress = Math.Clamp(Progress, 0, 100),
        Media = Media ?? BrowserWorkspaceMediaSnapshot.None,
    };
}

/// <summary>Receiver-reported renderer state for a page, rather than an invented media state.</summary>
public enum BrowserWorkspacePaneState
{
    /// <summary>The receiver did not publish a more specific state.</summary>
    Unknown,

    /// <summary>The page is loading.</summary>
    Loading,

    /// <summary>The page has a live renderer.</summary>
    Live,

    /// <summary>The receiver intentionally suspended the page.</summary>
    Suspended,

    /// <summary>The renderer or navigation failed.</summary>
    Failed,
}

/// <summary>Media evidence and allowed request types for one page.</summary>
public sealed record BrowserWorkspaceMediaSnapshot(
    BrowserWorkspacePlaybackState Playback,
    BrowserWorkspaceMuteState Mute,
    BrowserWorkspaceMediaActions AllowedActions)
{
    /// <summary>No media capability is assumed for arbitrary pages.</summary>
    public static BrowserWorkspaceMediaSnapshot None { get; } = new(
        BrowserWorkspacePlaybackState.Unknown,
        BrowserWorkspaceMuteState.Unknown,
        BrowserWorkspaceMediaActions.None);

    /// <summary>Whether the receiver has granted any media request for this particular pane.</summary>
    public bool IsControllable => AllowedActions != BrowserWorkspaceMediaActions.None;
}

/// <summary>Playback state reported by the page or receiver.</summary>
public enum BrowserWorkspacePlaybackState
{
    /// <summary>Neither the receiver nor page reported a playback state.</summary>
    Unknown,

    /// <summary>The receiver reports playback is active.</summary>
    Playing,

    /// <summary>The receiver reports playback is paused.</summary>
    Paused,
}

/// <summary>Audio state reported by the page or receiver.</summary>
public enum BrowserWorkspaceMuteState
{
    /// <summary>Neither the receiver nor page reported a mute state.</summary>
    Unknown,

    /// <summary>The page's audio is audible.</summary>
    Audible,

    /// <summary>The receiver reports this pane is muted.</summary>
    Muted,
}

/// <summary>Media requests an individual page has explicitly allowed.</summary>
[Flags]
public enum BrowserWorkspaceMediaActions
{
    /// <summary>No media request is available.</summary>
    None = 0,

    /// <summary>Ask the page to begin playback.</summary>
    Play = 1 << 0,

    /// <summary>Ask the page to pause playback.</summary>
    Pause = 1 << 1,

    /// <summary>Ask the receiver to mute the page's audio.</summary>
    Mute = 1 << 2,

    /// <summary>Ask the receiver to unmute the page's audio.</summary>
    Unmute = 1 << 3,

    /// <summary>Ask a page with an unknown reported playback state to toggle playback.</summary>
    TogglePlayback = 1 << 4,

    /// <summary>Ask a page with an unknown reported mute state to toggle its own audio state.</summary>
    ToggleMute = 1 << 5,
}

/// <summary>An intent from the Windows cockpit for a later versioned protocol adapter to send.</summary>
public abstract record BrowserWorkspaceCommand;

/// <summary>Sets bounded divider fractions in ten-thousandths.</summary>
public sealed record ResizeBrowserWorkspaceCommand(int Column, int Row) : BrowserWorkspaceCommand;

/// <summary>Switches presentation while retaining both sets of pages.</summary>
public sealed record SwitchBrowserWorkspaceModeCommand(bool Workspace, int Column, int Row) : BrowserWorkspaceCommand;

/// <summary>Asks the receiver to focus one pane. Input remains blocked until a snapshot confirms it.</summary>
public sealed record FocusBrowserWorkspacePaneCommand(string PaneId) : BrowserWorkspaceCommand;

/// <summary>Asks the receiver to change the workspace arrangement.</summary>
public sealed record SetBrowserWorkspaceLayoutCommand(BrowserWorkspaceLayout Layout) : BrowserWorkspaceCommand;

/// <summary>Asks the receiver to create another independently browsing page.</summary>
public sealed record CreateBrowserWorkspacePaneCommand(string Url = "") : BrowserWorkspaceCommand;

/// <summary>Asks the receiver to close an independently browsing page.</summary>
public sealed record CloseBrowserWorkspacePaneCommand(string PaneId) : BrowserWorkspaceCommand;

/// <summary>Moves one pane to another slot without recreating its renderer.</summary>
public sealed record MoveBrowserWorkspacePaneCommand(string PaneId, int TargetSlot) : BrowserWorkspaceCommand;

/// <summary>Asks one exact pane for a media operation; it never addresses global device audio.</summary>
public sealed record RequestBrowserWorkspaceMediaCommand(
    string PaneId,
    BrowserWorkspaceMediaActions Action) : BrowserWorkspaceCommand;

/// <summary>Sends text only to the pane the receiver has confirmed as focused.</summary>
public sealed record SendBrowserWorkspaceInputCommand(
    string PaneId,
    string Text) : BrowserWorkspaceCommand;

/// <summary>A portable key sent to one receiver-confirmed page.</summary>
public sealed record SendBrowserWorkspaceKeyCommand(string PaneId, Flint.Protocol.BrowserSemanticKey Key)
    : BrowserWorkspaceCommand;

/// <summary>A page navigation operation addressed to a stable pane identity.</summary>
public enum BrowserWorkspacePageAction { Navigate, Reload, Back, Forward }

/// <summary>Per-pane navigation; address resolution happens before transport.</summary>
public sealed record BrowserWorkspacePageCommand(string PaneId, BrowserWorkspacePageAction Action, string Url = "")
    : BrowserWorkspaceCommand;

/// <summary>Switches the TV between workspace controls and page interaction.</summary>
public sealed record BrowserWorkspaceInteractionCommand(bool InteractWithPage) : BrowserWorkspaceCommand;

/// <summary>Refreshes authoritative state after a lost or rejected operation.</summary>
public sealed record RefreshBrowserWorkspaceCommand : BrowserWorkspaceCommand;

/// <summary>Abstraction for the future negotiated cockpit-to-receiver workspace binding.</summary>
public interface IBrowserWorkspaceCommandSink
{
    /// <summary>Sends a semantic workspace intent without exposing a wire format to the UI.</summary>
    Task SendAsync(BrowserWorkspaceCommand command, CancellationToken cancellationToken = default);
}
