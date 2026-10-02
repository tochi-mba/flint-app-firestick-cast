using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Flint.Core.Media;
using Flint.Protocol;

namespace Flint.App.ViewModels;

/// <summary>
/// Moving through the file: play, pause, seek, skip, stop, and the Media page's keys.
/// </summary>
public sealed partial class NowPlayingViewModel
{
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

    private bool CanPlayPause => CanControl && !isSending && !IsPicture && snapshot is not null
        && Phase is not PlaybackPhase.Problem;

    private bool CanSkip => CanPlayPause && HasDuration;

    private bool CanStop => CanControl && !isSending;

    private bool CanCancel => CanControl && isSending;

    private bool CanTryAgain => CanControl && ShowsProblem;

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
        settings!.Update(current => current with { Media = current.Media with { ShowTotalTime = showTotal } });
    }

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
}
