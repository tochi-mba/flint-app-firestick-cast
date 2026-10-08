using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Flint.Core;
using Flint.Core.Settings;

namespace Flint.App.ViewModels;

/// <summary>Pausing the share for privacy: by hand, or when this PC locks.</summary>
public sealed partial class ScreenPageViewModel
{
    /// <summary>How often the time paused is brought up to date.</summary>
    internal static readonly TimeSpan PauseTick = TimeSpan.FromSeconds(1);

    private ITimer? pauseTimer;
    private DateTimeOffset pausedAt;
    private bool pausedByLock;

    /// <summary>Whether the share is paused.</summary>
    public bool IsPaused => Cast.IsMirrorPaused;

    /// <summary>Whether PAUSE is offered: a share is running and sending.</summary>
    public bool CanPause => Cast.IsMirroring && !Cast.IsMirrorPaused;

    /// <summary>What the TV is doing while paused, or null while it is not.</summary>
    public string? PausedBanner => Cast.MirrorPause switch
    {
        MirrorPause.HoldingLastPicture => "Paused. The TV is holding the last picture.",
        MirrorPause.Black => "Paused. The TV is showing a black screen.",
        _ => null,
    };

    /// <summary>How long the share has been paused, such as "Paused for 1:05".</summary>
    [ObservableProperty]
    private string? _pausedFor;

    /// <summary>Stops sending the screen until RESUME; the TV does as the settings say.</summary>
    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause() => PauseFor(byLock: false);

    /// <summary>Sends the screen again, at once.</summary>
    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void Resume()
    {
        pausedByLock = false;
        Cast.ResumeMirror();
    }

    /// <summary>This PC was locked: pause, if the settings say to.</summary>
    public void OnPcLocked()
    {
        if (Screen.PauseWhenLocked && CanPause)
        {
            PauseFor(byLock: true);
        }
    }

    /// <summary>This PC was unlocked: resume a pause the lock made, unless the settings say to wait.</summary>
    /// <remarks>A pause the person made themselves is theirs to end; unlocking never ends it.</remarks>
    public void OnPcUnlocked()
    {
        if (pausedByLock && !Screen.StayPausedAfterUnlock)
        {
            Resume();
        }
    }

    private void PauseFor(bool byLock)
    {
        var how = Screen.PausedPicture is PausedPicture.Black ? MirrorPause.Black : MirrorPause.HoldingLastPicture;
        if (Cast.PauseMirror(how))
        {
            pausedByLock = byLock;
        }
    }

    /// <summary>Keeps the banner, the timer and the buttons in step with the share.</summary>
    private void OnPauseChanged()
    {
        if (IsPaused)
        {
            pausedAt = time.GetUtcNow();
            ShowTimePaused();
            pauseTimer ??= time.CreateTimer(_ => OnUi(ShowTimePaused), null, PauseTick, PauseTick);
        }
        else
        {
            StopPauseTimer();
            PausedFor = null;
            pausedByLock = false;
        }

        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(PausedBanner));
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Brings the time paused up to date; a tick that arrives after resuming changes nothing.</summary>
    internal void ShowTimePaused()
    {
        if (!IsPaused)
        {
            return;
        }

        var paused = time.GetUtcNow() - pausedAt;
        var format = paused >= TimeSpan.FromHours(1) ? @"h\:mm\:ss" : @"m\:ss";
        PausedFor = $"Paused for {paused.ToString(format, CultureInfo.InvariantCulture)}";
    }

    private void StopPauseTimer()
    {
        pauseTimer?.Dispose();
        pauseTimer = null;
    }

    /// <summary>Runs <paramref name="action"/> where the page's bindings read it.</summary>
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
}
