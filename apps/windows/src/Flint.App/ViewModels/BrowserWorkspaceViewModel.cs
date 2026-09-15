using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Flint.App.ViewModels;

/// <summary>
/// Windows cockpit state for a receiver-owned browser workspace.
/// </summary>
/// <remarks>
/// <para>
/// The television remains canonical: this view model projects receiver snapshots and emits semantic
/// intents through <see cref="IBrowserWorkspaceCommandSink"/>. It deliberately does not invent a
/// local browser, renderer, media result, or focused input target.
/// </para>
/// <para>
/// Most importantly, a focus click starts a barrier. Text input is disabled until a later receiver
/// snapshot confirms the exact requested pane. That prevents a fast typist from sending text to the
/// previously focused page while focus is in flight.
/// </para>
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The pending-request watchdog is disposed whenever that request resolves, times out or the connection resets; the view model lives as long as the window.")]
public sealed partial class BrowserWorkspaceViewModel : ObservableObject
{
    /// <summary>Enough for a page-sized paste without allowing an accidental unbounded command.</summary>
    public const int MaximumInputCharacters = 4_096;

    /// <summary>
    /// Progress snapshots often arrive before the TV confirms a layout/pane change. After this many
    /// newer snapshots without confirmation, release the pending gate so controls are not bricked.
    /// </summary>
    public const int PendingConfirmationMissLimit = 12;

    /// <summary>
    /// Wall-clock cap for guided Split/Stack when the TV stays on tabs and never republishes
    /// workspace panes (silent OpenPane entry reject left the HUD on WAITING forever).
    /// </summary>
    public static readonly TimeSpan PendingArrangementTimeout = TimeSpan.FromSeconds(6);

    private readonly Dictionary<string, BrowserWorkspacePaneViewModel> panesById =
        new(StringComparer.Ordinal);
    private BrowserWorkspaceCapabilities capabilities = BrowserWorkspaceCapabilities.Unavailable;
    private IBrowserWorkspaceCommandSink? commandSink;
    private long epoch;
    private long revision;
    private bool hasSnapshot;
    private string? confirmedFocusedPaneId;
    private string? pendingFocusPaneId;
    private BrowserWorkspaceLayout? pendingLayout;
    private string? pendingClosePaneId;
    private int paneCountAtCreationRequest;
    private int layoutConfirmationMisses;
    private int paneCreationMisses;
    private long connectionGeneration;
    private DateTime? pendingWaitStartedUtc;
    private Timer? pendingWatchdog;
    /// <summary>
    /// After SPLIT VIEW / STACK grows the pane count, apply this arrangement once the TV reports
    /// enough pages — so the user does not have to press layout chips separately.
    /// </summary>
    private BrowserWorkspaceLayout? arrangementAfterPaneCount;

    /// <summary>Whether independent pages currently own the TV browser surface.</summary>
    public bool HasOpenPanes => IsAvailable && Panes.Count > 0;

    public bool CanChangeInteraction => HasOpenPanes && commandSink is not null && pendingFocusPaneId is null;

    /// <summary>Creates a workspace projection with no controls enabled until a receiver advertises it.</summary>
    public BrowserWorkspaceViewModel(IBrowserWorkspaceCommandSink? commandSink = null)
    {
        this.commandSink = commandSink;
        LayoutOptions =
        [
            new BrowserWorkspaceLayoutOptionViewModel(BrowserWorkspaceLayout.Single, RequestLayoutAsync),
            new BrowserWorkspaceLayoutOptionViewModel(BrowserWorkspaceLayout.TwoColumns, RequestLayoutAsync),
            new BrowserWorkspaceLayoutOptionViewModel(BrowserWorkspaceLayout.TwoRows, RequestLayoutAsync),
            new BrowserWorkspaceLayoutOptionViewModel(BrowserWorkspaceLayout.FourGrid, RequestLayoutAsync),
        ];
    }

    /// <summary>Receiver-ordered independently browsing panes.</summary>
    public ObservableCollection<BrowserWorkspacePaneViewModel> Panes { get; } = [];

    /// <summary>All desktop-supported layout choices; unsupported choices remain visibly disabled.</summary>
    public IReadOnlyList<BrowserWorkspaceLayoutOptionViewModel> LayoutOptions { get; }

    /// <summary>Whether a compatible receiver has actually advertised a workspace.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(InputStatusLabel))]
    [NotifyPropertyChangedFor(nameof(CanAddPane))]
    [NotifyPropertyChangedFor(nameof(CanSendText))]
    [NotifyPropertyChangedFor(nameof(WorkspaceDetail))]
    private bool isAvailable;

    /// <summary>Current receiver-reported workspace arrangement.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(WorkspaceDetail))]
    [NotifyPropertyChangedFor(nameof(MosaicColumnCount))]
    [NotifyPropertyChangedFor(nameof(MosaicRowCount))]
    private BrowserWorkspaceLayout layout = BrowserWorkspaceLayout.Single;

    /// <summary>Uniform-grid columns for the desktop mosaic stage.</summary>
    public int MosaicColumnCount => Layout.MosaicColumns();

    /// <summary>Uniform-grid rows for the desktop mosaic stage.</summary>
    public int MosaicRowCount => Layout.MosaicRows();

    /// <summary>True after a layout intent and before the TV confirms the selected arrangement.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private bool isLayoutRequestPending;

    /// <summary>True after an add-pane intent and before the TV publishes an additional pane.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddPane))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private bool isPaneCreationPending;

    /// <summary>Text waiting to be sent to the receiver-confirmed focused pane.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSendText))]
    private string draftInputText = string.Empty;

    /// <summary>Factual UI-safe failure feedback; no remote exception details are echoed verbatim.</summary>
    [ObservableProperty]
    private string? commandError;

    /// <summary>The exact focus target still awaiting a receiver snapshot, if any.</summary>
    public string? PendingFocusPaneId => pendingFocusPaneId;

    /// <summary>The exact receiver-confirmed input target, if input is allowed.</summary>
    public string? ConfirmedFocusedPaneId => confirmedFocusedPaneId;

    /// <summary>
    /// The pane named by the latest authoritative TV snapshot. Desktop address and navigation
    /// controls bind only to this value, so there is never an ambiguous workspace-wide address bar.
    /// </summary>
    public BrowserWorkspacePaneViewModel? FocusedPane =>
        confirmedFocusedPaneId is { } paneId && panesById.TryGetValue(paneId, out var pane)
            ? pane
            : null;

    /// <summary>Whether text may be sent to a confirmed receiver focus target right now.</summary>
    public bool CanSendConfirmedInput =>
        IsWorkspaceMode && !IsRestoringProfile && IsAvailable &&
        commandSink is not null &&
        pendingFocusPaneId is null &&
        !string.IsNullOrWhiteSpace(confirmedFocusedPaneId) &&
        capabilities.CanSendFocusedPaneInput;

    /// <summary>Whether creating another independently browsing page is presently allowed.</summary>
    public bool CanAddPane =>
        !IsRestoringProfile && IsAvailable &&
        commandSink is not null &&
        capabilities.CanCreatePane &&
        !IsPaneCreationPending &&
        Panes.Count < Math.Max(0, capabilities.MaximumVisiblePanes);

    /// <summary>Whether the draft is safe and targeted enough to send.</summary>
    public bool CanSendText =>
        CanSendConfirmedInput &&
        DraftInputText.Length is > 0 and <= MaximumInputCharacters;

    /// <summary>A receiver-first status line that coaches the next useful action.</summary>
    public string StatusLabel => BrowserWorkspaceCopy.StatusLabel(
        IsAvailable,
        focusRequestPending: pendingFocusPaneId is not null,
        arrangementPending: arrangementAfterPaneCount is not null || IsPaneCreationPending,
        layoutRequestPending: IsLayoutRequestPending,
        Panes.Count,
        Layout);

    /// <summary>Longer coach copy for tooltips — tabs vs split view.</summary>
    public string WorkspaceCoachHint =>
        IsAvailable ? BrowserWorkspaceCopy.CoachHint : BrowserWorkspaceCopy.Unavailable;

    /// <summary>Explains the deliberately conservative control model.</summary>
    public string WorkspaceDetail =>
        IsAvailable ? BrowserWorkspaceCopy.CoachHint : BrowserWorkspaceCopy.UnavailableDetail;

    /// <summary>Explains exactly why desktop text entry is enabled or held behind the focus barrier.</summary>
    public string InputStatusLabel => BrowserWorkspaceCopy.InputStatus(
        IsAvailable,
        pendingFocusPaneName: pendingFocusPaneId is { } pending ? PaneName(pending) : null,
        canSendInput: capabilities.CanSendFocusedPaneInput,
        confirmedFocusPaneName: confirmedFocusedPaneId is { } focused ? PaneName(focused) : null,
        channelConnected: commandSink is not null);

    /// <summary>Binds the future negotiated receiver adapter, without enabling unsupported capabilities.</summary>
    public void Bind(IBrowserWorkspaceCommandSink? sink)
    {
        if (!ReferenceEquals(commandSink, sink)) connectionGeneration++;
        commandSink = sink;
        queuedResize = null;
        OnPropertyChanged(nameof(CanResize));
        RefreshPresentation();
    }

    /// <summary>
    /// Applies one receiver-authoritative snapshot.
    /// </summary>
    /// <returns><see langword="true"/> when the snapshot advanced the projection.</returns>
    public bool ApplySnapshot(BrowserWorkspaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (hasSnapshot && snapshot.Epoch == epoch && snapshot.Revision == revision)
        {
            ApplyGeometry(snapshot);
            return false;
        }
        if (hasSnapshot && (snapshot.Epoch < epoch || snapshot.Epoch == epoch && snapshot.Revision < revision))
        {
            return false;
        }

        var epochChanged = !hasSnapshot || snapshot.Epoch != epoch;
        hasSnapshot = true;
        epoch = snapshot.Epoch;
        revision = snapshot.Revision;
        capabilities = NormalizeCapabilities(snapshot.Capabilities);

        if (epochChanged)
        {
            connectionGeneration++;
            DraftInputText = string.Empty;
            pendingFocusPaneId = null;
            pendingLayout = null;
            arrangementAfterPaneCount = null;
            pendingClosePaneId = null;
            paneCountAtCreationRequest = 0;
            pendingWaitStartedUtc = null;
            pendingWatchdog?.Dispose();
            pendingWatchdog = null;
            layoutConfirmationMisses = 0;
            paneCreationMisses = 0;
            IsLayoutRequestPending = false;
            IsPaneCreationPending = false;
            CommandError = null;
        }

        ApplyGeometry(snapshot);
        IsAvailable = capabilities.IsAvailable;
        Layout = Enum.IsDefined<BrowserWorkspaceLayout>(snapshot.Layout)
            ? snapshot.Layout
            : BrowserWorkspaceLayout.Single;
        if (!IsAvailable)
        {
            ClearUnavailableWorkspace();
            RefreshPresentation();
            return true;
        }

        var paneSnapshots = NormalizePanes(snapshot.Panes);
        var paneIds = paneSnapshots.Select(item => item.PaneId).ToHashSet(StringComparer.Ordinal);
        var reportedFocus = NormalizePaneId(snapshot.FocusedPaneId);
        if (reportedFocus is not null && !paneIds.Contains(reportedFocus))
        {
            // A focus id for a no-longer-present pane is not a safe input target.
            reportedFocus = null;
        }

        ReconcileFocusRequest(reportedFocus, paneIds);
        ReconcileLayoutRequest();
        // Replace panes before reconciling create/close so guided Split/Stack sees the new count.
        ReplacePanes(paneSnapshots);
        ReconcilePaneRequests(paneIds);
        confirmedFocusedPaneId = reportedFocus;
        RefreshPresentation();
        OnPropertyChanged(nameof(CanResize));
        return true;
    }

    /// <summary>
    /// Drops a workspace projection that belongs to a previous browser surface epoch.
    /// </summary>
    /// <remarks>
    /// After Mirror → Web reclaim the host mints a new OPEN epoch. A leftover mosaic snapshot from
    /// the closed epoch still looks clickable, but every command fails revision checks until the
    /// TV republishes under the live epoch.
    /// </remarks>
    public void InvalidateIfStale(long browserEpoch)
    {
        if (!hasSnapshot || browserEpoch <= 0 || epoch == browserEpoch)
        {
            return;
        }

        Flint.Core.FlintDiag.Info(
            "FlintWorkspace",
            $"invalidate stale workspace snapshotEpoch={epoch} browserEpoch={browserEpoch}");
        connectionGeneration++;
        hasSnapshot = false;
        epoch = 0;
        revision = 0;
        capabilities = BrowserWorkspaceCapabilities.Unavailable;
        pendingFocusPaneId = null;
        pendingLayout = null;
        arrangementAfterPaneCount = null;
        pendingClosePaneId = null;
        paneCountAtCreationRequest = 0;
        pendingWaitStartedUtc = null;
        pendingWatchdog?.Dispose();
        pendingWatchdog = null;
        layoutConfirmationMisses = 0;
        paneCreationMisses = 0;
        confirmedFocusedPaneId = null;
        IsAvailable = false;
        Layout = BrowserWorkspaceLayout.Single;
        IsLayoutRequestPending = false;
        IsPaneCreationPending = false;
        DraftInputText = string.Empty;
        CommandError = null;
        ClearUnavailableWorkspace();
        RefreshPresentation();
    }

    /// <summary>Clears receiver-owned projection and keeps all commands safely disabled.</summary>
    public void Reset()
    {
        connectionGeneration++;
        hasSnapshot = false;
        epoch = 0;
        revision = 0;
        capabilities = BrowserWorkspaceCapabilities.Unavailable;
        pendingFocusPaneId = null;
        pendingLayout = null;
        arrangementAfterPaneCount = null;
        pendingClosePaneId = null;
        paneCountAtCreationRequest = 0;
        pendingWaitStartedUtc = null;
        pendingWatchdog?.Dispose();
        pendingWatchdog = null;
        layoutConfirmationMisses = 0;
        paneCreationMisses = 0;
        confirmedFocusedPaneId = null;
        IsAvailable = false;
        Layout = BrowserWorkspaceLayout.Single;
        IsLayoutRequestPending = false;
        IsPaneCreationPending = false;
        DraftInputText = string.Empty;
        CommandError = null;
        Panes.Clear();
        panesById.Clear();
        RefreshPresentation();
    }

    [RelayCommand(CanExecute = nameof(CanAddPane))]
    private async Task AddPaneAsync()
    {
        if (!CanAddPane || commandSink is null)
        {
            return;
        }

        IsPaneCreationPending = true;
        paneCountAtCreationRequest = Panes.Count;
        paneCreationMisses = 0;
        ArmPendingWatchdog();
        CommandError = null;
        RefreshPresentation();
        try
        {
            Flint.Core.FlintDiag.Info("FlintWorkspace", "command OpenPane");
            await commandSink.SendAsync(new CreateBrowserWorkspacePaneCommand()).ConfigureAwait(true);
        }
        catch (Exception)
        {
            ClearPendingArrangement("THE NEW-PANE REQUEST COULD NOT BE SENT TO THE TV");
        }
    }

    /// <summary>One click: open a second page if needed, then arrange side by side.</summary>
    [RelayCommand(CanExecute = nameof(CanSplitBeside))]
    private Task SplitBesideAsync() => ArrangeGuidedAsync(BrowserWorkspaceLayout.TwoColumns);

    /// <summary>One click: open a second page if needed, then stack one above the other.</summary>
    [RelayCommand(CanExecute = nameof(CanStackBelow))]
    private Task StackBelowAsync() => ArrangeGuidedAsync(BrowserWorkspaceLayout.TwoRows);

    public bool CanSplitBeside => CanArrangeGuided(BrowserWorkspaceLayout.TwoColumns);

    public bool CanStackBelow => CanArrangeGuided(BrowserWorkspaceLayout.TwoRows);

    private bool CanArrangeGuided(BrowserWorkspaceLayout layout) =>
        IsAvailable &&
        commandSink is not null &&
        !IsLayoutRequestPending &&
        !IsPaneCreationPending &&
        arrangementAfterPaneCount is null &&
        LayoutAdvertisedByTv(layout) &&
        (layout.FitsPaneCount(Panes.Count)
            ? Layout != layout
            : Panes.Count < layout.VisiblePaneCapacity() &&
              Panes.Count < Math.Max(0, capabilities.MaximumVisiblePanes));

    private async Task ArrangeGuidedAsync(BrowserWorkspaceLayout layout)
    {
        if (!CanArrangeGuided(layout) || commandSink is null)
        {
            return;
        }

        if (layout.FitsPaneCount(Panes.Count))
        {
            await RequestLayoutAsync(layout).ConfigureAwait(true);
            return;
        }

        arrangementAfterPaneCount = layout;
        CommandError = null;
        ArmPendingWatchdog();
        Flint.Core.FlintDiag.Info("FlintWorkspace", $"guided arrange layout={layout} panes={Panes.Count}");
        RefreshPresentation();
        if (!CanAddPane)
        {
            ClearPendingArrangement("CANNOT OPEN ANOTHER PAGE FOR THAT LAYOUT");
            return;
        }

        await AddPaneAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Called when other cockpit families (tabs) keep updating while a mosaic arrange is pending.
    /// If the TV never confirms panes, release WAITING so Split View can be pressed again.
    /// </summary>
    public void ObserveHostActivityWhilePending()
    {
        if (!IsPaneCreationPending && arrangementAfterPaneCount is null)
        {
            return;
        }

        if (pendingWaitStartedUtc is not { } started)
        {
            pendingWaitStartedUtc = DateTime.UtcNow;
            return;
        }

        if (DateTime.UtcNow - started < PendingArrangementTimeout)
        {
            return;
        }

        Flint.Core.FlintDiag.Info(
            "FlintWorkspace",
            $"pending arrangement timed out after {PendingArrangementTimeout.TotalSeconds:0}s panes={Panes.Count}");
        ClearPendingArrangement(
            Panes.Count == 0
                ? "SPLIT VIEW TIMED OUT — TV STAYED ON TABS. TRY AGAIN."
                : "SPLIT VIEW TIMED OUT WAITING FOR THE TV. TRY AGAIN.");
    }

    /// <summary>Test hook: backdate the pending-wait clock without sleeping.</summary>
    internal void SetPendingWaitStartedUtcForTests(DateTime utc) => pendingWaitStartedUtc = utc;

    private void ArmPendingWatchdog()
    {
        pendingWaitStartedUtc ??= DateTime.UtcNow;
        pendingWatchdog?.Dispose();
        // Tab snapshots stop once the TV enters mosaic, so do not rely on ObserveHostActivity alone.
        pendingWatchdog = new Timer(
            _ => Avalonia.Threading.Dispatcher.UIThread.Post(ObserveHostActivityWhilePending),
            null,
            PendingArrangementTimeout,
            Timeout.InfiniteTimeSpan);
    }

    private void ClearPendingArrangement(string? error)
    {
        pendingWatchdog?.Dispose();
        pendingWatchdog = null;
        IsPaneCreationPending = false;
        arrangementAfterPaneCount = null;
        paneCountAtCreationRequest = 0;
        paneCreationMisses = 0;
        pendingWaitStartedUtc = null;
        if (error is not null)
        {
            CommandError = error;
        }

        RefreshPresentation();
    }

    [RelayCommand(CanExecute = nameof(CanSendText))]
    private async Task SendTextAsync()
    {
        var target = confirmedFocusedPaneId;
        var text = DraftInputText;
        var sendingGeneration = connectionGeneration;
        if (!CanSendText || commandSink is null || target is null)
        {
            return;
        }

        try
        {
            CommandError = null;
            await commandSink.SendAsync(new SendBrowserWorkspaceInputCommand(target, text)).ConfigureAwait(true);
            if (sendingGeneration == connectionGeneration && DraftInputText == text) DraftInputText = string.Empty;
        }
        catch (Exception)
        {
            if (sendingGeneration == connectionGeneration) CommandError = "TEXT COULD NOT BE SENT TO THE CONFIRMED TV PANE";
        }
        finally
        {
            RefreshPresentation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelFocusWait))]
    private void CancelFocusWait()
    {
        pendingFocusPaneId = null;
        CommandError = "FOCUS REQUEST CANCELLED; INPUT REMAINS TARGETED ONLY TO THE LAST TV-CONFIRMED PANE";
        RefreshPresentation();
    }

    private bool CanCancelFocusWait() => pendingFocusPaneId is not null;

    private async Task RequestFocusAsync(BrowserWorkspacePaneViewModel pane)
    {
        if (!IsAvailable || commandSink is null || !capabilities.CanRequestPaneFocus ||
            pane.IsFocused || pane.IsFocusPending || !panesById.ContainsKey(pane.PaneId))
        {
            return;
        }

        pendingFocusPaneId = pane.PaneId;
        CommandError = null;
        RefreshPresentation();
        try
        {
            Flint.Core.FlintDiag.Info("FlintWorkspace", "command FocusPane");
            await commandSink.SendAsync(new FocusBrowserWorkspacePaneCommand(pane.PaneId)).ConfigureAwait(true);
        }
        catch (Exception)
        {
            if (StringComparer.Ordinal.Equals(pendingFocusPaneId, pane.PaneId))
            {
                pendingFocusPaneId = null;
                CommandError = "THE FOCUS REQUEST COULD NOT BE SENT TO THE TV";
                RefreshPresentation();
            }
        }
    }

    private async Task RequestLayoutAsync(BrowserWorkspaceLayout requested)
    {
        if (!CanRequestLayout(requested) || commandSink is null)
        {
            return;
        }

        pendingLayout = requested;
        IsLayoutRequestPending = true;
        layoutConfirmationMisses = 0;
        CommandError = null;
        RefreshPresentation();
        try
        {
            Flint.Core.FlintDiag.Info("FlintWorkspace", $"command SetLayout layout={requested}");
            await commandSink.SendAsync(new SetBrowserWorkspaceLayoutCommand(requested)).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            Flint.Core.FlintDiag.Warn("FlintWorkspace", $"command SetLayout failed: {exception.Message}");
            if (pendingLayout == requested)
            {
                pendingLayout = null;
                IsLayoutRequestPending = false;
                CommandError = "THE LAYOUT REQUEST COULD NOT BE SENT TO THE TV";
                RefreshPresentation();
            }
        }
    }

    private async Task RequestMediaAsync(
        BrowserWorkspacePaneViewModel pane,
        BrowserWorkspaceMediaActions action)
    {
        if (!IsAvailable || commandSink is null || !capabilities.CanRequestMediaControl ||
            action == BrowserWorkspaceMediaActions.None || !panesById.ContainsKey(pane.PaneId))
        {
            return;
        }

        pane.MarkMediaRequestPending(action);
        CommandError = null;
        try
        {
            await commandSink.SendAsync(new RequestBrowserWorkspaceMediaCommand(pane.PaneId, action))
                .ConfigureAwait(true);
        }
        catch (Exception)
        {
            pane.MarkMediaRequestFailed("MEDIA REQUEST COULD NOT BE SENT TO THE TV");
        }
    }

    private async Task RequestCloseAsync(BrowserWorkspacePaneViewModel pane)
    {
        if (!CanClosePane(pane) || commandSink is null)
        {
            return;
        }

        pendingClosePaneId = pane.PaneId;
        CommandError = null;
        RefreshPresentation();
        try
        {
            await commandSink.SendAsync(new CloseBrowserWorkspacePaneCommand(pane.PaneId)).ConfigureAwait(true);
        }
        catch (Exception)
        {
            if (StringComparer.Ordinal.Equals(pendingClosePaneId, pane.PaneId))
            {
                pendingClosePaneId = null;
                CommandError = "THE CLOSE-PANE REQUEST COULD NOT BE SENT TO THE TV";
                RefreshPresentation();
            }
        }
    }

    private async Task RequestMoveAsync(BrowserWorkspacePaneViewModel pane, int targetSlot)
    {
        if (IsRestoringProfile || !IsAvailable || commandSink is null || !panesById.ContainsKey(pane.PaneId) ||
            targetSlot < 0 || targetSlot >= Panes.Count || targetSlot == pane.Slot)
        {
            return;
        }

        CommandError = null;
        try
        {
            await commandSink.SendAsync(new MoveBrowserWorkspacePaneCommand(pane.PaneId, targetSlot))
                .ConfigureAwait(true);
        }
        catch (Exception)
        {
            CommandError = "THE PANE COULD NOT BE REORDERED. RECONNECT AND TRY AGAIN.";
        }
    }

    private void ReplacePanes(IReadOnlyList<BrowserWorkspacePaneSnapshot> snapshots)
    {
        var next = new List<BrowserWorkspacePaneViewModel>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            if (!panesById.TryGetValue(snapshot.PaneId, out var pane))
            {
                pane = new BrowserWorkspacePaneViewModel(
                    snapshot.PaneId,
                    RequestFocusAsync,
                    RequestMediaAsync,
                    RequestCloseAsync,
                    RequestPageAsync,
                    RequestMoveAsync);
                panesById.Add(snapshot.PaneId, pane);
            }

            var inputPermitted = CanSendConfirmedInput &&
                StringComparer.Ordinal.Equals(confirmedFocusedPaneId, snapshot.PaneId);
            pane.Apply(
                snapshot,
                focused: StringComparer.Ordinal.Equals(confirmedFocusedPaneId, snapshot.PaneId),
                focusPending: StringComparer.Ordinal.Equals(pendingFocusPaneId, snapshot.PaneId),
                EffectiveCapabilities,
                inputPermitted,
                closePermitted: CanClosePaneId(snapshot.PaneId, snapshots.Count),
                paneCount: snapshots.Count);
            next.Add(pane);
        }

        var present = snapshots.Select(item => item.PaneId).ToHashSet(StringComparer.Ordinal);
        foreach (var removed in panesById.Keys.Where(id => !present.Contains(id)).ToArray())
        {
            panesById.Remove(removed);
        }

        // Preserve item containers and keyboard focus when only progress/title/media changes.
        for (var index = Panes.Count - 1; index >= 0; index--)
        {
            if (!present.Contains(Panes[index].PaneId)) Panes.RemoveAt(index);
        }
        for (var index = 0; index < next.Count; index++)
        {
            var existing = Panes.IndexOf(next[index]);
            if (existing < 0) Panes.Insert(index, next[index]);
            else if (existing != index) Panes.Move(existing, index);
        }
    }

    private void ReconcileFocusRequest(string? reportedFocus, ISet<string> paneIds)
    {
        if (pendingFocusPaneId is not { } pending)
        {
            return;
        }

        if (StringComparer.Ordinal.Equals(pending, reportedFocus))
        {
            pendingFocusPaneId = null;
            return;
        }

        if (!paneIds.Contains(pending))
        {
            pendingFocusPaneId = null;
            CommandError = "THE REQUESTED PANE IS NO LONGER AVAILABLE ON THE TV";
        }
    }

    private void ReconcileLayoutRequest()
    {
        if (pendingLayout is not { } requested)
        {
            layoutConfirmationMisses = 0;
            return;
        }

        if (requested == Layout)
        {
            pendingLayout = null;
            IsLayoutRequestPending = false;
            layoutConfirmationMisses = 0;
            return;
        }

        // Progress / media snapshots often keep the previous layout while the TV is still applying.
        // Do not treat those as rejection — that cleared pending immediately and left chips inert.
        layoutConfirmationMisses++;
        if (layoutConfirmationMisses < PendingConfirmationMissLimit)
        {
            return;
        }

        pendingLayout = null;
        IsLayoutRequestPending = false;
        layoutConfirmationMisses = 0;
        CommandError = "THE TV DID NOT CONFIRM THAT LAYOUT REQUEST";
    }

    private void ReconcilePaneRequests(ISet<string> reportedPaneIds)
    {
        if (IsPaneCreationPending && reportedPaneIds.Count > paneCountAtCreationRequest)
        {
            IsPaneCreationPending = false;
            paneCountAtCreationRequest = 0;
            paneCreationMisses = 0;
            if (arrangementAfterPaneCount is null)
            {
                pendingWaitStartedUtc = null;
            }

            _ = ContinueGuidedArrangementAsync();
        }
        else if (IsPaneCreationPending)
        {
            paneCreationMisses++;
            if (paneCreationMisses >= PendingConfirmationMissLimit)
            {
                ClearPendingArrangement("THE TV DID NOT OPEN A NEW PANE");
            }
        }
        else
        {
            _ = ContinueGuidedArrangementAsync();
        }

        if (pendingClosePaneId is { } pending && !reportedPaneIds.Contains(pending))
        {
            pendingClosePaneId = null;
        }
    }

    private async Task ContinueGuidedArrangementAsync()
    {
        if (arrangementAfterPaneCount is not { } desired || commandSink is null || IsPaneCreationPending)
        {
            return;
        }

        if (!desired.FitsPaneCount(Panes.Count))
        {
            if (Panes.Count < desired.VisiblePaneCapacity() && CanAddPane)
            {
                await AddPaneAsync().ConfigureAwait(true);
            }
            else if (Panes.Count >= desired.VisiblePaneCapacity())
            {
                // Too many panes for this arrangement — stop coaching rather than closing pages.
                ClearPendingArrangement("CLOSE A PAGE BEFORE THAT LAYOUT CAN APPLY");
            }

            return;
        }

        arrangementAfterPaneCount = null;
        pendingWaitStartedUtc = null;
        pendingWatchdog?.Dispose();
        pendingWatchdog = null;
        if (Layout != desired)
        {
            await RequestLayoutAsync(desired).ConfigureAwait(true);
        }
        else
        {
            RefreshPresentation();
        }
    }

    private void ClearUnavailableWorkspace()
    {
        pendingFocusPaneId = null;
        pendingLayout = null;
        arrangementAfterPaneCount = null;
        pendingClosePaneId = null;
        paneCountAtCreationRequest = 0;
        pendingWaitStartedUtc = null;
        pendingWatchdog?.Dispose();
        pendingWatchdog = null;
        layoutConfirmationMisses = 0;
        paneCreationMisses = 0;
        confirmedFocusedPaneId = null;
        IsLayoutRequestPending = false;
        IsPaneCreationPending = false;
        DraftInputText = string.Empty;
        Panes.Clear();
        panesById.Clear();
    }

    private void RefreshPresentation()
    {
        OnPropertyChanged(nameof(PendingFocusPaneId));
        OnPropertyChanged(nameof(ConfirmedFocusedPaneId));
        OnPropertyChanged(nameof(FocusedPane));
        OnPropertyChanged(nameof(CanSendConfirmedInput));
        OnPropertyChanged(nameof(CanAddPane));
        OnPropertyChanged(nameof(CanSendText));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(WorkspaceDetail));
        OnPropertyChanged(nameof(InputStatusLabel));
        OnPropertyChanged(nameof(HasOpenPanes));
        OnPropertyChanged(nameof(CanChangeInteraction));
        OnPropertyChanged(nameof(MosaicColumnCount));
        OnPropertyChanged(nameof(MosaicRowCount));
        AddPaneCommand.NotifyCanExecuteChanged();
        SplitBesideCommand.NotifyCanExecuteChanged();
        StackBelowCommand.NotifyCanExecuteChanged();
        SendTextCommand.NotifyCanExecuteChanged();
        CancelFocusWaitCommand.NotifyCanExecuteChanged();
        EnterPageCommand.NotifyCanExecuteChanged();
        WorkspaceControlsCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSplitBeside));
        OnPropertyChanged(nameof(CanStackBelow));
        OnPropertyChanged(nameof(WorkspaceCoachHint));
        OnPropertyChanged(nameof(WorkspaceDetail));

        foreach (var option in LayoutOptions)
        {
            var fits = option.Layout.FitsPaneCount(Panes.Count);
            option.Apply(
                supported: LayoutAdvertisedByTv(option.Layout) && fits,
                selected: Layout == option.Layout,
                canSend: CanRequestLayout(option.Layout),
                availabilityReason: LayoutAvailabilityReason(option.Layout));
        }

        // A focus request disables every input target, including the old focus, before the command
        // round trip completes. Reapplying panes is the single place that derives this invariant.
        foreach (var pane in Panes)
        {
            pane.Apply(
                new BrowserWorkspacePaneSnapshot(
                    pane.PaneId,
                    pane.Slot,
                    pane.Url,
                    pane.Title,
                    pane.Progress,
                    pane.State,
                    pane.MediaSnapshot,
                    pane.IsPageFullscreen,
                    pane.CanGoBack,
                    pane.CanGoForward),
                focused: StringComparer.Ordinal.Equals(confirmedFocusedPaneId, pane.PaneId),
                focusPending: StringComparer.Ordinal.Equals(pendingFocusPaneId, pane.PaneId),
                EffectiveCapabilities,
                inputPermitted: CanSendConfirmedInput &&
                    StringComparer.Ordinal.Equals(confirmedFocusedPaneId, pane.PaneId),
                closePermitted: CanClosePaneId(pane.PaneId, Panes.Count),
                paneCount: Panes.Count,
                receiverSnapshot: false);
        }
    }

    private bool CanRequestLayout(BrowserWorkspaceLayout requested) =>
        IsAvailable &&
        commandSink is not null &&
        !IsLayoutRequestPending &&
        Layout != requested &&
        capabilities.Supports(requested) &&
        capabilities.MaximumVisiblePanes >= requested.VisiblePaneCapacity() &&
        requested.FitsPaneCount(Panes.Count);

    private bool LayoutAdvertisedByTv(BrowserWorkspaceLayout layout) =>
        capabilities.Supports(layout) &&
        capabilities.MaximumVisiblePanes >= layout.VisiblePaneCapacity();

    private string LayoutAvailabilityReason(BrowserWorkspaceLayout layout) =>
        BrowserWorkspaceCopy.LayoutAvailability(
            layout,
            advertisedByTv: LayoutAdvertisedByTv(layout),
            fitsPaneCount: layout.FitsPaneCount(Panes.Count),
            currentLayout: Layout);

    private BrowserWorkspaceCapabilities EffectiveCapabilities => commandSink is null
        ? capabilities with
        {
            IsAvailable = false,
            CanCreatePane = false,
            CanClosePane = false,
            CanRequestPaneFocus = false,
            CanSendFocusedPaneInput = false,
            CanRequestMediaControl = false,
            CanRequestTheaterMode = false,
        }
        : capabilities;

    private bool CanClosePane(BrowserWorkspacePaneViewModel pane) =>
        CanClosePaneId(pane.PaneId, Panes.Count) && panesById.ContainsKey(pane.PaneId);

    private bool CanClosePaneId(string paneId, int count) =>
        IsAvailable &&
        commandSink is not null &&
        capabilities.CanClosePane &&
        count > 1 &&
        !StringComparer.Ordinal.Equals(pendingClosePaneId, paneId);

    private static BrowserWorkspaceCapabilities NormalizeCapabilities(BrowserWorkspaceCapabilities? source)
    {
        if (source is not { IsAvailable: true })
        {
            return BrowserWorkspaceCapabilities.Unavailable;
        }

        return source with
        {
            MaximumVisiblePanes = Math.Max(0, source.MaximumVisiblePanes),
            SupportedLayouts = source.SupportedLayouts &
                (BrowserWorkspaceLayoutSet.Single |
                 BrowserWorkspaceLayoutSet.TwoColumns |
                 BrowserWorkspaceLayoutSet.TwoRows |
                 BrowserWorkspaceLayoutSet.FourGrid),
        };
    }

    private static IReadOnlyList<BrowserWorkspacePaneSnapshot> NormalizePanes(
        IReadOnlyList<BrowserWorkspacePaneSnapshot>? source) =>
        (source ?? [])
            .Select(item => item.Normalize())
            .Where(item => !string.IsNullOrWhiteSpace(item.PaneId))
            .GroupBy(item => item.PaneId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.Slot)
            .ThenBy(item => item.PaneId, StringComparer.Ordinal)
            .ToArray();

    private static string? NormalizePaneId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private string PaneName(string paneId) => panesById.TryGetValue(paneId, out var pane)
        ? pane.DisplayTitle
        : "the requested pane";

    private async Task RequestPageAsync(BrowserWorkspacePaneViewModel pane, BrowserWorkspacePageAction action, string address)
    {
        if (commandSink is null || !IsAvailable || !panesById.ContainsKey(pane.PaneId)) return;
        var url = action == BrowserWorkspacePageAction.Navigate
            ? Flint.App.Services.BrowserAddressBarResolver.TryResolve(address) : string.Empty;
        if (url is null)
        {
            CommandError = "Enter a website address or search for this pane.";
            return;
        }
        await SendSafelyAsync(new BrowserWorkspacePageCommand(pane.PaneId, action, url));
    }

    [RelayCommand(CanExecute = nameof(CanChangeInteraction))]
    private Task EnterPageAsync() => SendSafelyAsync(new BrowserWorkspaceInteractionCommand(true));

    [RelayCommand(CanExecute = nameof(CanChangeInteraction))]
    private Task WorkspaceControlsAsync() => SendSafelyAsync(new BrowserWorkspaceInteractionCommand(false));

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task RefreshAsync() => SendSafelyAsync(new RefreshBrowserWorkspaceCommand());

    internal async Task<bool> SendConfirmedInputAsync(Flint.Protocol.BrowserInputEvent input, CancellationToken cancellationToken)
    {
        if (!CanSendConfirmedInput || confirmedFocusedPaneId is not { } target) return false;
        BrowserWorkspaceCommand? command = input switch
        {
            Flint.Protocol.BrowserTextInput text => new SendBrowserWorkspaceInputCommand(target, text.Text),
            Flint.Protocol.BrowserSemanticKeyInput key => new SendBrowserWorkspaceKeyCommand(target, key.Key),
            _ => null,
        };
        return command is not null && await SendSafelyAsync(command, cancellationToken);
    }

    private async Task<bool> SendSafelyAsync(BrowserWorkspaceCommand command, CancellationToken cancellationToken = default)
    {
        if (commandSink is null) return false;
        var sendingGeneration = connectionGeneration;
        try
        {
            CommandError = null;
            await commandSink.SendAsync(command, cancellationToken).ConfigureAwait(true);
            return sendingGeneration == connectionGeneration;
        }
        catch (Exception)
        {
            if (sendingGeneration == connectionGeneration) CommandError = "The request could not reach the TV. Reconnect and try again.";
            return false;
        }
    }
}
