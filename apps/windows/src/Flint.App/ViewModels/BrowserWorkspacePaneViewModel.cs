using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Flint.App.ViewModels;

/// <summary>
/// One independently browsing page in the desktop projection of the TV workspace.
/// </summary>
/// <remarks>
/// This is deliberately a page/pane model, not a video-tile model. A video is only one possible
/// thing a page may contain, and media controls remain explicit requests because an arbitrary site
/// can decline them.
/// </remarks>
public sealed partial class BrowserWorkspacePaneViewModel : ObservableObject
{
    private readonly Func<BrowserWorkspacePaneViewModel, Task> requestFocus;
    private readonly Func<BrowserWorkspacePaneViewModel, BrowserWorkspaceMediaActions, Task> requestMedia;
    private readonly Func<BrowserWorkspacePaneViewModel, Task> requestClose;
    private readonly Func<BrowserWorkspacePaneViewModel, BrowserWorkspacePageAction, string, Task> requestPage;
    private readonly Func<BrowserWorkspacePaneViewModel, int, Task> requestMove;
    private BrowserWorkspaceMediaSnapshot media = BrowserWorkspaceMediaSnapshot.None;
    private BrowserWorkspaceMediaActions pendingMediaAction;

    internal BrowserWorkspacePaneViewModel(
        string paneId,
        Func<BrowserWorkspacePaneViewModel, Task> requestFocus,
        Func<BrowserWorkspacePaneViewModel, BrowserWorkspaceMediaActions, Task> requestMedia,
        Func<BrowserWorkspacePaneViewModel, Task> requestClose,
        Func<BrowserWorkspacePaneViewModel, BrowserWorkspacePageAction, string, Task> requestPage,
        Func<BrowserWorkspacePaneViewModel, int, Task> requestMove)
    {
        PaneId = paneId;
        this.requestFocus = requestFocus;
        this.requestMedia = requestMedia;
        this.requestClose = requestClose;
        this.requestPage = requestPage;
        this.requestMove = requestMove;
    }

    /// <summary>Receiver-stable identity, used for every focused request and never inferred from a slot.</summary>
    public string PaneId { get; }

    /// <summary>Receiver-selected visual position, zero based.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private int slot;

    /// <summary>Page title as reported by the receiver.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private string title = string.Empty;

    /// <summary>Current page URL as reported by the receiver.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HostOrUrl))]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private string url = string.Empty;

    /// <summary>Address draft belongs to this pane and survives progress snapshots.</summary>
    [ObservableProperty]
    private string draftAddress = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NavigateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    private bool canNavigate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private bool canGoBack;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ForwardCommand))]
    private bool canGoForward;

    /// <summary>Receiver-reported navigation progress.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    [NotifyPropertyChangedFor(nameof(ShowLoadProgress))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private int progress;

    /// <summary>Receiver-reported page lifecycle state.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    [NotifyPropertyChangedFor(nameof(ShowLoadProgress))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private BrowserWorkspacePaneState state;

    /// <summary>Whether the page's own fullscreen content currently fills this pane only.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private bool isPageFullscreen;

    /// <summary>True only after the receiver's snapshot names this pane as focused.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FocusLabel))]
    [NotifyPropertyChangedFor(nameof(FocusBadge))]
    [NotifyPropertyChangedFor(nameof(ShowFocusBadge))]
    [NotifyPropertyChangedFor(nameof(CanSendInput))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    [NotifyCanExecuteChangedFor(nameof(RequestFocusCommand))]
    private bool isFocused;

    /// <summary>True after a focus request, until the receiver confirms this exact pane.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FocusLabel))]
    [NotifyPropertyChangedFor(nameof(FocusBadge))]
    [NotifyPropertyChangedFor(nameof(ShowFocusBadge))]
    [NotifyPropertyChangedFor(nameof(CanSendInput))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    [NotifyCanExecuteChangedFor(nameof(RequestFocusCommand))]
    private bool isFocusPending;

    /// <summary>Whether this pane can ask the receiver to make it focused.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestFocusCommand))]
    private bool canRequestFocus;

    /// <summary>Whether only confirmed-focus input may be sent to this pane.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FocusLabel))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private bool canSendInput;

    /// <summary>Whether the receiver allows this pane to be closed at the current capacity.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool canClose;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveLeftCommand))]
    private bool canMoveLeft;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveRightCommand))]
    private bool canMoveRight;

    /// <summary>Whether the receiver and currently bound channel permit media requests for this pane.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestPlaybackCommand))]
    [NotifyCanExecuteChangedFor(nameof(RequestMuteCommand))]
    private bool canRequestMedia;

    /// <summary>Whether a media request is in flight and still lacks receiver confirmation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaybackRequestLabel))]
    [NotifyPropertyChangedFor(nameof(MuteRequestLabel))]
    [NotifyPropertyChangedFor(nameof(MediaRequestStatusLabel))]
    [NotifyPropertyChangedFor(nameof(ShowMediaActions))]
    [NotifyPropertyChangedFor(nameof(ShowMediaStatus))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    [NotifyCanExecuteChangedFor(nameof(RequestPlaybackCommand))]
    [NotifyCanExecuteChangedFor(nameof(RequestMuteCommand))]
    private bool isMediaRequestPending;

    /// <summary>A factual transport or receiver-confirmation message about the latest media request.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MediaRequestStatusLabel))]
    [NotifyPropertyChangedFor(nameof(ShowMediaStatus))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private string? mediaRequestError;

    /// <summary>Title with a stable fallback for a new or title-less page.</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? HostOrFallback(Url) : Title;

    /// <summary>Host where possible, without claiming an absent URL is a loaded page.</summary>
    public string HostOrUrl => HostOrFallback(Url);

    /// <summary>Lifecycle evidence from the receiver.</summary>
    public string StateLabel => State switch
    {
        BrowserWorkspacePaneState.Loading => $"LOADING {Progress}%",
        BrowserWorkspacePaneState.Live when IsPageFullscreen => "PAGE FULLSCREEN IN THIS PANE",
        BrowserWorkspacePaneState.Live => "PAGE LIVE",
        BrowserWorkspacePaneState.Suspended => "PAGE SUSPENDED",
        BrowserWorkspacePaneState.Failed => "PAGE FAILED",
        _ => "PAGE STATE NOT REPORTED",
    };

    /// <summary>Focus wording that makes the input safety barrier visible rather than surprising.</summary>
    public string FocusLabel => IsFocusPending
        ? "WAITING FOR TV TO CONFIRM FOCUS"
        : IsFocused && CanSendInput
            ? "FOCUS CONFIRMED BY TV"
            : IsFocused
                ? "FOCUSED, INPUT NOT AVAILABLE"
                : "NOT FOCUSED";

    /// <summary>Short visual badge for the mosaic tile — long copy stays in <see cref="FocusLabel"/>.</summary>
    public string FocusBadge => IsFocusPending
        ? "WAITING"
        : IsFocused
            ? "ACTIVE"
            : string.Empty;

    /// <summary>Whether the mosaic tile should show the compact focus badge.</summary>
    public bool ShowFocusBadge => !string.IsNullOrEmpty(FocusBadge);

    /// <summary>Whether a load bar should occupy mosaic chrome.</summary>
    public bool ShowLoadProgress => State == BrowserWorkspacePaneState.Loading && Progress is > 0 and < 100;

    /// <summary>Whether media request buttons belong on this tile.</summary>
    public bool ShowMediaActions =>
        CanRequestPlayback() || CanRequestMute() || IsMediaRequestPending;

    /// <summary>Whether media status text adds signal beyond the default idle label.</summary>
    public bool ShowMediaStatus =>
        IsMediaRequestPending
        || !string.IsNullOrWhiteSpace(MediaRequestError)
        || Media.Playback is BrowserWorkspacePlaybackState.Playing or BrowserWorkspacePlaybackState.Paused;

    /// <summary>Receiver-reported media state, not an optimistic result of a request button.</summary>
    public string ReportedMediaLabel => Media.Playback switch
    {
        BrowserWorkspacePlaybackState.Playing => Media.Mute == BrowserWorkspaceMuteState.Muted
            ? "PLAYING · MUTED"
            : "PLAYING",
        BrowserWorkspacePlaybackState.Paused => Media.Mute == BrowserWorkspaceMuteState.Muted
            ? "PAUSED · MUTED"
            : "PAUSED",
        _ when Media.Mute == BrowserWorkspaceMuteState.Muted => "PLAYBACK NOT REPORTED · MUTED",
        _ => "MEDIA STATE NOT REPORTED",
    };

    /// <summary>Action label explicitly framed as a request, since pages can reject playback commands.</summary>
    public string PlaybackRequestLabel => IsMediaRequestPending && IsPlaybackPending
        ? "PLAYBACK REQUEST SENT"
        : PlaybackAction switch
        {
            BrowserWorkspaceMediaActions.Play => "REQUEST PLAY",
            BrowserWorkspaceMediaActions.Pause => "REQUEST PAUSE",
            BrowserWorkspaceMediaActions.TogglePlayback => "REQUEST PLAY/PAUSE",
            _ => "PLAYBACK CONTROL UNAVAILABLE",
        };

    /// <summary>Action label explicitly framed as a per-pane request, never a global device mute.</summary>
    public string MuteRequestLabel => IsMediaRequestPending && IsMutePending
        ? "AUDIO REQUEST SENT"
        : MuteAction switch
        {
            BrowserWorkspaceMediaActions.Mute => "REQUEST MUTE",
            BrowserWorkspaceMediaActions.Unmute => "REQUEST UNMUTE",
            BrowserWorkspaceMediaActions.ToggleMute => "REQUEST MUTE/UNMUTE",
            _ => "AUDIO CONTROL UNAVAILABLE",
        };

    /// <summary>Short feedback that never re-labels a site as paused or muted without receiver evidence.</summary>
    public string MediaRequestStatusLabel => IsMediaRequestPending
        ? "REQUEST SENT · WAITING FOR TV CONFIRMATION"
        : MediaRequestError ?? ReportedMediaLabel;

    /// <summary>Accessible page summary; visible labels remain concise at normal desktop scale.</summary>
    public string AccessibleName =>
        $"Pane {Slot + 1}: {DisplayTitle}. {StateLabel}. {FocusLabel}. {MediaRequestStatusLabel}.";

    private BrowserWorkspaceMediaSnapshot Media => media;

    /// <summary>Last receiver media snapshot, retained when focus/capability state is recomputed locally.</summary>
    internal BrowserWorkspaceMediaSnapshot MediaSnapshot => media;

    private BrowserWorkspaceMediaActions PlaybackAction => ResolvePlaybackAction(media);

    private BrowserWorkspaceMediaActions MuteAction => ResolveMuteAction(media);

    private bool IsPlaybackPending => pendingMediaAction is BrowserWorkspaceMediaActions.Play
        or BrowserWorkspaceMediaActions.Pause
        or BrowserWorkspaceMediaActions.TogglePlayback;

    private bool IsMutePending => pendingMediaAction is BrowserWorkspaceMediaActions.Mute
        or BrowserWorkspaceMediaActions.Unmute
        or BrowserWorkspaceMediaActions.ToggleMute;

    internal void Apply(
        BrowserWorkspacePaneSnapshot snapshot,
        bool focused,
        bool focusPending,
        BrowserWorkspaceCapabilities capabilities,
        bool inputPermitted,
        bool closePermitted,
        int paneCount = 1,
        bool receiverSnapshot = true)
    {
        var normalized = snapshot.Normalize();
        Slot = normalized.Slot;
        Title = normalized.Title;
        if (DraftAddress == Url || string.IsNullOrWhiteSpace(DraftAddress)) DraftAddress = normalized.Url;
        Url = normalized.Url;
        Progress = normalized.Progress;
        State = normalized.State;
        IsPageFullscreen = normalized.IsPageFullscreen;
        media = normalized.Media;
        IsFocused = focused;
        IsFocusPending = focusPending;
        CanRequestFocus = capabilities.IsAvailable && capabilities.CanRequestPaneFocus && !focused && !focusPending;
        CanSendInput = inputPermitted;
        CanClose = closePermitted;
        CanMoveLeft = capabilities.IsAvailable && paneCount > 1 && normalized.Slot > 0;
        CanMoveRight = capabilities.IsAvailable && paneCount > 1 && normalized.Slot < paneCount - 1;
        CanNavigate = capabilities.IsAvailable;
        CanGoBack = CanNavigate && normalized.CanGoBack;
        CanGoForward = CanNavigate && normalized.CanGoForward;
        CanRequestMedia = capabilities.IsAvailable && capabilities.CanRequestMediaControl;
        if (receiverSnapshot) ReconcileMediaRequest();
        NotifyMediaPresentation();
    }

    /// <summary>Marks a command as sent while waiting for receiver evidence; no media state is changed here.</summary>
    internal void MarkMediaRequestPending(BrowserWorkspaceMediaActions action)
    {
        pendingMediaAction = action;
        MediaRequestError = null;
        IsMediaRequestPending = true;
        NotifyMediaPresentation();
    }

    /// <summary>Reports a transport failure without fabricating a page-level result.</summary>
    internal void MarkMediaRequestFailed(string message)
    {
        pendingMediaAction = BrowserWorkspaceMediaActions.None;
        IsMediaRequestPending = false;
        MediaRequestError = message;
        NotifyMediaPresentation();
    }

    [RelayCommand(CanExecute = nameof(CanRequestFocus))]
    private Task RequestFocusAsync() => requestFocus(this);

    [RelayCommand(CanExecute = nameof(CanRequestPlayback))]
    private Task RequestPlaybackAsync() => requestMedia(this, PlaybackAction);

    [RelayCommand(CanExecute = nameof(CanRequestMute))]
    private Task RequestMuteAsync() => requestMedia(this, MuteAction);

    [RelayCommand(CanExecute = nameof(CanClose))]
    private Task CloseAsync() => requestClose(this);

    [RelayCommand(CanExecute = nameof(CanMoveLeft))]
    private Task MoveLeftAsync() => requestMove(this, Slot - 1);

    [RelayCommand(CanExecute = nameof(CanMoveRight))]
    private Task MoveRightAsync() => requestMove(this, Slot + 1);

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private Task NavigateAsync() => requestPage(this, BrowserWorkspacePageAction.Navigate, DraftAddress);

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private Task ReloadAsync() => requestPage(this, BrowserWorkspacePageAction.Reload, string.Empty);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private Task BackAsync() => requestPage(this, BrowserWorkspacePageAction.Back, string.Empty);

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private Task ForwardAsync() => requestPage(this, BrowserWorkspacePageAction.Forward, string.Empty);

    private bool CanRequestPlayback() =>
        CanRequestMedia && !IsMediaRequestPending && PlaybackAction != BrowserWorkspaceMediaActions.None;

    private bool CanRequestMute() =>
        CanRequestMedia && !IsMediaRequestPending && MuteAction != BrowserWorkspaceMediaActions.None;

    private void ReconcileMediaRequest()
    {
        if (pendingMediaAction == BrowserWorkspaceMediaActions.None)
        {
            return;
        }

        if (IsActionConfirmed(pendingMediaAction))
        {
            pendingMediaAction = BrowserWorkspaceMediaActions.None;
            IsMediaRequestPending = false;
            MediaRequestError = null;
            return;
        }

        // A concrete opposite receiver state is useful evidence that the page did not honour the
        // request. Unknown stays pending because the receiver has not told us enough to judge it.
        if (IsActionContradicted(pendingMediaAction))
        {
            pendingMediaAction = BrowserWorkspaceMediaActions.None;
            IsMediaRequestPending = false;
            MediaRequestError = "THE TV HAS NOT CONFIRMED THAT MEDIA REQUEST";
        }
    }

    private bool IsActionConfirmed(BrowserWorkspaceMediaActions action) => action switch
    {
        BrowserWorkspaceMediaActions.Play => Media.Playback == BrowserWorkspacePlaybackState.Playing,
        BrowserWorkspaceMediaActions.Pause => Media.Playback == BrowserWorkspacePlaybackState.Paused,
        BrowserWorkspaceMediaActions.Mute => Media.Mute == BrowserWorkspaceMuteState.Muted,
        BrowserWorkspaceMediaActions.Unmute => Media.Mute == BrowserWorkspaceMuteState.Audible,
        BrowserWorkspaceMediaActions.TogglePlayback => Media.Playback != BrowserWorkspacePlaybackState.Unknown,
        BrowserWorkspaceMediaActions.ToggleMute => Media.Mute != BrowserWorkspaceMuteState.Unknown,
        _ => false,
    };

    private bool IsActionContradicted(BrowserWorkspaceMediaActions action) => action switch
    {
        BrowserWorkspaceMediaActions.Play => Media.Playback == BrowserWorkspacePlaybackState.Paused,
        BrowserWorkspaceMediaActions.Pause => Media.Playback == BrowserWorkspacePlaybackState.Playing,
        BrowserWorkspaceMediaActions.Mute => Media.Mute == BrowserWorkspaceMuteState.Audible,
        BrowserWorkspaceMediaActions.Unmute => Media.Mute == BrowserWorkspaceMuteState.Muted,
        _ => false,
    };

    private static BrowserWorkspaceMediaActions ResolvePlaybackAction(BrowserWorkspaceMediaSnapshot source)
    {
        var allowed = source.AllowedActions &
            (BrowserWorkspaceMediaActions.Play |
             BrowserWorkspaceMediaActions.Pause |
             BrowserWorkspaceMediaActions.TogglePlayback);
        return source.Playback switch
        {
            BrowserWorkspacePlaybackState.Playing when allowed.HasFlag(BrowserWorkspaceMediaActions.Pause) =>
                BrowserWorkspaceMediaActions.Pause,
            BrowserWorkspacePlaybackState.Paused when allowed.HasFlag(BrowserWorkspaceMediaActions.Play) =>
                BrowserWorkspaceMediaActions.Play,
            _ when allowed.HasFlag(BrowserWorkspaceMediaActions.TogglePlayback) =>
                BrowserWorkspaceMediaActions.TogglePlayback,
            _ => BrowserWorkspaceMediaActions.None,
        };
    }

    private static BrowserWorkspaceMediaActions ResolveMuteAction(BrowserWorkspaceMediaSnapshot source)
    {
        var allowed = source.AllowedActions &
            (BrowserWorkspaceMediaActions.Mute |
             BrowserWorkspaceMediaActions.Unmute |
             BrowserWorkspaceMediaActions.ToggleMute);
        return source.Mute switch
        {
            BrowserWorkspaceMuteState.Audible when allowed.HasFlag(BrowserWorkspaceMediaActions.Mute) =>
                BrowserWorkspaceMediaActions.Mute,
            BrowserWorkspaceMuteState.Muted when allowed.HasFlag(BrowserWorkspaceMediaActions.Unmute) =>
                BrowserWorkspaceMediaActions.Unmute,
            BrowserWorkspaceMuteState.Unknown when allowed.HasFlag(BrowserWorkspaceMediaActions.ToggleMute) =>
                BrowserWorkspaceMediaActions.ToggleMute,
            _ => BrowserWorkspaceMediaActions.None,
        };
    }

    private void NotifyMediaPresentation()
    {
        OnPropertyChanged(nameof(ReportedMediaLabel));
        OnPropertyChanged(nameof(PlaybackRequestLabel));
        OnPropertyChanged(nameof(MuteRequestLabel));
        OnPropertyChanged(nameof(MediaRequestStatusLabel));
        OnPropertyChanged(nameof(AccessibleName));
        RequestPlaybackCommand.NotifyCanExecuteChanged();
        RequestMuteCommand.NotifyCanExecuteChanged();
    }

    private static string HostOrFallback(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) && !string.IsNullOrWhiteSpace(parsed.Host)
            ? parsed.Host
            : string.IsNullOrWhiteSpace(url) ? "New page" : url;
}
