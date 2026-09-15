using System.ComponentModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.App.Controls;
using Flint.App.Services;
using Flint.Core;
using Flint.Protocol;
using Flint.Session.Browser;

namespace Flint.App.ViewModels;

/// <summary>
/// Presents the browser experiment's independently assessed receiver eligibility and secure-session
/// readiness. Navigation stays disabled until a pinned TLS BrowserSession is verified.
/// </summary>
public sealed partial class BrowserPageViewModel
    : ObservableObject, IDisposable, IBrowserInputHost, IBrowserTabSessionHost
{
    private readonly CastPageViewModel cast;
    private readonly IBrowserSessionConnector connector;
    private readonly IBrowserTrustStore trustStore;
    private readonly IBrowserUiDispatcher uiDispatcher;
    private readonly BrowserPreviewDecoder previewDecoder;
    private ISecureBrowserRemote? session;
    private long nextCommandId = 1;
    /// <summary>
    /// Browser epoch for the active surface. Each OPEN must be strictly newer than any epoch the
    /// receiver has already observed — including after CLOSE or a dead TLS session — so this value
    /// only ever advances and is never reset to 1 on reconnect.
    /// </summary>
    private long browserEpoch;
    /// <summary>
    /// The number carried by the next input, and never reused.
    ///
    /// The receiver drops any input whose sequence does not advance — that is how it discards a
    /// duplicate or a replay — so a repeated number here is a key that silently does nothing.
    /// </summary>
    private bool browserSurfaceOpen;

    private readonly BrowserKeyboardCapture capture = new();
    private readonly BrowserCockpitBinding cockpit;
    private readonly BrowserInputController input;
    private readonly BrowserTabSessionRestorer tabSession;
    private readonly BrowserReceiverStateApplier receiverState;
    private readonly BrowserReconnectController reconnect;
    private readonly BrowserSessionCommands sessionCommands;
    private readonly BrowserSecureSessionController secureSession;
    private readonly BrowserNavigator navigator;
    private readonly BrowserPageObservers observers;
    /// <summary>
    /// Navigation / frame id the receiver currently expects on pointer and scroll input.
    /// </summary>
    /// <remarks>
    /// The TV rejects stale geometry rather than clicking the wrong page, so every touch must carry
    /// the id of the page that is actually showing. Updated from Open/Navigate command ids and from
    /// later state snapshots.
    /// </remarks>
    private long activeNavigationId;
    private ModeSessionCoordinator? coordinator;
    private bool disposed;
    private bool reconnectInFlight;
    private bool hasRememberedTrust;
    /// <summary>
    /// URLs for the profile session strip captured when Close yields the glass (Mirror/Media).
    /// Used to rebuild distinct tabs after TV ids die — including two Google searches that only
    /// differ by query string.
    /// </summary>
    /// <summary>
    /// Whether this secure session already asked the TV to honour the default-on preview preference
    /// for the current open surface. Reconnect adopts an open page without Navigate, so without this
    /// Windows sits on WAITING FOR TV PREVIEW while the receiver stays Disabled (2026-09-08).
    /// </summary>
    private bool previewPreferenceSyncedForOpenSurface;

    /// <summary>Builds a browser presentation that follows the Cast probe's selected receiver.</summary>
    public BrowserPageViewModel(
        CastPageViewModel cast,
        IBrowserSessionConnector? connector = null,
        IBrowserTrustStore? trustStore = null,
        IBrowserUiDispatcher? uiDispatcher = null,
        IBrowserProfileLibraryStore? profileLibraryStore = null,
        BrowserHelpViewModel? help = null)
    {
        this.cast = cast ?? throw new ArgumentNullException(nameof(cast));
        Help = help ?? new BrowserHelpViewModel();
        this.connector = connector ?? new BrowserSessionConnector();
        // Persisted by default. Verification asks a person to compare a fingerprint on two screens;
        // that is reasonable once and corrosive every launch, because someone asked to compare
        // codes daily stops comparing and starts accepting. An in-memory default made the desktop
        // meet every receiver as a stranger each time it started.
        this.trustStore = trustStore ?? BrowserTrustStoreLocation.Open();
        this.uiDispatcher = uiDispatcher ?? new AvaloniaBrowserUiDispatcher();
        Library = new BrowserLibraryViewModel(
            profileLibraryStore ?? BrowserProfileLibraryStoreLocation.Open());
        Network = new BrowserNetworkViewModel();
        Workspace = new BrowserWorkspaceViewModel();
        previewDecoder = new BrowserPreviewDecoder(
            this.uiDispatcher,
            onDecoded: OnPreviewDecoded,
            onDropped: Preview.MarkDropped,
            onFailure: exception => SetError(BrowserUiError.FromException(exception, BrowserUiErrorKind.Preview)));
        input = new BrowserInputController(this);
        receiverState = new BrowserReceiverStateApplier(this);
        reconnect = new BrowserReconnectController(this);
        sessionCommands = new BrowserSessionCommands(this);
        secureSession = new BrowserSecureSessionController(this);
        navigator = new BrowserNavigator(this);
        // Before the cockpit: the cockpit binding takes this restorer's gate as a delegate.
        tabSession = new BrowserTabSessionRestorer(this);
        cockpit = new BrowserCockpitBinding(
            this.uiDispatcher,
            Tabs,
            ViewControls,
            Library,
            Network,
            Workspace,
            receiverState.ApplyActiveTab,
            BeforeProfileTabCommandAsync);
        observers = new BrowserPageObservers(this, cast);
        observers.Attach();
        SyncBrowserPortFromEvidence();
        _ = reconnect.RequestAsync();
    }

    /// <summary>TV-owned tabs, or one honest legacy projection while tab messages are unavailable.</summary>
    public BrowserTabsViewModel Tabs { get; } = new();

    /// <summary>Opt-in, latest-only TV preview.</summary>
    public BrowserPreviewViewModel Preview { get; } = new();

    /// <summary>Bookmarks and history owned by the selected TV profile or this Windows device.</summary>
    public BrowserLibraryViewModel Library { get; }

    /// <summary>Per-TV-profile WireGuard settings — paste lives here; the tunnel runs on the TV.</summary>
    public BrowserNetworkViewModel Network { get; }

    /// <summary>Receiver-owned multi-pane workspace projection; enabled only after a snapshot arrives.</summary>
    public BrowserWorkspaceViewModel Workspace { get; }

    /// <summary>Replayable contextual guidance that never opens automatically.</summary>
    public BrowserHelpViewModel Help { get; }

    /// <summary>Receiver-reflected zoom, UA, display and find settings.</summary>
    public BrowserViewControlsViewModel ViewControls { get; } = new();

    /// <summary>The one active native page dialog.</summary>
    public BrowserDialogViewModel Dialog { get; } = new();

    /// <summary>Wires the shell-level exclusive-surface coordinator.</summary>
    internal void AttachCoordinator(ModeSessionCoordinator modeCoordinator) =>
        coordinator = modeCoordinator ?? throw new ArgumentNullException(nameof(modeCoordinator));

    /// <summary>Whether the TV browser surface was opened from this PC.</summary>
    public bool HasOpenBrowserSurface => browserSurfaceOpen;

    /// <summary>Where trusted receivers are remembered between runs.</summary>
    internal IBrowserTrustStore TrustStore => trustStore;

    /// <summary>The exact evidence-backed eligibility verdict, or the neutral pre-probe verdict.</summary>
    public BrowserCapability Capability =>
        EffectiveDevice() is { } device
            ? BrowserCapabilityAssessor.Assess(device)
            : BrowserCapability.Unverified;

    /// <summary>The closed availability outcome for diagnostics and presentation.</summary>
    public BrowserAvailability Availability => Capability.Availability;

    /// <summary>Short, user-facing status for the page heading.</summary>
    public string StatusLabel => BrowserVerdictCopy.StatusLabel(SessionPhase, Availability);

    /// <summary>Semantic status colour; eligibility is signal, every unavailable state remains neutral.</summary>
    public Tone HeadingTone => SessionPhase is BrowserUiPhase.SecureReady || IsEligible
        ? Tone.Signal
        : Tone.Neutral;

    /// <summary>The canonical safe explanation, shared with diagnostics rather than rephrased here.</summary>
    public string Reason => IsReconnecting
        ? BrowserSecureReceiverPrompt.ReconnectingReason
        : BrowserVerdictCopy.Reason(SessionPhase, Capability, LastError);

    /// <summary>
    /// The first input a person still has to supply, in the order they would supply them.
    /// </summary>
    internal BrowserSecureReceiverStep SecureReceiverStep => BrowserSecureReceiverPrompt.Step(
        SessionPhase,
        IsEligible,
        HasRememberedTrust,
        HasUsablePairingCode,
        BrowserEndpointResolver.TryResolve(EffectiveDevice(), TryParseManualBrowserPort()) is not null);

    /// <summary>The exact safe next step, naming where it is taken — or null when there is none.</summary>
    public string? Remedy => SessionPhase is BrowserUiPhase.Mismatch or BrowserUiPhase.SecureReady
        ? BrowserVerdictCopy.Remedy(SessionPhase, Capability)
        : BrowserSecureReceiverPrompt.Remedy(SecureReceiverStep, Capability.Remedy);

    /// <summary>Whether a remedy line should occupy space in the page.</summary>
    public bool HasRemedy => !string.IsNullOrWhiteSpace(Remedy);

    /// <summary>Whether the selected receiver passed the local eligibility gates.</summary>
    public bool IsEligible => Capability.IsEligible;

    /// <summary>Whether address entry and Go are available after a verified TLS session.</summary>
    public bool CanNavigate => SessionPhase == BrowserUiPhase.SecureReady && HasLiveSession;

    /// <summary>
    /// Whether Verify / reconnect may be offered once routing (advertised or typed browser port) and
    /// a Cast pairing code are present.
    /// </summary>
    public bool CanVerify =>
        SessionPhase is BrowserUiPhase.Idle or BrowserUiPhase.Mismatch
        && IsEligible
        && HasUsablePairingCode
        && BrowserEndpointResolver.TryResolve(EffectiveDevice(), TryParseManualBrowserPort()) is not null;

    /// <summary>Whether a durable SPKI pin already exists for the default receiver identity.</summary>
    public bool HasRememberedTrust => hasRememberedTrust;

    /// <summary>Primary action label: first-use verify vs returning-user reconnect.</summary>
    public string SecureReceiverActionLabel =>
        HasRememberedTrust ? "CONNECT SECURE BROWSER" : "VERIFY SECURE RECEIVER";

    /// <summary>Instructions above the secure-receiver action; states standing trust, then the step.</summary>
    public string SecureReceiverInstructions =>
        BrowserSecureReceiverPrompt.Instructions(SecureReceiverStep, HasRememberedTrust);

    /// <summary>Whether the Cast page already carries a six-digit pairing code.</summary>
    public bool HasUsablePairingCode
    {
        get
        {
            var code = cast.PairingCode?.Trim() ?? string.Empty;
            return code.Length == 6 && code.All(char.IsDigit);
        }
    }

    /// <summary>
    /// Whether the secure-receiver card should occupy layout space.
    /// </summary>
    /// <remarks>
    /// Absent when nothing is missing: a remembered pin plus routing plus a pairing code means
    /// Windows reconnects unattended, and a card asking for a click it does not need reads as a
    /// step the person has to complete.
    /// </remarks>
    public bool ShowSecureReceiverCard =>
        cast.Report?.Device is not null
        && BrowserSecureReceiverPrompt.ShowsCard(SessionPhase, SecureReceiverStep);

    /// <summary>Whether the browser-port box is a stand-in for discovery rather than noise.</summary>
    public bool ShowBrowserPortEntry =>
        BrowserSecureReceiverPrompt.ShowsPortEntry(
            cast.Report?.Device?.BrowserEvidence?.SecureEndpointPort is not null);

    /// <summary>Whether an unattended reconnect is in flight, so the page can say so.</summary>
    /// <remarks>
    /// Without this the page sits on the pre-connect explanation while Windows is already dialling,
    /// which reads as nothing happening.
    /// </remarks>
    public bool IsReconnecting => reconnectInFlight;

    /// <summary>Current secure-session UI phase.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(HeadingTone))]
    [NotifyPropertyChangedFor(nameof(Reason))]
    [NotifyPropertyChangedFor(nameof(Remedy))]
    [NotifyPropertyChangedFor(nameof(HasRemedy))]
    [NotifyPropertyChangedFor(nameof(CanVerify))]
    [NotifyPropertyChangedFor(nameof(CanNavigate))]
    [NotifyPropertyChangedFor(nameof(ShowNavigation))]
    [NotifyPropertyChangedFor(nameof(ShowSecureReceiverCard))]
    [NotifyPropertyChangedFor(nameof(ShowBrowserPortEntry))]
    private BrowserUiPhase sessionPhase = BrowserUiPhase.Idle;

    /// <summary>Fingerprint display code shown during first-use verification.</summary>
    [ObservableProperty]
    private string? fingerprintDisplay;

    /// <summary>Last safe error detail for mismatch / rejection.</summary>
    [ObservableProperty]
    private string? lastError;

    /// <summary>Typed, actionable failure shown in the cockpit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private BrowserUiError? currentError;

    /// <summary>Whether an actionable failure panel is visible.</summary>
    public bool HasError => CurrentError is not null;

    /// <summary>Whether page-scoped keyboard input is being captured for the television.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyboardCaptureLabel))]
    private bool keyboardCaptureEnabled;

    /// <summary>Visible capture state; Escape always turns it off.</summary>
    public string KeyboardCaptureLabel => capture.Label;

    /// <summary>Text in the find bar. It is sent only when the user explicitly starts a find.</summary>
    [ObservableProperty]
    private string findText = string.Empty;

    /// <summary>Whether the destructive clear-data confirmation is open.</summary>
    [ObservableProperty]
    private bool clearDataConfirmationVisible;

    /// <summary>
    /// Optional TLS browser listener port read from the TV when mDNS did not deliver
    /// <c>browser_port</c> (common on locked-down Wi-Fi).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Capability))]
    [NotifyPropertyChangedFor(nameof(Availability))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(HeadingTone))]
    [NotifyPropertyChangedFor(nameof(Reason))]
    [NotifyPropertyChangedFor(nameof(Remedy))]
    [NotifyPropertyChangedFor(nameof(HasRemedy))]
    [NotifyPropertyChangedFor(nameof(IsEligible))]
    [NotifyPropertyChangedFor(nameof(CanVerify))]
    [NotifyCanExecuteChangedFor(nameof(VerifySecureReceiverCommand))]
    private string manualBrowserPort = string.Empty;

    /// <summary>HTTPS address destined for the TV-resident WebView.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NavigateCommand))]
    private string address = string.Empty;

    /// <summary>Bounded safe title reported by the receiver, never a URL dump.</summary>
    [ObservableProperty]
    private string? pageTitle;

    /// <summary>Bounded progress percent from the TV WebView, when known.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageStatusLabel))]
    private int? pageProgress;

    /// <summary>Whether the page on the television has somewhere to go back to.</summary>
    /// <remarks>
    /// Reported by the receiver rather than inferred from what this side sent. A page that redirects
    /// changes its own history, so a count kept here would disagree with the television within one
    /// navigation and offer a Back button that does nothing.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    private bool canGoBack;

    /// <summary>Whether the page on the television has somewhere to go forward to.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoForwardCommand))]
    private bool canGoForward;

    /// <summary>Text being composed for the page on the television.</summary>
    /// <remarks>
    /// Owned here rather than read out of the text box, so the page never reaches into its own
    /// visual tree by name — which is both fragile and the reason the same control name could
    /// collide across two instances of this page.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendComposedTextCommand))]
    private string composedText = string.Empty;

    /// <summary>Whether the television is currently loading a page.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageStatusLabel))]
    private bool isLoading;

    /// <summary>The address the receiver says it is actually showing.</summary>
    /// <remarks>
    /// Kept apart from <see cref="Address"/>, which is what the user typed. Showing the typed text
    /// as though it were the current page hides redirects and failed loads.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageStatusLabel))]
    private string? currentUrl;

    /// <summary>One line describing what the television is doing, for the page's status row.</summary>
    public string PageStatusLabel => IsLoading
        ? $"Loading… {PageProgress ?? 0}%"
        : CurrentUrl is { Length: > 0 } url ? url : "Nothing open";

    /// <summary>Whether the navigation card should occupy layout space.</summary>
    public bool ShowNavigation => SessionPhase == BrowserUiPhase.SecureReady;

    /// <summary>
    /// Workspace panes own the stage once the TV has advertised open pages. Everything else is HUD.
    /// </summary>
    public bool ShowWorkspaceMosaic => ShowNavigation && Workspace.HasOpenPanes && Workspace.IsWorkspaceMode;

    /// <summary>Large preview stage when there is no workspace mosaic to fill the stage.</summary>
    public bool ShowAimStage => ShowNavigation && (!Workspace.HasOpenPanes || !Workspace.IsWorkspaceMode);

    /// <summary>Secondary library / VPN / find / clear drawer over the HUD stage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MorePanelToggleLabel))]
    private bool isMorePanelOpen;

    /// <summary>Accessible label for the MORE CONTROLS HUD toggle.</summary>
    public string MorePanelToggleLabel => IsMorePanelOpen ? "Close extra controls" : "Open extra controls";

    /// <summary>Opens or closes the secondary HUD drawer.</summary>
    [RelayCommand]
    private void ToggleMorePanel() => IsMorePanelOpen = !IsMorePanelOpen;

    /// <summary>
    /// Opens the pinned TLS browser session. First use accepts trust via the Verify click; a
    /// remembered pin reconnects without treating the click as a new fingerprint comparison.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanVerify))]
    private Task VerifySecureReceiverAsync(CancellationToken cancellationToken) =>
        secureSession.ConnectAsync(allowFirstUseAccept: true, cancellationToken);

    /// <summary>
    /// Sends Open/Navigate for the address bar over the pinned TLS session.
    /// </summary>
    /// <remarks>
    /// Plain search text becomes a Google results URL; host-like text gets https://. Enter in the
    /// address field and GO both call this path.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private Task NavigateAsync(CancellationToken cancellationToken) =>
        navigator.NavigateAsync(cancellationToken);

    /// <summary>
    /// Sends one navigation key to the page on the television.
    /// </summary>
    /// <remarks>
    /// The keys the protocol carries are semantic — up, select, page down — rather than key codes,
    /// so the desktop never has to know what a Fire TV expects. The receiver maps them to whatever
    /// its platform uses.
    /// </remarks>
    [RelayCommand]
    private Task SendKeyAsync(BrowserSemanticKey key, CancellationToken cancellationToken) =>
        input.SendKeyAsync(key, cancellationToken);

    /// <summary>
    /// Types text into whatever the page has focused.
    /// </summary>
    /// <remarks>
    /// Sent whole rather than a character at a time. A search phrase typed key by key is a round
    /// trip per letter, which on a remote page is visible as the text arriving behind the typing.
    /// </remarks>
    [RelayCommand]
    private Task SendTextAsync(string? text, CancellationToken cancellationToken) =>
        input.SendTextAsync(text, cancellationToken);

    /// <summary>Sends the composed text to the television, then clears it.</summary>
    /// <remarks>
    /// Cleared on success so a second press does not send the phrase twice, which on a search page
    /// silently doubles the query.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanSendComposedText))]
    private async Task SendComposedTextAsync(CancellationToken cancellationToken)
    {
        var text = ComposedText;
        await input.SendTextAsync(text, cancellationToken).ConfigureAwait(true);
        if (LastError is null)
        {
            ComposedText = string.Empty;
        }
    }

    /// <summary>Whether there is text to send and somewhere to send it.</summary>
    private bool CanSendComposedText => CanNavigate && !string.IsNullOrEmpty(ComposedText);

    /// <summary>Whether pointer / scroll input can be sent to the TV page.</summary>
    public bool CanSendTouch => CanNavigate && activeNavigationId > 0;

    /// <summary>Whether the interactive preview surface should accept pointer/scroll.</summary>
    public bool CanInteractWithPreview => CanSendTouch && Preview.IsRequested && Preview.HasFrame;

    /// <summary>Sends a touch/mouse pointer sample mapped to the page open on the television.</summary>
    public Task SendPointerAsync(
        BrowserPointerAction action,
        int x,
        int y,
        int buttons,
        CancellationToken cancellationToken = default) =>
        SendPointerAsync(action, x, y, buttons, navigationId: 0, frameId: 0, cancellationToken);

    /// <summary>Sends a pointer sample with explicit refs (0 = resolve from the live preview).</summary>
    public Task SendPointerAsync(
        BrowserPointerAction action,
        int x,
        int y,
        int buttons,
        long navigationId,
        long frameId,
        CancellationToken cancellationToken = default) =>
        input.SendPointerAsync(action, x, y, buttons, navigationId, frameId, cancellationToken);

    /// <summary>Sends a scroll gesture at a pad point, in pixels the page should move.</summary>
    public Task SendScrollAsync(
        int x,
        int y,
        int deltaX,
        int deltaY,
        CancellationToken cancellationToken = default) =>
        SendScrollAsync(x, y, deltaX, deltaY, navigationId: 0, frameId: 0, cancellationToken);

    /// <summary>Sends scroll with explicit refs (0 = resolve from the live preview).</summary>
    public Task SendScrollAsync(
        int x,
        int y,
        int deltaX,
        int deltaY,
        long navigationId,
        long frameId,
        CancellationToken cancellationToken = default) =>
        input.SendScrollAsync(x, y, deltaX, deltaY, navigationId, frameId, cancellationToken);

    ISecureBrowserRemote? IBrowserInputHost.Session => session;

    long IBrowserInputHost.Epoch => browserEpoch;

    long IBrowserInputHost.ActiveNavigationId => activeNavigationId;

    void IBrowserInputHost.ReportInputResult(string? failure) => LastError = failure;

    /// <summary>Moves the television's page back one history entry.</summary>
    /// <remarks>
    /// A history command rather than a navigation to the previous address: navigating would push a
    /// *new* entry, so Back would walk forwards forever.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanGoBackNow))]
    private Task GoBackAsync(CancellationToken cancellationToken) =>
        navigator.SendActionAsync(BrowserCommandAction.Back, cancellationToken);

    /// <summary>Moves the television's page forward one history entry.</summary>
    [RelayCommand(CanExecute = nameof(CanGoForwardNow))]
    private Task GoForwardAsync(CancellationToken cancellationToken) =>
        navigator.SendActionAsync(BrowserCommandAction.Forward, cancellationToken);

    /// <summary>Reloads the page on the television.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnOpenPage))]
    private Task ReloadAsync(CancellationToken cancellationToken) =>
        navigator.SendActionAsync(BrowserCommandAction.Reload, cancellationToken);

    /// <summary>Stops the current load on the television.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnOpenPage))]
    private Task StopLoadingAsync(CancellationToken cancellationToken) =>
        navigator.SendActionAsync(BrowserCommandAction.Stop, cancellationToken);

    /// <summary>
    /// Whether a command that acts on the open page can do anything.
    /// </summary>
    /// <remarks>
    /// Reload, Stop and Close all reach the receiver's surface-open guard and are dropped when no
    /// page is open. Offering them anyway is a button that silently does nothing, which is worse
    /// than one that is plainly unavailable.
    /// </remarks>
    private bool CanActOnOpenPage => CanNavigate && HasOpenBrowserSurface;

    /// <summary>Whether a back move is available and would reach a live session.</summary>
    private bool CanGoBackNow => CanNavigate && CanGoBack;

    /// <summary>Whether a forward move is available and would reach a live session.</summary>
    private bool CanGoForwardNow => CanNavigate && CanGoForward;

    /// <summary>Turns the TV preview on or off for this secure session.</summary>
    /// <remarks>
    /// Preview is on by default when the receiver supports capture. This toggle is the way to stop
    /// (or resume) sending page pixels to Windows for the rest of the session.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanTogglePreview))]
    private Task TogglePreviewAsync(CancellationToken cancellationToken) =>
        sessionCommands.TogglePreviewAsync(cancellationToken);

    private bool CanTogglePreview => CanNavigate && Preview.CanToggle;

    /// <summary>Opens the destructive confirmation for erasing the television's browsing data.</summary>
    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void ShowClearDataConfirmation() => ClearDataConfirmationVisible = true;

    /// <summary>Closes the confirmation without erasing anything.</summary>
    [RelayCommand]
    private void CancelClearData() => ClearDataConfirmationVisible = false;

    /// <summary>Erases the television's cookies, storage, cache, history and bookmarks.</summary>
    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private async Task ConfirmClearDataAsync(CancellationToken cancellationToken)
    {
        ClearDataConfirmationVisible = false;
        await navigator.SendSessionActionAsync(BrowserCommandAction.ClearData, cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Answers the one native dialog the page is holding open.
    /// </summary>
    /// <remarks>
    /// The television shows the same dialog and can answer it alone; this is the second way in, not
    /// the only one. A dialog nobody answers blocks the page, which is why an unanswerable one is
    /// worse than an ugly one.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanAnswerDialog))]
    private Task ConfirmDialogAsync(CancellationToken cancellationToken) =>
        sessionCommands.AnswerDialogAsync(accepted: true, cancellationToken);

    /// <summary>Refuses the page's dialog. Cancel is always the safe answer.</summary>
    [RelayCommand(CanExecute = nameof(CanAnswerDialog))]
    private Task CancelDialogAsync(CancellationToken cancellationToken) =>
        sessionCommands.AnswerDialogAsync(accepted: false, cancellationToken);

    private bool CanAnswerDialog => session is { IsConnected: true } && Dialog.IsVisible;

    /// <summary>
    /// Turns page-scoped keyboard forwarding on or off.
    /// </summary>
    /// <remarks>
    /// Scoped to this page and always visible in the UI. Flint never installs a global hook, so
    /// keystrokes reach the television only while this is on and the remote surface has focus.
    /// </remarks>
    [RelayCommand]
    private void ToggleKeyboardCapture()
    {
        capture.Toggle();
        KeyboardCaptureEnabled = capture.IsEnabled;
        OnPropertyChanged(nameof(KeyboardCaptureLabel));
    }

    [RelayCommand(CanExecute = nameof(CanActOnOpenPage))]
    private Task CloseBrowserAsync(CancellationToken cancellationToken) =>
        secureSession.StopSurfaceAsync(cancellationToken);

    /// <summary>Closes the browser surface if open; safe no-op otherwise.</summary>
    /// <summary>
    /// Binds the cockpit channel that carries tabs, view settings and the library.
    /// </summary>
    /// <remarks>
    /// Null until the receiver negotiates those message families. The cockpit panels then report
    /// themselves unavailable rather than rendering empty controls that would look broken — a panel
    /// that pretends to work is worse than one that says it cannot.
    /// </remarks>
    bool IBrowserTabSessionHost.SurfaceOpen => browserSurfaceOpen;

    Task IBrowserTabSessionHost.PrepareBrowserGlassAsync(CancellationToken cancellationToken) =>
        PrepareBrowserGlassAsync(cancellationToken);

    async Task IBrowserTabSessionHost.OpenAddressAsync(string url, CancellationToken cancellationToken)
    {
        Address = url;
        await NavigateAsync(cancellationToken).ConfigureAwait(true);
    }

    internal void AttachCockpit(IBrowserCockpitRemote? remote)
    {
        cockpit.Attach(remote);
    }

    /// <summary>Publishes a decoded preview frame, on the UI thread the decoder returned to.</summary>
    private void OnPreviewDecoded(BrowserPreviewMessage message, Bitmap decoded) =>
        Preview.Accept(message, decoded);

    /// <summary>
    /// Records a typed, actionable failure.
    /// </summary>
    /// <remarks>
    /// Both fields are set on purpose: <see cref="CurrentError"/> drives the recoverable-error
    /// panel, and <see cref="LastError"/> remains the single line the verify card and older tests
    /// read. Letting them disagree is how a page ends up showing two different explanations of the
    /// same failure.
    /// </remarks>
    private void SetError(BrowserUiError error)
    {
        CurrentError = error;
        LastError = error.Message;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        observers.Detach();
        cockpit.Detach();
        previewDecoder.Dispose();
        Preview.Dispose();
        if (session is not null)
        {
            session.StateReceived -= OnStateReceived;
            _ = session.DisposeAsync().AsTask();
            session = null;
        }
    }

    /// <summary>
    /// Re-raises everything that depends on whether a live session exists.
    /// </summary>
    /// <remarks>
    /// One list, called wherever a session appears or disappears. The faults it prevents are all
    /// the same shape — a control left disabled, or a card left hidden, long after the thing it
    /// reflects has changed — and the stuck-GO failure after a receiver reinstall was one missing
    /// line from this set. Keeping them together is what makes an omission visible.
    /// </remarks>
    internal void RaiseSessionSurfaceProperties()
    {
        OnPropertyChanged(nameof(CanNavigate));
        OnPropertyChanged(nameof(CanVerify));
        OnPropertyChanged(nameof(ShowNavigation));
        OnPropertyChanged(nameof(ShowWorkspaceMosaic));
        OnPropertyChanged(nameof(ShowAimStage));
        OnPropertyChanged(nameof(CanSendTouch));
        OnPropertyChanged(nameof(CanInteractWithPreview));
        OnPropertyChanged(nameof(HasOpenBrowserSurface));
    }

    /// <summary>Re-evaluates every command whose availability follows the session.</summary>
    private void RaiseSessionCommands()
    {
        VerifySecureReceiverCommand.NotifyCanExecuteChanged();
        NavigateCommand.NotifyCanExecuteChanged();
        CloseBrowserCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
        StopLoadingCommand.NotifyCanExecuteChanged();
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Re-raises every part of the page that restates the receiver verdict.
    /// </summary>
    /// <remarks>
    /// The heading pill, the explanation, the next step and the secure-receiver card are four
    /// renderings of one judgement. Raising a subset is how a card goes stale while the heading
    /// beside it has already moved on, so callers always raise the whole set.
    /// </remarks>
    internal void RaiseVerdictChanged()
    {
        OnPropertyChanged(nameof(Capability));
        OnPropertyChanged(nameof(Availability));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(HeadingTone));
        OnPropertyChanged(nameof(Reason));
        OnPropertyChanged(nameof(Remedy));
        OnPropertyChanged(nameof(HasRemedy));
        OnPropertyChanged(nameof(IsEligible));
        OnPropertyChanged(nameof(CanVerify));
        OnPropertyChanged(nameof(HasUsablePairingCode));
        OnPropertyChanged(nameof(ShowSecureReceiverCard));
        OnPropertyChanged(nameof(ShowBrowserPortEntry));
        OnPropertyChanged(nameof(HasRememberedTrust));
        OnPropertyChanged(nameof(SecureReceiverActionLabel));
        OnPropertyChanged(nameof(SecureReceiverInstructions));
    }

    /// <summary>
    /// Reconnect can adopt a live TV page without Open/Navigate. Default-on preview still has to be
    /// asked for once the epoch is known and the surface is open.
    /// </summary>
    private void MaybeSyncPreviewAfterAdoptedSurface(BrowserStateMessage state)
    {
        if (previewPreferenceSyncedForOpenSurface
            || !browserSurfaceOpen
            || !Preview.IsSupported
            || !Preview.IsRequested
            || browserEpoch <= 0
            || state.PreviewState == BrowserPreviewState.Enabled
            || state.PreviewState == BrowserPreviewState.Unavailable)
        {
            return;
        }

        previewPreferenceSyncedForOpenSurface = true;
        Flint.Core.FlintDiag.Info(
            "FlintBrowser",
            $"adopted surface syncing default preview epoch={browserEpoch} navId={state.NavigationId}");
        _ = sessionCommands.SyncPreferenceAsync(CancellationToken.None);
    }

    private void OnPreviewReceived(BrowserPreviewMessage frame) => previewDecoder.Offer(frame);

    private void OnDialogReceived(BrowserDialogMessage dialog) =>
        uiDispatcher.Dispatch(() =>
        {
            Dialog.Show(dialog);
            ConfirmDialogCommand.NotifyCanExecuteChanged();
            CancelDialogCommand.NotifyCanExecuteChanged();
            Flint.Core.FlintDiag.Info(
                "FlintBrowser",
                $"dialog received id={dialog.DialogId} type={dialog.Type}");
        });

    private void OnStateReceived(BrowserStateMessage state) =>
        uiDispatcher.Dispatch(() => receiverState.Apply(state));

    /// <summary>Closes the browser surface on the television, leaving the session intact.</summary>
    internal Task StopBrowserSurfaceAsync(CancellationToken cancellationToken = default) =>
        secureSession.StopSurfaceAsync(cancellationToken);

    /// <summary>Re-raises whether the address bar and GO are usable.</summary>
    internal void RaiseNavigationAvailability()
    {
        OnPropertyChanged(nameof(CanNavigate));
        VerifySecureReceiverCommand.NotifyCanExecuteChanged();
        NavigateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Re-raises which stage the page shows: mosaic, aim pad, or neither.</summary>
    internal void RaiseStageLayout()
    {
        OnPropertyChanged(nameof(ShowWorkspaceMosaic));
        OnPropertyChanged(nameof(ShowAimStage));
        OnPropertyChanged(nameof(CanInteractWithPreview));
    }

    /// <summary>Re-raises whether the preview surface should accept pointer input.</summary>
    internal void RaisePreviewInteractivity() => OnPropertyChanged(nameof(CanInteractWithPreview));

    /// <summary>Arms or disarms keyboard forwarding from the receiver's focused-field report.</summary>
    internal void ApplyEditingFocus(bool editing)
    {
        if (capture.SetEditing(editing))
        {
            KeyboardCaptureEnabled = capture.IsEnabled;
            OnPropertyChanged(nameof(KeyboardCaptureLabel));
        }
    }

    /// <summary>Takes the television's glass for the browser, stopping mirror or media first.</summary>
    internal Task PrepareBrowserGlassAsync(CancellationToken cancellationToken) =>
        coordinator is null
            ? Task.CompletedTask
            : coordinator.PrepareForAsync(TvSurfaceKind.Browser, cancellationToken);

    /// <summary>Asks the television to match the host preview preference.</summary>
    internal Task SyncPreviewPreferenceAsync(CancellationToken cancellationToken) =>
        sessionCommands.SyncPreferenceAsync(cancellationToken);

    /// <summary>Records that default-on preview has been asked for on this open surface.</summary>
    internal void MarkPreviewSynced() => previewPreferenceSyncedForOpenSurface = true;

    /// <summary>Swaps the pinned session in or out. Wiring is the controller's business.</summary>
    internal void AdoptSession(ISecureBrowserRemote? next) => session = next;

    /// <summary>Where a new session comes from.</summary>
    internal IBrowserSessionConnector Connector => connector;

    /// <summary>The UI thread every receive-loop callback has to hop onto.</summary>
    internal IBrowserUiDispatcher Dispatcher => uiDispatcher;

    /// <summary>The Cast pairing code, trimmed; empty when there is none.</summary>
    internal string PairingCode => cast.PairingCode?.Trim() ?? string.Empty;

    /// <summary>The endpoint to dial, or null when routing is unknown.</summary>
    internal BrowserEndpoint? ResolveEndpoint() =>
        BrowserEndpointResolver.TryResolve(EffectiveDevice(), TryParseManualBrowserPort());

    /// <summary>Restarts per-session input numbering for a replacement transport.</summary>
    internal void ResetInputSequence() => input.Reset();

    /// <summary>Remembers the tab strip before the surface goes away.</summary>
    internal void CaptureTabSession() => tabSession.Capture();

    /// <summary>Unbinds the cockpit channel along with the transport it rides.</summary>
    internal void DetachCockpit() => cockpit.Detach();

    /// <summary>Lets default-on preview be asked for again on the next open surface.</summary>
    internal void ResetPreviewSync() => previewPreferenceSyncedForOpenSurface = false;

    /// <summary>Asks for an unattended reconnect pass.</summary>
    internal void RequestReconnect() => _ = reconnect.RequestAsync();

    /// <summary>Re-raises whether a browser surface is open on the television.</summary>
    internal void RaiseSurfaceOpenChanged()
    {
        OnPropertyChanged(nameof(HasOpenBrowserSurface));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageProgress));
        CloseBrowserCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
        StopLoadingCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Receive-loop handlers, bound and unbound with the transport.</summary>
    internal void HandleState(BrowserStateMessage state) => OnStateReceived(state);

    /// <inheritdoc cref="HandleState" />
    internal void HandlePreview(BrowserPreviewMessage frame) => OnPreviewReceived(frame);

    /// <inheritdoc cref="HandleState" />
    internal void HandleDialog(BrowserDialogMessage dialog) => OnDialogReceived(dialog);

    /// <summary>The pinned session, or null when there is none.</summary>
    internal ISecureBrowserRemote? Session => session;

    /// <summary>Records a named, recoverable failure for the page to show.</summary>
    internal void ReportError(BrowserUiError error) => SetError(error);

    /// <summary>Session epoch every command and input is stamped with.</summary>
    internal long Epoch
    {
        get => browserEpoch;
        set => browserEpoch = value;
    }

    /// <summary>The next command id to send.</summary>
    internal long NextCommandId
    {
        get => nextCommandId;
        set => nextCommandId = value;
    }

    /// <summary>The navigation the television is showing.</summary>
    internal long ActiveNavigationId
    {
        get => activeNavigationId;
        set => activeNavigationId = value;
    }

    /// <summary>Whether Windows believes a browser surface is live on the television.</summary>
    internal bool SurfaceOpen
    {
        get => browserSurfaceOpen;
        set => browserSurfaceOpen = value;
    }

    /// <summary>Re-raises the two properties that follow an active navigation.</summary>
    internal void RaiseTouchAvailability()
    {
        OnPropertyChanged(nameof(CanSendTouch));
        OnPropertyChanged(nameof(CanInteractWithPreview));
    }

    /// <summary>Asks for default-on preview once an adopted surface has an epoch.</summary>
    internal void SyncPreviewAfterAdoptedSurface(BrowserStateMessage state) =>
        MaybeSyncPreviewAfterAdoptedSurface(state);

    /// <summary>Test seam: epoch used for cockpit and navigation commands.</summary>
    internal long BrowserEpochForTests => browserEpoch;

    /// <summary>Active page navigation id used for pointer fallback when preview is not live.</summary>
    internal long ActiveNavigationIdForInteraction => activeNavigationId;

    /// <summary>Test seam: next host command id that will be sent.</summary>
    internal long NextCommandIdForTests => nextCommandId;

    /// <summary>Test seam: whether Windows believes a browser surface is live on the TV.</summary>
    internal bool BrowserSurfaceOpenForTests => browserSurfaceOpen;

    /// <summary>Fills the port field from discovery so nobody retypes what the TV advertised.</summary>
    internal void SyncBrowserPortFromEvidence()
    {
        if (BrowserReceiverSelection.AdvertisedPortText(cast.Report?.Device) is { } text
            && !string.Equals(ManualBrowserPort, text, StringComparison.Ordinal))
        {
            ManualBrowserPort = text;
        }
    }

    private FireTvDevice? EffectiveDevice() =>
        BrowserReceiverSelection.Effective(cast.Report?.Device, TryParseManualBrowserPort());

    private int? TryParseManualBrowserPort() => BrowserReceiverSelection.ParsePort(ManualBrowserPort);

    /// <summary>Rechecks a trusted receiver when Web is selected; never accepts first-use trust.</summary>
    public Task ActivateAsync() => reconnect.RequestAsync();

    partial void OnManualBrowserPortChanged(string value) => _ = reconnect.RequestAsync();

    /// <summary>Test seam so returning-user reconnect can be awaited without sleeps.</summary>
    internal Task RefreshRememberedTrustAndMaybeReconnectForTestsAsync() => reconnect.RequestAsync();

    /// <summary>Whether this page has been torn down; reconnect stops when it has.</summary>
    internal bool IsDisposed => disposed;

    /// <summary>True while the pinned TLS remote can still accept commands.</summary>
    internal bool HasLiveSession => session is { IsConnected: true };

    /// <summary>Records whether a durable pin exists, re-rendering the verdict when it changes.</summary>
    internal void ApplyRememberedTrust(bool remembered)
    {
        if (remembered == hasRememberedTrust)
        {
            return;
        }

        hasRememberedTrust = remembered;
        RaiseVerdictChanged();
    }

    /// <summary>Clears a dead session so the page falls back to an honest Idle.</summary>
    internal void MarkSessionLost(string reason) => secureSession.MarkLost(reason);

    /// <summary>Opens the pinned session; first-use accept is the caller's decision.</summary>
    internal Task ConnectAsync(bool allowFirstUseAccept, CancellationToken cancellationToken) =>
        secureSession.ConnectAsync(allowFirstUseAccept, cancellationToken);

    /// <summary>
    /// Says whether an unattended reconnect is running.
    /// </summary>
    /// <remarks>
    /// Without this the page sits on its pre-connect explanation while Windows is already
    /// dialling, which reads as nothing happening.
    /// </remarks>
    internal void SetReconnecting(bool value)
    {
        if (reconnectInFlight == value)
        {
            return;
        }

        reconnectInFlight = value;
        OnPropertyChanged(nameof(IsReconnecting));
        OnPropertyChanged(nameof(Reason));
    }
}
