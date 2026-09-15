using Flint.Protocol;
using Flint.App.ViewModels;
using Flint.Session.Browser;

namespace Flint.App.Services;

/// <summary>
/// Carries the additive cockpit families — tabs, view settings and the library — over an existing
/// authenticated browser session.
/// </summary>
/// <remarks>
/// Kept apart from <see cref="SecureBrowserRemote"/> because these message families are negotiated
/// separately: an older receiver speaks the browser protocol perfectly and knows nothing about tabs,
/// and the cockpit must report that honestly rather than sending commands into silence.
///
/// It also does the translation. The UI works in <see cref="BrowserTabsSnapshot"/> and friends
/// rather than in positional wire records, so a wire field moving does not reach a XAML binding, and
/// a UI concept the wire has no field for — an unsupported dark mode, say — has somewhere to live.
/// </remarks>
public sealed class SecureBrowserCockpitRemote : IBrowserCockpitRemote, IDisposable
{
    private readonly BrowserSession session;
    private readonly Func<long> epoch;
    private readonly Func<long> nextCommandId;
    private readonly object workspaceRevisionGate = new();
    private long latestWorkspaceEpoch;
    private long latestWorkspaceRevision;
    private bool disposed;
    private BrowserWorkspaceSnapshot? latestWorkspace;
    private bool geometryAdvertised;
    private bool exclusivePresentation;

    /// <summary>Wraps <paramref name="session"/> using the caller's epoch and command numbering.</summary>
    /// <remarks>
    /// Identifiers are borrowed rather than owned: every host-to-receiver message on this session
    /// shares one ordered command sequence, and a second counter here would let a tab command and a
    /// navigation claim the same number.
    /// </remarks>
    public SecureBrowserCockpitRemote(
        BrowserSession session,
        Func<long> epoch,
        Func<long> nextCommandId)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.epoch = epoch ?? throw new ArgumentNullException(nameof(epoch));
        this.nextCommandId = nextCommandId ?? throw new ArgumentNullException(nameof(nextCommandId));

        WorkspaceCommandSink = new BrowserWorkspaceCommandSinkAdapter(
            session,
            epoch,
            nextCommandId,
            GetLatestWorkspaceRevision,
            () => !disposed && CockpitFeatures.HasFlag(BrowserCockpitFeatures.Workspace),
            () => !disposed && geometryAdvertised && !exclusivePresentation);

        session.TabsReceived += OnTabs;
        session.ViewReceived += OnView;
        session.LibraryReceived += OnLibrary;
        session.LibraryRequestReceived += OnDeviceLibraryRequest;
        session.ProfileReceived += OnProfiles;
        session.NetworkReceived += OnNetwork;
        session.WorkspaceReceived += OnWorkspace;
        session.GeometryReceived += OnGeometry;
    }

    /// <summary>
    /// The cockpit families this receiver has actually demonstrated.
    /// </summary>
    /// <remarks>
    /// Discovered rather than declared. The capability message predates these families and has no
    /// field for them, and guessing the wrong way is not symmetric: a receiver that does not know a
    /// message type closes the session rather than ignoring it, so sending a tab command
    /// speculatively would disconnect the very people it was meant to help.
    ///
    /// A family therefore turns on when the receiver sends its first snapshot of it — the receiver
    /// volunteers what it can do, and until then the panel says so instead of offering controls
    /// that would end the session.
    /// </remarks>
    public BrowserCockpitFeatures CockpitFeatures { get; private set; } = BrowserCockpitFeatures.None;

    /// <summary>Raised when a family becomes available for the first time.</summary>
    public event Action<BrowserCockpitFeatures>? FeaturesChanged;

    /// <inheritdoc />
    public event Action<BrowserTabsSnapshot>? TabsReceived;

    /// <inheritdoc />
    public event Action<BrowserViewSnapshot>? ViewReceived;

    /// <inheritdoc />
    public event Action<BrowserLibrarySnapshot>? LibraryReceived;

    /// <inheritdoc />
    public event Action<BrowserLibraryRequest>? DeviceLibraryRequestReceived;

    /// <inheritdoc />
    public event Action<BrowserProfilesSnapshot>? ProfilesReceived;

    /// <inheritdoc />
    public event Action<BrowserNetworkSnapshot>? NetworkReceived;

    /// <inheritdoc />
    public event Action<BrowserWorkspaceSnapshot>? WorkspaceReceived;

    /// <summary>Semantic workspace command sink bound to this authenticated session.</summary>
    public IBrowserWorkspaceCommandSink WorkspaceCommandSink { get; }

    /// <inheritdoc />
    public Task SendTabCommandAsync(BrowserTabRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CockpitFeatures.HasFlag(BrowserCockpitFeatures.Tabs))
        {
            return Task.CompletedTask;
        }

        var currentEpoch = epoch();
        if (currentEpoch <= 0)
        {
            Flint.Core.FlintDiag.Warn("FlintBrowser", "tab command skipped: receiver epoch not adopted yet");
            return Task.CompletedTask;
        }

        return session.SendTabCommandAsync(
            new BrowserTabCommandMessage(
                currentEpoch,
                nextCommandId(),
                request.Operation switch
                {
                    BrowserTabOperation.New => BrowserTabAction.New,
                    BrowserTabOperation.Close => BrowserTabAction.Close,
                    BrowserTabOperation.Select => BrowserTabAction.Select,
                    BrowserTabOperation.Move => BrowserTabAction.Move,
                    BrowserTabOperation.Duplicate => BrowserTabAction.Duplicate,
                    _ => throw new ArgumentOutOfRangeException(nameof(request)),
                },
                request.TabId,
                request.Url),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SendViewCommandAsync(BrowserViewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CockpitFeatures.HasFlag(BrowserCockpitFeatures.View))
        {
            return Task.CompletedTask;
        }

        return session.SendViewCommandAsync(
            new BrowserViewCommandMessage(
                epoch(),
                nextCommandId(),
                request.Operation switch
                {
                    BrowserViewOperation.SetZoom => BrowserViewAction.SetZoom,
                    BrowserViewOperation.SetUserAgent => BrowserViewAction.SetUa,
                    BrowserViewOperation.SetDarkMode => BrowserViewAction.SetDark,
                    BrowserViewOperation.SetInputMode => BrowserViewAction.SetInputMode,
                    BrowserViewOperation.SetFullscreen => BrowserViewAction.SetFullscreen,
                    BrowserViewOperation.FindStart => BrowserViewAction.FindStart,
                    BrowserViewOperation.FindNext => BrowserViewAction.FindNext,
                    BrowserViewOperation.FindPrevious => BrowserViewAction.FindPrev,
                    BrowserViewOperation.FindClear => BrowserViewAction.FindClear,
                    _ => throw new ArgumentOutOfRangeException(nameof(request)),
                },
                request.Value,
                request.Text),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SendLibraryCommandAsync(
        BrowserLibraryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CockpitFeatures.HasFlag(BrowserCockpitFeatures.Library))
        {
            return Task.CompletedTask;
        }

        return session.SendLibraryCommandAsync(
            new BrowserLibraryCommandMessage(
                epoch(),
                nextCommandId(),
                request.Operation switch
                {
                    BrowserLibraryOperation.AddBookmark => BrowserLibraryAction.AddBookmark,
                    BrowserLibraryOperation.RemoveBookmark => BrowserLibraryAction.RemoveBookmark,
                    BrowserLibraryOperation.ClearHistory => BrowserLibraryAction.ClearHistory,
                    BrowserLibraryOperation.ClearBookmarks => BrowserLibraryAction.ClearBookmarks,
                    BrowserLibraryOperation.RequestSnapshot => BrowserLibraryAction.RequestSnapshot,
                    _ => throw new ArgumentOutOfRangeException(nameof(request)),
                },
                request.Url,
                request.Title),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SendLibraryStateAsync(
        BrowserLibrarySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!CockpitFeatures.HasFlag(BrowserCockpitFeatures.Library) &&
            !CockpitFeatures.HasFlag(BrowserCockpitFeatures.Profiles))
        {
            return Task.CompletedTask;
        }

        return session.SendLibraryStateAsync(
            BrowserCockpitWireMapper.ToLibraryState(snapshot),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SendProfileCommandAsync(
        BrowserProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CockpitFeatures.HasFlag(BrowserCockpitFeatures.Profiles))
        {
            return Task.CompletedTask;
        }

        return session.SendProfileCommandAsync(
            BrowserCockpitWireMapper.ToProfileCommand(request, epoch(), nextCommandId()),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SendNetworkCommandAsync(
        BrowserNetworkRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Always send. Network support is discovered from the TV's reply; gating here left the
        // paste UI permanently locked when the first snapshot was skipped (no epoch yet).
        return session.SendNetworkCommandAsync(
            BrowserCockpitWireMapper.ToNetworkCommand(request, epoch(), nextCommandId()),
            cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        session.TabsReceived -= OnTabs;
        session.ViewReceived -= OnView;
        session.LibraryReceived -= OnLibrary;
        session.LibraryRequestReceived -= OnDeviceLibraryRequest;
        session.ProfileReceived -= OnProfiles;
        session.NetworkReceived -= OnNetwork;
        session.WorkspaceReceived -= OnWorkspace;
        session.GeometryReceived -= OnGeometry;
    }

    private void Discover(BrowserCockpitFeatures family)
    {
        if (CockpitFeatures.HasFlag(family))
        {
            return;
        }

        CockpitFeatures |= family;
        FeaturesChanged?.Invoke(CockpitFeatures);
    }

    private void OnTabs(BrowserTabStateMessage message)
    {
        Discover(BrowserCockpitFeatures.Tabs);
        TabsReceived?.Invoke(
            new BrowserTabsSnapshot(
                message.Epoch,
                message.Revision,
                message.ActiveTabId,
                [.. message.Tabs.Select(tab => new BrowserTabSnapshotItem(
                    tab.TabId,
                    tab.Title,
                    tab.Url,
                    tab.Progress,
                    tab.LoadState == BrowserLoadState.Loading,
                    tab.CanGoBack,
                    tab.CanGoForward,
                    tab.Frozen))]));
    }

    private void OnView(BrowserViewStateMessage message)
    {
        Discover(BrowserCockpitFeatures.View);
        ViewReceived?.Invoke(
            new BrowserViewSnapshot(
                message.Epoch,
                message.Revision,
                message.ZoomPercent,
                message.UaMode switch
                {
                    Protocol.BrowserUserAgentMode.Desktop => BrowserUserAgentMode.Desktop,
                    Protocol.BrowserUserAgentMode.Mobile => BrowserUserAgentMode.Mobile,
                    _ => BrowserUserAgentMode.Tv,
                },
                // The wire says which style the page is being shown in; the cockpit only offers a
                // switch. "Unavailable" is reserved for a receiver that cannot darken at all, which
                // is a capability answer rather than a state one.
                message.DarkMode == Protocol.BrowserDarkMode.Dark
                    ? BrowserDarkMode.On
                    : BrowserDarkMode.Off,
                message.InputMode == BrowserInteractionMode.Focus
                    ? BrowserInputMode.Focus
                    : BrowserInputMode.Cursor,
                message.Fullscreen,
                message.EditingFocused,
                message.FindActive,
                message.FindCurrent,
                message.FindTotal));
    }

    private void OnLibrary(BrowserLibraryStateMessage message)
    {
        Discover(BrowserCockpitFeatures.Library);
        LibraryReceived?.Invoke(
            new BrowserLibrarySnapshot(
                message.Epoch,
                message.Revision,
                // Bookmarks first, then history. The wire keeps them apart because they are bounded
                // separately; the cockpit shows one ordered list and reads the kind off each row.
                [.. message.Bookmarks.Concat(message.History).Select(ToLibraryItem)]));
    }

    private static BrowserLibraryItem ToLibraryItem(BrowserLibraryEntry entry) =>
        new(
            entry.Kind == BrowserLibraryEntryKind.Bookmark
                ? BrowserLibraryItemKind.Bookmark
                : BrowserLibraryItemKind.History,
            entry.Url,
            entry.Title,
            DateTimeOffset.FromUnixTimeMilliseconds(
                Math.Clamp(entry.LastVisitedMilliseconds, 0, DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())),
            entry.FaviconId);

    private void OnDeviceLibraryRequest(BrowserLibraryCommandMessage message)
    {
        Discover(BrowserCockpitFeatures.Library);
        DeviceLibraryRequestReceived?.Invoke(BrowserCockpitWireMapper.ToDeviceLibraryRequest(message));
    }

    private void OnProfiles(BrowserProfileStateMessage message)
    {
        Discover(BrowserCockpitFeatures.Profiles);
        ProfilesReceived?.Invoke(BrowserCockpitWireMapper.ToProfiles(message));
    }

    private void OnNetwork(BrowserNetworkStateMessage message)
    {
        Discover(BrowserCockpitFeatures.Network);
        NetworkReceived?.Invoke(BrowserCockpitWireMapper.ToNetwork(message));
    }

    private void OnWorkspace(BrowserWorkspaceStateMessage message)
    {
        lock (workspaceRevisionGate)
        {
            // Adopt equal revisions too: empty close/open republishes after Mirror reclaim bump the
            // page epoch while revision may restart or match the previous watermark.
            if (message.Epoch > latestWorkspaceEpoch ||
                message.Epoch == latestWorkspaceEpoch && message.Revision >= latestWorkspaceRevision)
            {
                latestWorkspaceEpoch = message.Epoch;
                latestWorkspaceRevision = message.Revision;
            }
        }

        Discover(BrowserCockpitFeatures.Workspace);
        if (latestWorkspace is { } previous && (message.Epoch < previous.Epoch ||
            message.Epoch == previous.Epoch && message.Revision < previous.Revision)) return;
        if (latestWorkspace?.Epoch != message.Epoch) geometryAdvertised = false;
        exclusivePresentation = message.TheaterPaneId != 0 || message.PageFullscreenPaneId != 0;
        latestWorkspace = BrowserCockpitWireMapper.ToWorkspace(message) with
        {
            CanResize = geometryAdvertised && !exclusivePresentation && (latestWorkspace?.IsWorkspaceMode ?? true),
            IsWorkspaceMode = latestWorkspace?.IsWorkspaceMode ?? true,
            SupportsCustomization = geometryAdvertised,
            ColumnSplit = geometryAdvertised ? latestWorkspace?.ColumnSplit ?? 5000 : 5000,
            RowSplit = geometryAdvertised ? latestWorkspace?.RowSplit ?? 5000 : 5000,
        };
        WorkspaceReceived?.Invoke(latestWorkspace);
    }

    private void OnGeometry(BrowserWorkspaceGeometryMessage message)
    {
        if (latestWorkspace is not { } current || message.Epoch != current.Epoch || message.Revision != current.Revision) return;
        geometryAdvertised = true;
        latestWorkspace = current with { CanResize = !exclusivePresentation && message.Mode != 1,
            ColumnSplit = message.Column, RowSplit = message.Row, IsWorkspaceMode = message.Mode != 1, SupportsCustomization = true };
        WorkspaceReceived?.Invoke(latestWorkspace);
    }

    private long GetLatestWorkspaceRevision()
    {
        lock (workspaceRevisionGate)
        {
            var currentEpoch = epoch();
            if (latestWorkspaceRevision <= 0)
            {
                throw new IOException("No authoritative TV workspace revision is available for this session.");
            }

            if (latestWorkspaceEpoch != currentEpoch)
            {
                throw new IOException(
                    $"TV workspace revision is for a previous browser epoch ({latestWorkspaceEpoch}); live epoch is {currentEpoch}.");
            }

            return latestWorkspaceRevision;
        }
    }
}
