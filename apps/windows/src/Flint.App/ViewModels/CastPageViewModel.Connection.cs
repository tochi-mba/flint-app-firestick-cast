using CommunityToolkit.Mvvm.Input;
using Flint.App.Services;
using Flint.Core;
using Flint.Discovery;
using Flint.Session;

namespace Flint.App.ViewModels;

/// <summary>
/// Finding the television, pairing with it, and remembering where it was.
/// </summary>
/// <remarks>
/// One concern of the Cast page, in its own file so the page stays readable.
/// </remarks>
public sealed partial class CastPageViewModel
{
    /// <summary>Runs the probe and rebuilds the verdict cards.</summary>
    [RelayCommand]
    private Task ProbeAsync(CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(ManualAddress)
            ? RunProbeAsync(prober.ProbeAsync, cancellationToken)
            : ProbeAddressAsync(cancellationToken);

    /// <summary>Probes one user-supplied address when multicast discovery is unavailable.</summary>
    [RelayCommand]
    private async Task ProbeAddressAsync(CancellationToken cancellationToken)
    {
        if (!ManualProbeEndpoint.TryParse(
                ManualAddress,
                ManualPort,
                out var endpoint,
                out var error))
        {
            Failure = error;
            RaiseDerived();
            return;
        }

        var validEndpoint = endpoint!;
        await RunProbeAsync(
            token => prober.ProbeAddressAsync(validEndpoint.Address, validEndpoint.Port, token),
            cancellationToken,
            () => RememberAddress(validEndpoint)).ConfigureAwait(true);
    }

    /// <summary>Connects to the receiver using the pairing code shown on the TV.</summary>
    /// <remarks>
    /// The two branches below have different requirements and must not share one guard. Opening
    /// the receiver over ADB needs ADB authorised; pairing with a code the user already has does
    /// not touch ADB at all, and exists specifically to keep working when ADB does not.
    /// </remarks>
    [RelayCommand]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (Report?.Device is not { } device)
        {
            Failure = "Connect to the TV first, then enter the pairing code shown on its screen.";
            RaiseDerived();
            return;
        }

        IsConnecting = true;
        Failure = null;
        try
        {
            if (string.IsNullOrWhiteSpace(PairingCode))
            {
                if (device.AdbState is not AdbConnectionState.Connected)
                {
                    Failure = "Flint cannot open the receiver automatically because ADB is not "
                        + "authorised on this TV. Open \"Flint Receiver\" on the TV yourself, then "
                        + "type the six-digit code it shows into the field above.";
                    FlintDiag.Warn("FlintCast", $"pair blocked: adb={device.AdbState} no pairing code");
                    return;
                }

                FlintDiag.Info("FlintCast", "pair path=open-receiver-adb");
                await OpenReceiverCoreAsync(device, cancellationToken).ConfigureAwait(true);
                PairingStatus = "Receiver opened on the TV. Enter the six-digit code shown there, then pair.";
                return;
            }

            if (PairingCode.Length != 6 || !PairingCode.All(char.IsDigit))
            {
                Failure = "Enter the six-digit pairing code shown on the TV.";
                FlintDiag.Warn("FlintCast", "pair blocked: pairing code shape invalid");
                return;
            }

            if (!int.TryParse(ReceiverPort, out var port) || port is < 1 or > 65535)
            {
                Failure = "Enter the receiver port shown on the TV.";
                FlintDiag.Warn("FlintCast", "pair blocked: receiver port invalid");
                return;
            }

            PairingStatus = "Pairing with the receiver...";
            FlintDiag.Info(
                "FlintCast",
                $"pair begin address={device.Address} port={port} pairingCodePresent=yes");
            var nextSession = await ConnectReceiverAsync(device.Address, port, PairingCode, cancellationToken)
                .ConfigureAwait(true);
            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(true);
            }

            session = nextSession;
            IsConnected = true;
            _ = WatchAsync(nextSession);
            ListenForPlayback(nextSession);
            PairingStatus = "Paired and ready to cast.";
            FlintDiag.Info(
                "FlintCast",
                $"pair ok browserPort={nextSession.BrowserSecureEndpointPort?.ToString() ?? "(none)"}");
            await ApplyPairedSessionAsync(device, nextSession, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            Failure = exception.Message;
            IsConnected = false;
            FlintDiag.Error("FlintCast", $"pair failed: {exception.GetType().Name}");
        }
        finally
        {
            IsConnecting = false;
            RaiseDerived();
        }
    }

    /// <summary>
    /// After Pair, drop the ADB-install BLOCKED story and attach browser_port from the AUTH-ack
    /// (preferred) or a brief mDNS listen so Web can autofill without retyping.
    /// </summary>
    private async Task ApplyPairedSessionAsync(
        FireTvDevice device,
        CastSession paired,
        CancellationToken cancellationToken)
    {
        if (Report is null)
        {
            return;
        }

        BrowserReceiverEvidence? browserEvidence = device.BrowserEvidence;
        if (paired.BrowserSecureEndpointPort is { } portFromPair)
        {
            browserEvidence = new BrowserReceiverEvidence(2, true, BrowserWebViewProbe.Passed)
            {
                SecureEndpointPort = portFromPair,
            };
        }
        else
        {
            try
            {
                browserEvidence = await FireTvDeviceProbe
                        .TryDiscoverBrowserEvidenceAsync(device.Address, cancellationToken)
                        .ConfigureAwait(true)
                    ?? browserEvidence;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Multicast is best-effort after Pair. A quiet network must not undo a successful pair.
            }
        }

        var updatedDevice = device with { BrowserEvidence = browserEvidence };
        ApplyReport(
            CapabilityAssessor.Assess(updatedDevice, Report.Host, Report.Path, pairedSessionActive: true));
    }

    private void ApplyReport(CapabilityReport report)
    {
        Report = report;
        Modes.Clear();
        foreach (var verdict in report.Verdicts)
        {
            Modes.Add(new ModeVerdictViewModel(verdict));
        }

        // A different television is a different question; what the last one had is not evidence.
        InstalledReceiver = null;
        ReceiverAction = ReceiverSetupAction.Unknown;
    }

    /// <summary>Opens the installed receiver so its pairing code is visible on the TV.</summary>
    [RelayCommand]
    private async Task OpenReceiverAsync(CancellationToken cancellationToken)
    {
        if (Report?.Device is not { IsReachable: true } device)
        {
            Failure = "Connect to the TV first, then open the receiver.";
            RaiseDerived();
            return;
        }

        IsConnecting = true;
        Failure = null;
        try
        {
            await OpenReceiverCoreAsync(device, cancellationToken).ConfigureAwait(true);
            PairingStatus = "Receiver opened on the TV. Enter the six-digit code shown there, then pair.";
        }
        catch (Exception exception)
        {
            Failure = DescribeTelevisionFailure(exception);
            PairingStatus = "Flint could not open the receiver on the TV.";
        }
        finally
        {
            IsConnecting = false;
            RaiseDerived();
        }
    }

    /// <summary>Clears saved direct addresses from both memory and persistent storage.</summary>
    [RelayCommand]
    private void ClearRecentAddresses()
    {
        addressStore.Clear();
        RecentAddresses.Clear();
    }

    private async Task RunProbeAsync(
        Func<CancellationToken, Task<CapabilityReport>> run,
        CancellationToken cancellationToken,
        Action? onSuccess = null)
    {
        IsProbing = true;
        Failure = null;
        FlintDiag.Info("FlintCast", "probe begin");
        try
        {
            var report = await run(cancellationToken).ConfigureAwait(true);
            ApplyReport(report);
            onSuccess?.Invoke();
            RefreshPairingGuidance();
            await IdentifyReceiverAsync(report.Device, cancellationToken).ConfigureAwait(true);
            FlintDiag.Info(
                "FlintCast",
                $"probe ok reachable={report.Device?.IsReachable == true} adb={report.Device?.AdbState} "
                + $"modesAvailable={report.HasAnyAvailableMode} browserPort={report.Device?.BrowserEvidence?.SecureEndpointPort?.ToString() ?? "(none)"}");
        }
        catch (OperationCanceledException)
        {
            FlintDiag.Info("FlintCast", "probe cancelled");
            // A cancelled probe is not a failure; leave the previous report in place.
        }
        catch (Exception exception)
        {
            Failure = exception.Message;
            FlintDiag.Error("FlintCast", $"probe failed: {exception.GetType().Name}");
        }
        finally
        {
            IsProbing = false;
            RaiseDerived();
        }
    }

    /// <summary>
    /// When ADB cannot identify the TV, say plainly that Pair still works from the on-screen code.
    /// </summary>
    private void RefreshPairingGuidance()
    {
        if (Report?.Device is not { } device)
        {
            return;
        }

        if (device.AdbState is AdbConnectionState.Refused or AdbConnectionState.Unauthorized)
        {
            PairingStatus =
                "ADB is unavailable, so Mirror and OPEN RECEIVER stay blocked. Open Flint Receiver "
                + "on the TV, type its six-digit code and receiver port above, then PAIR WITH TV. "
                + "Web can still verify if you type the BROWSER PORT shown on the TV.";
            OnPropertyChanged(nameof(PairingStatus));
        }
    }

    private async Task OpenReceiverCoreAsync(FireTvDevice device, CancellationToken cancellationToken)
    {
        PairingStatus = "Opening the receiver on the TV...";
        FlintDiag.Info("FlintCast", $"open receiver adb={device.AdbState} address={device.Address} package={ReceiverPackageToOpen}");
        await receiverLauncher.LaunchAsync(device, ReceiverPackageToOpen, cancellationToken).ConfigureAwait(true);
        FlintDiag.Info("FlintCast", "open receiver launch issued");
    }

    private static async Task<CastSession> ConnectReceiverAsync(
        System.Net.IPAddress address,
        int port,
        string pairingCode,
        CancellationToken cancellationToken)
    {
        const int MAX_ATTEMPTS = 12;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await CastSession.ConnectAsync(address, port, pairingCode, cancellationToken: cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (Exception exception) when (attempt < MAX_ATTEMPTS
                && exception is System.Net.Sockets.SocketException or IOException)
            {
                FlintDiag.Warn(
                    "FlintCast",
                    $"pair connect retry attempt={attempt}/{MAX_ATTEMPTS} err={exception.GetType().Name}");
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(true);
            }
        }
    }

    private void RememberAddress(ManualProbeEndpoint endpoint)
    {
        var address = new RecentAddress(endpoint.Address.ToString(), endpoint.Port);
        addressStore.Remember(address);
        var existing = RecentAddresses.FirstOrDefault(item =>
            string.Equals(item.Address, address.Address, StringComparison.OrdinalIgnoreCase)
            && item.Port == address.Port);
        if (existing is not null)
        {
            RecentAddresses.Remove(existing);
        }

        RecentAddresses.Insert(0, address);
        while (RecentAddresses.Count > FileRecentAddressStore.MaxEntries)
        {
            RecentAddresses.RemoveAt(RecentAddresses.Count - 1);
        }
    }
}
