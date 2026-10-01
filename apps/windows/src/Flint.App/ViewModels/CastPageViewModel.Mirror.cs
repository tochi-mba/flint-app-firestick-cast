using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Flint.Core;
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
    /// expect a stream that will never arrive — sending that message once left a TV sitting on a
    /// black "ready for frames" screen with nothing to show for it and nothing on the host side
    /// saying why.
    /// </remarks>
    [RelayCommand]
    private async Task StartScreenSessionAsync()
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
            });
        };

        mirrorStop = new CancellationTokenSource();
        mirrorStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IsMirroring = true;
        Failure = null;
        MirrorStatus = "Starting the mirror.";
        FlintDiag.Info("FlintCast", $"mirror begin maxWidth={MirrorMaxWidth}");
        RaiseDerived();
        OnPropertyChanged(nameof(CanStopMirror));
        StopScreenSessionCommand.NotifyCanExecuteChanged();

        try
        {
            var stats = await runner.RunAsync(
                session,
                new MirrorSessionOptions(MaxWidth: MirrorMaxWidth),
                Environment.MachineName,
                mirrorStop.Token).ConfigureAwait(true);

            // Nothing threw, so the session ran — but a mirror that sent no frames left the TV on
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
            mirrorStop.Dispose();
            mirrorStop = null;
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
