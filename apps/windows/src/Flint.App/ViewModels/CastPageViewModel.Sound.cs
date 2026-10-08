using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Flint.App.Services;
using Flint.Core;
using Flint.Core.Settings;
using Flint.Session;

namespace Flint.App.ViewModels;

/// <summary>
/// Sharing this PC's sound beside its screen.
/// </summary>
/// <remarks>
/// Sound runs on its own worker beside the picture's and is started, stopped and changed without
/// touching the picture: a failure here is a state the Screen page shows, never a stopped share.
/// </remarks>
public sealed partial class CastPageViewModel
{
    private AudioPump? soundPump;
    private CancellationTokenSource? soundStop;
    private Task? soundRun;
    private bool muteAsked;

    /// <summary>What shared sound is doing; off while nothing is shared.</summary>
    [ObservableProperty]
    private AudioShareState _soundState;

    /// <summary>Why sound is unavailable, in words for the person, or null.</summary>
    [ObservableProperty]
    private string? _soundProblem;

    /// <summary>How loud the shared sound is, from 0 to 1 as Windows captures it.</summary>
    [ObservableProperty]
    private float _soundLevel;

    /// <summary>A one-off explanation of something Flint did with this PC's sound, or null.</summary>
    [ObservableProperty]
    private string? _soundNotice;

    /// <summary>Whether this PC's output is muted for the TV right now.</summary>
    [ObservableProperty]
    private bool _isMutingThisPc;

    /// <summary>Raised on the UI thread with each look at the running sound share.</summary>
    public event Action<AudioPumpReport>? SoundReported;

    /// <summary>This PC's sound outputs, for the Settings page.</summary>
    internal IReadOnlyList<AudioDevice> SoundOutputs() => audioEngine.ListDevices();

    /// <summary>Whether sound is being shared now.</summary>
    internal bool IsSharingSound => soundRun is not null;

    private ScreenSettings ScreenChoices => settings?.Current.Screen ?? new ScreenSettings();

    /// <summary>Starts sound beside a running share, as the settings ask.</summary>
    internal void StartSound()
    {
        if (!IsMirroring || session is not { } sharing || mirrorControl is not { } control || soundRun is not null)
        {
            return;
        }

        var screen = ScreenChoices;
        var options = new AudioShareOptions(
            SharedOutput(screen),
            screen.SoundKbps,
            DelayMilliseconds: screen.SoundDelayMilliseconds);
        var pump = new AudioPump(audioEngine, time);
        pump.Reported += report => Dispatcher.UIThread.Post(() => OnSoundReported(pump, report));
        soundPump = pump;
        soundStop = new CancellationTokenSource();
        muteAsked = false;
        SoundState = AudioShareState.Ready;
        SoundProblem = null;
        SoundNotice = null;
        SoundLevel = 0;
        FlintDiag.Info("FlintCast", $"sound begin kbps={options.BitrateKbps} named={options.DeviceId is not null}");
        soundRun = RunSoundAsync(pump, sharing, options, control, soundStop);
    }

    /// <summary>Sets the TV's volume, for a played file and for shared sound alike.</summary>
    internal async Task SetTvVolumeAsync(float level)
    {
        try
        {
            await LiveSession().SetVolumeAsync(level).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The TV went away as the slider moved; the page says the connection ended on its own.
        }
    }

    /// <summary>Stops shared sound and waits until it has stopped; the picture carries on.</summary>
    internal async Task StopSoundAsync()
    {
        if (soundRun is not { } run)
        {
            return;
        }

        soundStop!.Cancel();
        await run.ConfigureAwait(true);
    }

    /// <summary>Stops sound and puts this PC's output back, as Flint closes.</summary>
    internal void EndSound()
    {
        soundStop?.Cancel();
        tvOnly.End();
        IsMutingThisPc = false;
    }

    private async Task RunSoundAsync(
        AudioPump pump,
        CastSession sharing,
        AudioShareOptions options,
        MirrorControl control,
        CancellationTokenSource stop)
    {
        AudioPumpEnd end;
        try
        {
            end = await pump.RunAsync(sharing, options, control, stop.Token).ConfigureAwait(true);
        }
        finally
        {
            stop.Dispose();
        }

        soundPump = null;
        soundStop = null;
        soundRun = null;
        tvOnly.End();
        IsMutingThisPc = false;
        SoundLevel = 0;
        SoundProblem = end.Problem;
        SoundState = end.Problem is null ? AudioShareState.Off : AudioShareState.Unavailable;
        FlintDiag.Info("FlintCast", $"sound ended packets={end.Stats.Packets} dropped={end.Stats.Dropped} failed={end.Problem is not null}");
    }

    /// <summary>Takes in a look at a sound share; one from a share since replaced is ignored.</summary>
    internal void OnSoundReported(AudioPump pump, AudioPumpReport report)
    {
        if (!ReferenceEquals(pump, soundPump))
        {
            return;
        }

        SoundState = report.Stats.State;
        SoundProblem = report.Problem;
        SoundLevel = report.Stats.Level;

        // Asked once the share is sending: a share that starts paused mutes when it resumes.
        if (!muteAsked && !IsMirrorPaused)
        {
            muteAsked = true;
            MuteForTvOnly();
        }

        Notice(tvOnly.Observe(report.Stats));
        SoundReported?.Invoke(report);
    }

    /// <summary>Mutes this PC when sound plays on the TV only and the share is not paused.</summary>
    private void MuteForTvOnly()
    {
        var screen = ScreenChoices;
        if (screen.SoundDestination is not SoundDestination.TvOnly || IsMirrorPaused || SoundOutputToMute(screen) is not { } device)
        {
            return;
        }

        IsMutingThisPc = tvOnly.Begin(device);
    }

    /// <summary>The output sound is captured from: the chosen one, or null for whichever Windows plays through.</summary>
    private static string? SharedOutput(ScreenSettings screen) =>
        screen.SoundSource is SoundSource.NamedDevice ? screen.SoundDeviceIdentity : null;

    /// <summary>The output "TV only" mutes: the chosen one, or the one Windows plays through now.</summary>
    private string? SoundOutputToMute(ScreenSettings screen) =>
        SharedOutput(screen) ?? audioEngine.ListDevices().FirstOrDefault(output => output.IsDefault)?.Id;

    private void Notice(MuteNews news)
    {
        if (news is MuteNews.None)
        {
            return;
        }

        IsMutingThisPc = false;
        SoundNotice = news is MuteNews.PersonUnmuted
            ? "You turned this PC's sound back on, so Flint leaves it on for the rest of this share."
            : "Muting this PC silenced the sound Flint shares, so this output plays on the TV and this PC from now on.";
        FlintDiag.Info("FlintCast", $"sound mute {news}");
    }

    /// <summary>Pauses or resumes the "TV only" mute with the share.</summary>
    private void FollowPauseWithMute()
    {
        if (!IsSharingSound)
        {
            return;
        }

        if (IsMirrorPaused)
        {
            tvOnly.Suspend();
            IsMutingThisPc = false;
        }
        else
        {
            IsMutingThisPc = tvOnly.Resume();
        }
    }

    /// <summary>Applies a change to the sound settings to a running share.</summary>
    private void OnSoundSettingsChanged(object? sender, SettingsChangedEventArgs change)
    {
        var before = change.Previous.Screen;
        var now = change.Current.Screen;
        if (!IsMirroring)
        {
            return;
        }

        if (before.ShareSound != now.ShareSound)
        {
            if (now.ShareSound)
            {
                StartSound();
            }
            else
            {
                _ = StopSoundAsync();
            }

            return;
        }

        var captureChanged = SharedOutput(before) != SharedOutput(now) || before.SoundKbps != now.SoundKbps;
        if (soundPump is not { } pump)
        {
            // Sound that could not start gets another go once what it would capture changes.
            if (captureChanged && now.ShareSound)
            {
                StartSound();
            }

            return;
        }

        if (captureChanged)
        {
            _ = RestartSoundAsync();
            return;
        }

        pump.SetDelay(now.SoundDelayMilliseconds);
        if (before.SoundDestination != now.SoundDestination)
        {
            if (now.SoundDestination is SoundDestination.TvOnly)
            {
                MuteForTvOnly();
            }
            else
            {
                tvOnly.End();
                IsMutingThisPc = false;
            }
        }
    }

    /// <summary>Stops sound and starts it again with the settings now in force.</summary>
    private async Task RestartSoundAsync()
    {
        await StopSoundAsync().ConfigureAwait(true);
        StartSound();
    }
}
