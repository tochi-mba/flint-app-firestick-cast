using System.Globalization;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.App.Controls;
using Flint.Core;
using Flint.Core.Media;
using Flint.Core.Settings;
using Flint.Protocol;

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
    private ISettingsService settings = null!;
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
    public string ElapsedText => FormatTime(PositionMs);

    /// <summary>The time on the right of the bar: what is left, or the whole length.</summary>
    public string RemainingText => ShowsTotalTime
        ? FormatTime(DurationMs)
        : "-" + FormatTime(Math.Max(0, DurationMs - PositionMs));

    /// <summary>Whether the right-hand time is the whole length.</summary>
    public bool ShowsTotalTime => Media.ShowTotalTime;

    /// <summary>What the time bubble above a dragged thumb says.</summary>
    public string ScrubText => FormatTime(scrubPosition);

    /// <summary>The seek bar's value as a screen reader says it.</summary>
    public string SpokenPosition => $"{Spoken(PositionMs)} of {Spoken(DurationMs)}";

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

    /// <summary>How far the bar's Left key goes back, in seconds.</summary>
    public double SkipBackSeconds => Media.SkipBackSeconds;

    /// <summary>How far the bar's Right key goes ahead, in seconds.</summary>
    public double SkipForwardSeconds => Media.SkipForwardSeconds;

    /// <summary>The skip-back button's words.</summary>
    public string SkipBackLabel => $"−{Media.SkipBackSeconds} S";

    /// <summary>The skip-forward button's words.</summary>
    public string SkipForwardLabel => $"+{Media.SkipForwardSeconds} S";

    /// <summary>The skip-back button's name for a screen reader.</summary>
    public string SkipBackName => $"Back {Media.SkipBackSeconds} seconds";

    /// <summary>The skip-forward button's name for a screen reader.</summary>
    public string SkipForwardName => $"Forward {Media.SkipForwardSeconds} seconds";

    /// <summary>Why the skip buttons are off, when they are.</summary>
    public string? SkipTip => HasDuration ? null : NoLengthTip;

    /// <summary>Whether a volume has been set from this PC, so the slider has a position to show.</summary>
    public bool HasVolume => volumePercent is not null;

    /// <summary>Whether the TV was muted from here.</summary>
    public bool IsMuted => volumeBeforeMute is not null;

    /// <summary>The volume slider's value, in percent.</summary>
    public double VolumePercent
    {
        get => volumePercent ?? 0;
        set => SetVolume((int)Math.Round(value));
    }

    /// <summary>The line under the volume slider.</summary>
    public string VolumeText => volumePercent is { } level
        ? $"TV volume: {level}%"
        : "TV volume: not set from here yet";

    /// <summary>The mute button's words.</summary>
    public string MuteLabel => IsMuted ? "UNMUTE" : "MUTE";

    /// <summary>The mute button's name for a screen reader.</summary>
    public string MuteName => IsMuted ? "Unmute the TV" : "Mute the TV";

    /// <summary>Whether anything on the card can be used.</summary>
    public bool CanControl => IsActive && !IsConnectionLost;

    private bool CanPlayPause => CanControl && !isSending && !IsPicture && snapshot is not null
        && Phase is not PlaybackPhase.Problem;

    private bool CanSkip => CanPlayPause && HasDuration;

    private bool CanStop => CanControl && !isSending;

    private bool CanCancel => CanControl && isSending;

    private bool CanTryAgain => CanControl && ShowsProblem;

    private bool CanChangeVolume => CanControl && !isSending;

    private bool CanStepVolume => CanChangeVolume && HasVolume;

    private MediaSettings Media => settings.Current.Media;

    /// <summary>Reads skip distances, the volume step and the time display from the live settings.</summary>
    public void UseSettings(ISettingsService settingsService)
    {
        settings = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        settings.Changed += (_, change) =>
        {
            if (change.Previous.Media != change.Current.Media)
            {
                RaiseAll();
            }
        };
        RaiseAll();
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

    /// <summary>The bar has been taken hold of, at <paramref name="seconds"/>.</summary>
    public void BeginScrub(double seconds)
    {
        if (!CanSkip)
        {
            return;
        }

        scrubPosition = Clamp((long)(seconds * 1000));
        IsScrubbing = true;
        RaisePosition();
    }

    /// <summary>The bar was let go at <paramref name="seconds"/>: one seek, sent now.</summary>
    public void CommitSeek(double seconds)
    {
        IsScrubbing = false;
        if (!CanSkip)
        {
            RaisePosition();
            return;
        }

        SeekTo(Clamp((long)(seconds * 1000)));
    }

    /// <summary>Plays or pauses, or plays again from the start once finished.</summary>
    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private void PlayPause()
    {
        switch (Phase)
        {
            case PlaybackPhase.Finished:
                SeekTo(0);
                Expect(PlaybackPhase.Playing);
                Send(() => remote.SendTransportAsync(TransportAction.Play));
                break;
            case PlaybackPhase.Playing or PlaybackPhase.Buffering:
                Expect(PlaybackPhase.Paused);
                Send(() => remote.SendTransportAsync(TransportAction.Pause));
                break;
            default:
                Expect(PlaybackPhase.Playing);
                Send(() => remote.SendTransportAsync(TransportAction.Play));
                break;
        }

        RaiseAll();
    }

    /// <summary>Goes back by the skip-back distance.</summary>
    [RelayCommand(CanExecute = nameof(CanSkip))]
    private void SkipBack() => SkipBy(-Media.SkipBackSeconds * 1000L);

    /// <summary>Goes forward by the skip-forward distance.</summary>
    [RelayCommand(CanExecute = nameof(CanSkip))]
    private void SkipForward() => SkipBy(Media.SkipForwardSeconds * 1000L);

    /// <summary>Stops what is playing.</summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => Send(remote.StopAsync);

    /// <summary>Abandons the file being sent.</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => Send(remote.CancelSendAsync);

    /// <summary>Sends the file again.</summary>
    [RelayCommand(CanExecute = nameof(CanTryAgain))]
    private void TryAgain() => Send(remote.TryAgainAsync);

    /// <summary>Switches the right-hand time between what is left and the whole length.</summary>
    [RelayCommand]
    private void ToggleTimeDisplay()
    {
        var showTotal = !ShowsTotalTime;
        settings.Update(current => current with { Media = current.Media with { ShowTotalTime = showTotal } });
    }

    /// <summary>Mutes the TV, remembering the level to go back to; or goes back to it.</summary>
    [RelayCommand(CanExecute = nameof(CanStepVolume))]
    private void Mute()
    {
        if (volumeBeforeMute is { } restore)
        {
            SetVolume(restore);
            return;
        }

        volumeBeforeMute = volumePercent;
        volumePercent = 0;
        Send(() => remote.SetVolumeAsync(0f));
        RaiseVolume();
    }

    /// <summary>Turns the TV up by the volume step.</summary>
    [RelayCommand(CanExecute = nameof(CanStepVolume))]
    private void VolumeUp() => SetVolume(StepFrom + Media.VolumeStepPercent);

    /// <summary>Turns the TV down by the volume step.</summary>
    [RelayCommand(CanExecute = nameof(CanStepVolume))]
    private void VolumeDown() => SetVolume(StepFrom - Media.VolumeStepPercent);

    /// <summary>The level a volume step starts from: the one before muting, if muted.</summary>
    /// <remarks>Only read once a volume has been set, so there is always a level.</remarks>
    private int StepFrom => volumeBeforeMute ?? volumePercent.GetValueOrDefault();

    /// <summary>The Media page's keys: Space, Left, Right, Up, Down, M and S.</summary>
    /// <returns>Whether the key did something.</returns>
    public bool HandleKey(Key key)
    {
        var command = key switch
        {
            Key.Space => PlayPauseCommand,
            Key.Left => SkipBackCommand,
            Key.Right => SkipForwardCommand,
            Key.Up => VolumeUpCommand,
            Key.Down => VolumeDownCommand,
            Key.M => MuteCommand,
            Key.S => StopCommand,
            _ => null,
        };
        if (command?.CanExecute(null) != true)
        {
            return false;
        }

        command.Execute(null);
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        StopTimers();
        ownSettings.Dispose();
    }

    /// <summary>Sets the TV's volume, in percent.</summary>
    internal void SetVolume(int percent)
    {
        if (!CanChangeVolume)
        {
            RaiseVolume();
            return;
        }

        var level = Math.Clamp(percent, 0, 100);
        volumePercent = level;
        volumeBeforeMute = null;
        Send(() => remote.SetVolumeAsync(level / 100f));
        RaiseVolume();
    }

    private void SkipBy(long deltaMs)
    {
        gatheredSkip = Clamp((gatheredSkip ?? PositionMs) + deltaMs);
        skipTimer?.Dispose();
        skipTimer = time.CreateTimer(_ => OnUi(SendGatheredSkip), null, SkipGather, Timeout.InfiniteTimeSpan);
        RaisePosition();
    }

    private void SendGatheredSkip()
    {
        // Posted to the UI before the card was cleared or the connection lost: nothing to send.
        if (gatheredSkip is not { } target)
        {
            return;
        }

        gatheredSkip = null;
        SeekTo(target);
    }

    private void SeekTo(long positionMs)
    {
        heldPosition = positionMs;
        heldUntil = time.GetUtcNow() + SeekHold;
        Send(() => remote.SendTransportAsync(TransportAction.SeekTo, positionMs));
        RaisePosition();
    }

    private void Expect(PlaybackPhase phase)
    {
        expectedPhase = phase;
        expectedUntil = time.GetUtcNow() + ToggleHold;
    }

    /// <summary>A position kept within the file. Only seeks into a file of known length.</summary>
    private long Clamp(long positionMs) => Math.Clamp(positionMs, 0, DurationMs);

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

    private void RaiseVolume()
    {
        OnPropertyChanged(nameof(HasVolume));
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(VolumePercent));
        OnPropertyChanged(nameof(VolumeText));
        OnPropertyChanged(nameof(MuteLabel));
        OnPropertyChanged(nameof(MuteName));
        MuteCommand.NotifyCanExecuteChanged();
        VolumeUpCommand.NotifyCanExecuteChanged();
        VolumeDownCommand.NotifyCanExecuteChanged();
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

    /// <summary>A time as the bar shows it: m:ss, or h:mm:ss from an hour.</summary>
    internal static string FormatTime(long milliseconds)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : span.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>A time as a screen reader says it: "1 hour 32 minutes", "12 minutes 40 seconds".</summary>
    internal static string Spoken(long milliseconds)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        var parts = new List<string>(3);
        AddPart(parts, (int)span.TotalHours, "hour");
        AddPart(parts, span.Minutes, "minute");
        AddPart(parts, span.Seconds, "second");
        return parts.Count == 0 ? "0 seconds" : string.Join(' ', parts);
    }

    private static void AddPart(List<string> parts, int count, string unit)
    {
        if (count > 0)
        {
            parts.Add(count == 1 ? $"1 {unit}" : $"{count} {unit}s");
        }
    }
}
