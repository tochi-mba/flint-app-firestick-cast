using CommunityToolkit.Mvvm.ComponentModel;
using Flint.App.Controls;
using Flint.Core;
using Flint.Core.Media;
using Flint.Core.Settings;

namespace Flint.App.ViewModels;

/// <summary>
/// The Now Playing card on the Media page: what the TV is playing from this PC, and its controls.
/// </summary>
/// <remarks>
/// <para>
/// The TV reports twice a second. Between reports the bar moves on a local quarter-second tick,
/// corrected by each report, so it never jumps and never runs past the end.
/// </para>
/// <para>
/// Controls answer at once and the TV confirms afterwards. A seek holds the bar at its target until
/// a report lands within two seconds of it, so the bar never snaps back to the old position and then
/// forward again. Play and pause flip the button straight away and give way to what the TV says if
/// it has not agreed within a second and a half. Quick presses of a skip button add up to one seek.
/// </para>
/// </remarks>
public sealed partial class NowPlayingViewModel : ObservableObject, IDisposable
{
    /// <summary>How often the bar moves between reports.</summary>
    internal static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>How long a seek target is held while waiting for the TV to confirm it.</summary>
    internal static readonly TimeSpan SeekHold = TimeSpan.FromSeconds(2);

    /// <summary>How close a report must be to a seek target to confirm it, in milliseconds.</summary>
    internal const long SeekTolerance = 2_000;

    /// <summary>How long play or pause waits for the TV to agree before showing what it says.</summary>
    internal static readonly TimeSpan ToggleHold = TimeSpan.FromMilliseconds(1_500);

    /// <summary>How long after the last skip press the combined seek is sent.</summary>
    internal static readonly TimeSpan SkipGather = TimeSpan.FromMilliseconds(400);

    /// <summary>What the skip buttons say when they cannot be used.</summary>
    internal const string NoLengthTip = "This file does not report its length.";

    private readonly IMediaRemote remote;
    private readonly TimeProvider time;
    private readonly SynchronizationContext? context;
    private readonly SettingsService ownSettings;
    private ISettingsService? settings;
    private PlaybackSnapshot? snapshot;
    private bool isSending;
    private double sendFraction;
    private long? heldPosition;
    private DateTimeOffset heldUntil;
    private PlaybackPhase? expectedPhase;
    private DateTimeOffset expectedUntil;
    private long? gatheredSkip;
    private ITimer? skipTimer;
    private ITimer? ticker;
    private long scrubPosition;
    private long frozenPosition;
    private string? problem;
    private int? volumePercent;
    private int? volumeBeforeMute;

    /// <summary>Creates the card, empty.</summary>
    /// <param name="remote">Where controls go.</param>
    /// <param name="time">The clock and timers; the system's unless a test supplies its own.</param>
    public NowPlayingViewModel(IMediaRemote remote, TimeProvider? time = null)
    {
        this.remote = remote ?? throw new ArgumentNullException(nameof(remote));
        this.time = time ?? TimeProvider.System;
        context = SynchronizationContext.Current;

        // Until the shell hands over the live settings, the card keeps its own, as Flint ships them.
        ownSettings = new SettingsService(new InMemoryAppSettingsStore());
        UseSettings(ownSettings);
    }

    /// <summary>Whether the card is showing: something from this PC is on, or on its way to, the TV.</summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>The file's name.</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>Whether the file is a picture, which has no position to seek or play to.</summary>
    [ObservableProperty]
    private bool _isPicture;

    /// <summary>Whether the seek bar is being dragged.</summary>
    [ObservableProperty]
    private bool _isScrubbing;

    /// <summary>Whether the connection to the TV ended while this was on it.</summary>
    [ObservableProperty]
    private bool _isConnectionLost;

    /// <summary>What the card says about the lost connection.</summary>
    [ObservableProperty]
    private string _connectionLostText = string.Empty;

    /// <summary>What the TV's player is shown to be doing.</summary>
    public PlaybackPhase Phase => expectedPhase ?? snapshot?.Phase ?? PlaybackPhase.Buffering;

    /// <summary>Whether the file is still being sent.</summary>
    public bool IsSending => isSending;

    /// <summary>How much of the file has been sent, in percent.</summary>
    public int SendPercent => (int)Math.Clamp(sendFraction * 100, 0, 100);

    /// <summary>The state pill.</summary>
    public string PillText => IsConnectionLost ? "DISCONNECTED"
        : isSending
        ? $"SENDING {SendPercent}%"
        : ShowsPictureNote
        ? "SHOWING"
        : Phase switch
        {
            PlaybackPhase.Playing => "PLAYING",
            PlaybackPhase.Paused => "PAUSED",
            PlaybackPhase.Finished => "FINISHED",
            PlaybackPhase.Problem => "PROBLEM",
            _ => "BUFFERING",
        };

    /// <summary>The state pill's colour.</summary>
    public Tone PillTone => IsConnectionLost || isSending ? Tone.Neutral
        : Phase switch
        {
            PlaybackPhase.Playing => Tone.Signal,
            PlaybackPhase.Problem => Tone.Live,
            _ => Tone.Neutral,
        };

    /// <summary>Whether the TV said how long the file is.</summary>
    public bool HasDuration => snapshot?.HasDuration == true;

    /// <summary>Whether the seek bar shows: a file of known length, past sending, that is not a picture.</summary>
    public bool ShowsSeekBar => !isSending && !IsPicture && HasDuration && Phase is not PlaybackPhase.Problem;

    /// <summary>Whether only the elapsed time shows, for a file that does not say how long it is.</summary>
    public bool ShowsElapsedOnly => !isSending && !IsPicture && !HasDuration && Phase is not PlaybackPhase.Problem;

    /// <summary>Whether the transport buttons show.</summary>
    public bool ShowsTransport => !isSending && !IsPicture && Phase is not PlaybackPhase.Problem;

    /// <summary>Whether the card is showing a picture rather than playing something.</summary>
    public bool ShowsPictureNote => !isSending && IsPicture && Phase is not PlaybackPhase.Problem;

    /// <summary>Whether the TV could not play the file.</summary>
    public bool ShowsProblem => !isSending && Phase is PlaybackPhase.Problem;

    /// <summary>The TV's reason it could not play the file, in words that say what to do.</summary>
    public string ProblemText => problem ?? string.Empty;

    /// <summary>The position shown, in milliseconds.</summary>
    public long PositionMs =>
        IsConnectionLost ? frozenPosition
        : IsScrubbing ? scrubPosition
        : gatheredSkip ?? heldPosition ?? snapshot?.PositionAt(time.GetUtcNow()) ?? 0;

    /// <summary>The file's length, in milliseconds; zero when unknown.</summary>
    public long DurationMs => HasDuration ? snapshot!.DurationMs : 0;

    /// <summary>The seek bar's position in seconds. Written by the bar only while it is being dragged.</summary>
    public double SeekSeconds
    {
        get => PositionMs / 1000.0;
        set
        {
            if (IsScrubbing)
            {
                scrubPosition = Clamp((long)(value * 1000));
                RaisePosition();
            }
        }
    }

    /// <summary>The seek bar's end, in seconds.</summary>
    public double DurationSeconds => DurationMs / 1000.0;

    /// <summary>The time on the left of the bar.</summary>
    public string ElapsedText => PlaybackTimeText.Format(PositionMs);

    /// <summary>The time on the right of the bar: what is left, or the whole length.</summary>
    public string RemainingText => ShowsTotalTime
        ? PlaybackTimeText.Format(DurationMs)
        : "-" + PlaybackTimeText.Format(Math.Max(0, DurationMs - PositionMs));

    /// <summary>Whether the right-hand time is the whole length.</summary>
    public bool ShowsTotalTime => Media.ShowTotalTime;

    /// <summary>What the time bubble above a dragged thumb says.</summary>
    public string ScrubText => PlaybackTimeText.Format(scrubPosition);

    /// <summary>The seek bar's value as a screen reader says it.</summary>
    public string SpokenPosition => $"{PlaybackTimeText.Spoken(PositionMs)} of {PlaybackTimeText.Spoken(DurationMs)}";

    /// <summary>The play button's words.</summary>
    public string PlayPauseLabel => Phase switch
    {
        PlaybackPhase.Playing or PlaybackPhase.Buffering => "PAUSE",
        PlaybackPhase.Finished => "REPLAY",
        _ => "PLAY",
    };

    /// <summary>The play button's name for a screen reader.</summary>
    public string PlayPauseName => Phase switch
    {
        PlaybackPhase.Playing or PlaybackPhase.Buffering => "Pause",
        PlaybackPhase.Finished => "Play again from the start",
        _ => "Play",
    };

    /// <summary>Whether anything on the card can be used.</summary>
    public bool CanControl => IsActive && !IsConnectionLost;

    private MediaSettings Media => settings!.Current.Media;

    /// <summary>Reads skip distances, the volume step and the time display from the live settings.</summary>
    /// <remarks>Lets go of the settings it read before, so a replaced service no longer reaches the card.</remarks>
    public void UseSettings(ISettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        if (settings is not null)
        {
            settings.Changed -= OnSettingsChanged;
        }

        settings = settingsService;
        settings.Changed += OnSettingsChanged;
        RaiseAll();
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs change)
    {
        if (change.Previous.Media != change.Current.Media)
        {
            RaiseAll();
        }
    }

    /// <summary>A file has started on its way to the TV.</summary>
    public void BeginSending(string title, bool isPicture)
    {
        Reset();
        Title = title;
        IsPicture = isPicture;
        isSending = true;
        IsActive = true;
        RaiseAll();
    }

    /// <summary>How much of the file has reached the TV.</summary>
    public void ReportSendProgress(double fraction)
    {
        if (!isSending)
        {
            return;
        }

        sendFraction = fraction;
        OnPropertyChanged(nameof(SendPercent));
        OnPropertyChanged(nameof(PillText));
    }

    /// <summary>The file arrived and the TV answered with its first report.</summary>
    public void SendFinished(PlaybackSnapshot first)
    {
        ArgumentNullException.ThrowIfNull(first);
        if (!isSending)
        {
            return;
        }

        isSending = false;
        Apply(first);
        StartTicking();
    }

    /// <summary>The TV could not play the file, for the reason given.</summary>
    public void ShowProblem(string reason)
    {
        if (!IsActive || IsConnectionLost)
        {
            return;
        }

        isSending = false;
        problem = reason;
        expectedPhase = null;
        snapshot = new PlaybackSnapshot(PlaybackPhase.Problem, 0, -1, reason, time.GetUtcNow());
        RaiseAll();
    }

    /// <summary>A report from the TV's player.</summary>
    /// <remarks>
    /// Ignored while a file is being sent: whatever the TV says then is about the file before it.
    /// </remarks>
    public void Apply(PlaybackSnapshot report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!IsActive || isSending || IsConnectionLost)
        {
            return;
        }

        if (report.Phase is PlaybackPhase.Idle)
        {
            Clear();
            return;
        }

        snapshot = report;
        if (heldPosition is { } target && Math.Abs(report.PositionMs - target) <= SeekTolerance)
        {
            heldPosition = null;
        }

        if (expectedPhase == report.Phase || report.Phase is PlaybackPhase.Finished or PlaybackPhase.Problem)
        {
            expectedPhase = null;
        }

        if (report.Phase is PlaybackPhase.Problem)
        {
            problem = report.Detail;
        }

        RaiseAll();
    }

    /// <summary>The connection ended: the card stays, greyed, showing what was playing and where.</summary>
    public void ConnectionLost(string told)
    {
        if (!IsActive)
        {
            return;
        }

        frozenPosition = PositionMs;
        StopTimers();
        isSending = false;
        heldPosition = null;
        gatheredSkip = null;
        expectedPhase = null;
        IsScrubbing = false;
        ConnectionLostText = told;
        IsConnectionLost = true;
        RaiseAll();
    }

    /// <summary>Nothing from this PC is on the TV any more.</summary>
    public void Clear()
    {
        Reset();
        RaiseAll();
    }

    /// <summary>Moves the bar on between reports, and lets go of holds the TV never confirmed.</summary>
    internal void Tick()
    {
        var now = time.GetUtcNow();
        if (heldPosition is not null && now >= heldUntil)
        {
            heldPosition = null;
        }

        if (expectedPhase is not null && now >= expectedUntil)
        {
            // The TV did not agree in time: show what it says instead.
            expectedPhase = null;
            RaiseAll();
        }
        else
        {
            RaisePosition();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        StopTimers();
        ownSettings.Dispose();
    }

    private void StartTicking()
    {
        ticker = time.CreateTimer(_ => OnUi(Tick), null, TickInterval, TickInterval);
    }

    private void StopTimers()
    {
        ticker?.Dispose();
        ticker = null;
        skipTimer?.Dispose();
        skipTimer = null;
    }

    private void Reset()
    {
        StopTimers();
        snapshot = null;
        isSending = false;
        sendFraction = 0;
        heldPosition = null;
        expectedPhase = null;
        gatheredSkip = null;
        problem = null;
        frozenPosition = 0;
        IsScrubbing = false;
        IsConnectionLost = false;
        ConnectionLostText = string.Empty;
        IsActive = false;
        IsPicture = false;
        Title = string.Empty;
    }

    /// <summary>Runs <paramref name="action"/> where the card's bindings read it.</summary>
    private void OnUi(Action action)
    {
        if (context is null)
        {
            action();
        }
        else
        {
            context.Post(_ => action(), null);
        }
    }

    /// <summary>Sends without waiting; a send the TV never hears of is answered by the session ending.</summary>
    private static void Send(Func<Task> send) => _ = SendObservedAsync(send);

    private static async Task SendObservedAsync(Func<Task> send)
    {
        try
        {
            await send().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            FlintDiag.Warn("FlintCast", $"media control not sent: {exception.GetType().Name}");
        }
    }

    private void RaisePosition()
    {
        OnPropertyChanged(nameof(PositionMs));
        OnPropertyChanged(nameof(SeekSeconds));
        OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(ScrubText));
        OnPropertyChanged(nameof(SpokenPosition));
    }

    private void RaiseAll()
    {
        OnPropertyChanged(string.Empty);
        PlayPauseCommand.NotifyCanExecuteChanged();
        SkipBackCommand.NotifyCanExecuteChanged();
        SkipForwardCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        TryAgainCommand.NotifyCanExecuteChanged();
        RaiseVolume();
    }
}
