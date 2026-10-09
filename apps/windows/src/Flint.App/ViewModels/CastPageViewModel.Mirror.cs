using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Flint.Core;
using Flint.Core.Settings;
using Flint.Protocol;
using Flint.Session;

namespace Flint.App.ViewModels;

/// <summary>
/// Sharing this screen on the television.
/// </summary>
/// <remarks>
/// One concern of the Cast page, in its own file so the page stays readable.
/// </remarks>
public sealed partial class CastPageViewModel
{
    private MirrorControl? mirrorControl;

    /// <summary>Raised on the UI thread with a running share's counters, after each frame sent.</summary>
    public event Action<MirrorSessionStats>? MirrorStatsUpdated;

    /// <summary>Raised on the UI thread once a change to a running share was made or refused.</summary>
    public event Action<MirrorSwitch>? MirrorSwitched;

    /// <summary>Raised on the UI thread each time a share starts sending a picture.</summary>
    public event Action<MirrorPicture>? MirrorPictureStarted;

    /// <summary>Raised on the UI thread with the TV's own counters while a share runs.</summary>
    public event Action<StatsMessage>? MirrorReceiverStats;

    /// <summary>The TV's own screen width, as it said when it connected; the protocol requires one.</summary>
    internal int? TvScreenWidth => (session?.PeerHello.Message as HelloMessage)?.ScreenWidth;

    /// <summary>What a share of <paramref name="display"/> sends, as the settings ask.</summary>
    /// <param name="display">The display to share, or null for the first one capture finds.</param>
    internal MirrorSessionOptions MirrorOptionsFor(DisplayInfo? display) =>
        ScreenQualityPreset.ToOptions(
            settings?.Current.Screen ?? new ScreenSettings(),
            display?.Index ?? 0,
            TvScreenWidth,
            display?.Width);

    /// <summary>Asks a running share to send <paramref name="options"/> instead.</summary>
    /// <returns>False when nothing is being shared.</returns>
    internal bool ChangeMirror(MirrorSessionOptions options)
    {
        if (mirrorControl is not { } control)
        {
            return false;
        }

        FlintDiag.Info("FlintCast", $"mirror change display={options.OutputIndex} width={options.MaxWidth} fps={options.FrameRate}");
        control.Change(options);
        return true;
    }

    /// <summary>Draws the pointer, or stops, in a running share as soon as the setting changes.</summary>
    private void OnPointerSettingChanged(object? sender, SettingsChangedEventArgs change) =>
        mirrorControl?.SetShowPointer(change.Current.Screen.ShowPointer);

    /// <summary>Whether the Screen page should offer Stop rather than only Start.</summary>
    public bool CanStopMirror => IsMirroring;

    /// <summary>
    /// Whether pressing Start would begin mirroring now: this PC can mirror to this TV, a session
    /// is paired, and no mirror is already running.
    /// </summary>
    public bool CanStartMirrorNow => MirrorVerdict is { IsOfferable: true } && IsSessionConnected && !IsMirroring;

    /// <summary>
    /// Mirrors this screen onto the connected receiver until it is stopped.
    /// </summary>
    /// <remarks>
    /// Both guards below are reachable by invoking the command directly rather than through the
    /// button, which is disabled in either case. They refuse rather than telling the receiver to
    /// expect a stream that will never arrive - sending that message once left a TV sitting on a
    /// black "ready for frames" screen with nothing to show for it and nothing on the host side
    /// saying why.
    /// </remarks>
    [RelayCommand]
    private Task StartScreenSessionAsync() => StartMirrorAsync(MirrorOptionsFor(null));

    /// <summary>Shares the screen with <paramref name="options"/>, refusing as the command does.</summary>
    internal async Task StartMirrorAsync(MirrorSessionOptions options)
    {
        if (MirrorVerdict is not { IsOfferable: true })
        {
            Failure = MirrorVerdict?.Reason
                ?? "Flint cannot mirror to this receiver.";
            RaiseDerived();
            return;
        }

        if (!IsSessionConnected || session is null)
        {
            Failure = "Enter the pairing code on Cast and connect the receiver first.";
            RaiseDerived();
            return;
        }

        if (IsMirroring)
        {
            Failure = "Screen mirroring is already running. Stop it before starting again.";
            RaiseDerived();
            return;
        }

        if (coordinator is not null && !await coordinator.TakeAsync(TvSurfaceKind.Mirror).ConfigureAwait(true))
        {
            return;
        }

        var runner = new ScreenMirrorRunner(mirrorEngine);
        runner.StatsUpdated += stats =>
        {
            // The native session deliberately lives on its own thread for COM/DXGI affinity.
            // Avalonia-bound state must return to the UI dispatcher before it changes.
            Dispatcher.UIThread.Post(() =>
            {
                MirrorStatus =
                    $"Mirroring: {stats.FramesEncoded} frames, {stats.BytesEncoded / 1024} KiB sent.";
                OnPropertyChanged(nameof(MirrorStatus));
                MirrorStatsUpdated?.Invoke(stats);
            });
        };
        runner.PictureStarted += picture => Dispatcher.UIThread.Post(() => MirrorPictureStarted?.Invoke(picture));
        var control = new MirrorControl();
        control.SetShowPointer(ScreenChoices.ShowPointer);
        control.Switched += switched => Dispatcher.UIThread.Post(() => MirrorSwitched?.Invoke(switched));
        var sharing = session;
        void OnReceiverStats(StatsMessage stats) => Dispatcher.UIThread.Post(() => MirrorReceiverStats?.Invoke(stats));
        sharing.StatsReceived += OnReceiverStats;

        mirrorStop = new CancellationTokenSource();
        mirrorStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mirrorControl = control;
        IsMirroring = true;
        Failure = null;
        MirrorStatus = "Starting the mirror.";
        FlintDiag.Info(
            "FlintCast",
            $"mirror begin display={options.OutputIndex} maxWidth={options.MaxWidth} fps={options.FrameRate} bitrate={options.BitrateBitsPerSecond}");
        RaiseDerived();
        OnPropertyChanged(nameof(CanStopMirror));
        StopScreenSessionCommand.NotifyCanExecuteChanged();

        // Beside the picture, never ahead of it: sound waits for the picture's clock to start.
        if (ScreenChoices.ShareSound)
        {
            StartSound();
        }

        try
        {
            var stats = await runner.RunAsync(
                session,
                options,
                Environment.MachineName,
                control,
                mirrorStop.Token).ConfigureAwait(true);

            // Nothing threw, so the session ran - but a mirror that sent no frames left the TV on
            // an empty surface, and that must not read as success.
            MirrorStatus = stats.FramesEncoded > 0
                ? $"Mirror stopped after {stats.FramesEncoded} frames."
                : "The mirror ran but sent no frames.";
            if (stats.FramesEncoded == 0)
            {
                Failure = "Flint captured this screen but encoded nothing from it.";
                FlintDiag.Warn("FlintCast", "mirror ended with zero frames");
            }
            else
            {
                FlintDiag.Info("FlintCast", $"mirror ended frames={stats.FramesEncoded}");
            }
        }
        catch (OperationCanceledException)
        {
            MirrorStatus = "Mirror stopped.";
            FlintDiag.Info("FlintCast", "mirror cancelled");
        }
        catch (Exception exception)
        {
            MirrorStatus = "The mirror stopped unexpectedly.";
            Failure = exception.Message;
            FlintDiag.Error("FlintCast", $"mirror failed: {exception.GetType().Name}");
        }
        finally
        {
            await StopSoundAsync().ConfigureAwait(true);
            sharing.StatsReceived -= OnReceiverStats;
            mirrorStop.Dispose();
            mirrorStop = null;
            mirrorControl = null;

            // A pause belongs to the share it paused: the next share starts sending.
            MirrorPause = MirrorPause.Running;
            IsMirroring = false;
            mirrorStopped?.TrySetResult();
            mirrorStopped = null;
            RaiseDerived();
            OnPropertyChanged(nameof(CanStopMirror));
            StopScreenSessionCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Stops a running mirror session.</summary>
    [RelayCommand(CanExecute = nameof(CanStopMirror))]
    private void StopScreenSession() => _ = StopMirrorAsync();

    /// <summary>Cancels the mirror loop and waits until host cleanup finishes.</summary>
    internal async Task StopMirrorAsync(CancellationToken cancellationToken = default)
    {
        if (!IsMirroring)
        {
            return;
        }

        var pending = mirrorStopped;
        mirrorStop?.Cancel();
        if (pending is null)
        {
            // Flag without a live loop (torn-down runner / test seam). Clear so PrepareFor can
            // reclaim the glass instead of leaving IsMirroring stuck true forever.
            IsMirroring = false;
            MirrorStatus = "Mirror stopped.";
            RaiseDerived();
            OnPropertyChanged(nameof(CanStopMirror));
            StopScreenSessionCommand.NotifyCanExecuteChanged();
            return;
        }

        try
        {
            await pending.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled waiting; the mirror may still be tearing down.
        }
    }
}
