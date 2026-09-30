using CommunityToolkit.Mvvm.Input;
using Flint.Core;
using Flint.Discovery;

namespace Flint.App.ViewModels;

/// <summary>
/// The receiver on the television: what is installed, and the one button that installs, updates or opens it.
/// </summary>
/// <remarks>
/// Its own file because it is one concern of the Cast page with its own state and its own tests,
/// and the page is already long enough to be listed for decomposition.
/// </remarks>
public sealed partial class CastPageViewModel
{
    /// <summary>The Flint package the television reported, once ADB has been asked.</summary>
    public InstalledReceiver? InstalledReceiver { get; private set; }

    /// <summary>What the one receiver button does next: install, update or open.</summary>
    public ReceiverSetupAction ReceiverAction { get; private set; } = ReceiverSetupAction.Unknown;

    /// <summary>The label on the receiver button, which changes with <see cref="ReceiverAction"/>.</summary>
    public string ReceiverActionLabel => ReceiverSetup.ActionLabel(ReceiverAction);

    /// <summary>
    /// Whether the receiver button can be pressed.
    /// </summary>
    /// <remarks>
    /// Until ADB has said what is on the television, the button still offers to open the receiver,
    /// as it always did: a person who knows it is installed should not have to wait for a look
    /// that may be blocked by the television's own prompt.
    /// </remarks>
    public bool CanRunReceiverAction =>
        CanOpenReceiver && ReceiverAction is not ReceiverSetupAction.NothingBundled;

    /// <summary>What the card says about the receiver on this television.</summary>
    public string ReceiverSetupStatus => Report?.Device is { } device
        ? ReceiverSetup.Describe(ReceiverAction, bundledReceiver.Describe(), InstalledReceiver, device.FriendlyName)
        : "Find the TV first.";

    /// <summary>Bring required setup into view without exposing it on every connection.</summary>
    public bool NeedsReceiverSetup => ReceiverAction is ReceiverSetupAction.Install or ReceiverSetupAction.Update or ReceiverSetupAction.NothingBundled;

    /// <summary>Progress of a running install, as text, or blank.</summary>
    public string InstallProgress { get; private set; } = string.Empty;

    /// <summary>
    /// Asks the television which Flint package it has, so the receiver button can say what it will do.
    /// </summary>
    /// <remarks>
    /// Read-only, and only once ADB is authorised: the look never triggers the television's prompt
    /// by itself, because the probe that preceded it already did or already could not.
    /// </remarks>
    private async Task IdentifyReceiverAsync(CancellationToken cancellationToken)
    {
        if (Report?.Device is not { AdbState: AdbConnectionState.Connected } device)
        {
            return;
        }

        var bundled = bundledReceiver.Describe();
        var candidates = new List<string>();
        if (bundled is not null)
        {
            candidates.Add(bundled.PackageName);
        }

        candidates.AddRange(BundledReceiver.KnownPackages.Where(name => !candidates.Contains(name)));
        try
        {
            InstalledReceiver = await receiverInstaller.FindInstalledAsync(device, candidates, cancellationToken)
                .ConfigureAwait(true);
            ReceiverAction = ReceiverSetup.Decide(bundled, InstalledReceiver);
            FlintDiag.Info(
                "FlintCast",
                $"receiver identified installed={InstalledReceiver?.PackageName ?? "(none)"} "
                + $"version={InstalledReceiver?.VersionLabel ?? "(none)"} action={ReceiverAction}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            FlintDiag.Warn("FlintCast", "receiver identify timed out");
        }
        catch (Exception exception) when (exception is IOException or AdbProtocolException or InvalidOperationException)
        {
            // Not knowing is allowed. The button falls back to plain "open", as it always offered.
            FlintDiag.Warn("FlintCast", $"receiver identify failed: {exception.GetType().Name}");
        }
        finally
        {
            RaiseReceiverSetup();
        }
    }

    /// <summary>
    /// Does whatever the receiver button says: installs or updates the bundled receiver, then opens it.
    /// </summary>
    [RelayCommand]
    private async Task RunReceiverActionAsync(CancellationToken cancellationToken)
    {
        if (Report?.Device is not { } device)
        {
            Failure = "Connect to the TV first.";
            RaiseDerived();
            return;
        }

        if (!ReceiverSetup.Installs(ReceiverAction))
        {
            await OpenReceiverAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        var bundled = bundledReceiver.Describe();
        if (bundled is null)
        {
            Failure = "This copy of Flint carries no receiver to install.";
            RaiseDerived();
            return;
        }

        IsConnecting = true;
        Failure = null;
        var verb = ReceiverAction is ReceiverSetupAction.Update ? "Updating" : "Installing";
        try
        {
            PairingStatus = $"{verb} Flint on the TV. The screen may go dark for a moment.";
            OnPropertyChanged(nameof(PairingStatus));
            FlintDiag.Info("FlintCast", $"receiver install begin action={ReceiverAction} package={bundled.PackageName} code={bundled.VersionCode}");
            var apk = await bundledReceiver.ReadAsync(cancellationToken).ConfigureAwait(true);
            var progress = new Progress<double>(fraction =>
            {
                InstallProgress = $"{fraction * 100:F0}% copied";
                OnPropertyChanged(nameof(InstallProgress));
            });
            var outcome = await receiverInstaller.InstallAsync(device, apk, progress, cancellationToken).ConfigureAwait(true);
            if (!outcome.Succeeded)
            {
                Failure = $"The TV refused the package: {(outcome.Output.Length == 0 ? "it did not say why" : outcome.Output)}.";
                PairingStatus = "Flint was not installed on the TV.";
                FlintDiag.Warn("FlintCast", $"receiver install refused: {outcome.Output}");
                return;
            }

            // "Success" is what the installer said. Asking again is what proves it.
            InstalledReceiver = await receiverInstaller.FindInstalledAsync(device, [bundled.PackageName], cancellationToken)
                .ConfigureAwait(true);
            if (InstalledReceiver is null)
            {
                Failure = $"The installer answered Success, but {bundled.PackageName} is not on the TV afterwards.";
                PairingStatus = "Flint was not installed on the TV.";
                return;
            }

            ReceiverAction = ReceiverSetup.Decide(bundled, InstalledReceiver);
            FlintDiag.Info("FlintCast", "receiver install ok");
            await OpenReceiverCoreAsync(device, cancellationToken).ConfigureAwait(true);
            PairingStatus = $"Flint {bundled.VersionName} is on the TV and open. Enter the six-digit code shown there, then pair.";
        }
        catch (OperationCanceledException)
        {
            PairingStatus = "The install was cancelled.";
        }
        catch (Exception exception) when (exception is IOException or AdbProtocolException or InvalidOperationException)
        {
            Failure = exception.Message;
            PairingStatus = "Flint could not install the receiver on the TV.";
            FlintDiag.Error("FlintCast", $"receiver install failed: {exception.GetType().Name}");
        }
        finally
        {
            InstallProgress = string.Empty;
            IsConnecting = false;
            OnPropertyChanged(nameof(InstallProgress));
            OnPropertyChanged(nameof(PairingStatus));
            RaiseReceiverSetup();
            RaiseDerived();
        }
    }

    private void RaiseReceiverSetup()
    {
        OnPropertyChanged(nameof(InstalledReceiver));
        OnPropertyChanged(nameof(NeedsReceiverSetup));
        OnPropertyChanged(nameof(ReceiverAction));
        OnPropertyChanged(nameof(ReceiverActionLabel));
        OnPropertyChanged(nameof(CanRunReceiverAction));
        OnPropertyChanged(nameof(ReceiverSetupStatus));
    }

    /// <summary>The package to open: the one the TV reported, else the one this build would install.</summary>
    private string ReceiverPackageToOpen =>
        InstalledReceiver?.PackageName
        ?? bundledReceiver.Describe()?.PackageName
        ?? BundledReceiver.DebugPackage;
}
