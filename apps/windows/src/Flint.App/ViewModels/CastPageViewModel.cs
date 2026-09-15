using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using Flint.App.Controls;
using Flint.App.Services;
using Flint.Core;
using Flint.Discovery;
using Flint.Engine.Interop;
using Flint.Protocol;
using Flint.Session;

namespace Flint.App.ViewModels;

/// <summary>
/// The Cast page and local receiver connection screen.
/// </summary>
/// <remarks>
/// This page answers one question — what can this PC and this television actually do together — and
/// refuses to answer it before the probe has run.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The mirror cancellation source is disposed when the mirror stops and never carries a timer; the page lives as long as the window.")]
public sealed partial class CastPageViewModel : ObservableObject
{
    /// <summary>
    /// How long Flint waits for the file to reach the receiver and for it to confirm playback,
    /// before giving up locally.
    /// </summary>
    /// <remarks>
    /// Covers the whole push — not just decode — because the file's bytes travel over this same
    /// connection before the receiver can even try to play them (see
    /// <see cref="Flint.Session.CastSession.PushMediaAndWaitForPlaybackStartAsync"/>). Kept well
    /// ahead of the receiver's own decode timeout so its own, more specific error reaches the user
    /// first rather than Flint's generic "the TV did not confirm playback" winning the race by
    /// being impatient.
    /// </remarks>
    private static readonly TimeSpan MediaStartTimeout = TimeSpan.FromSeconds(60);
    /// <summary>
    /// How wide a mirrored frame may be before it is scaled down.
    /// </summary>
    /// <remarks>
    /// A 4K desktop encoded at full width costs far more to encode and to send than a television
    /// twelve feet away can show the difference of, so the default trades width no one can see for
    /// latency everyone can.
    /// </remarks>
    private const uint MirrorMaxWidth = 1920;
    private readonly CapabilityProber prober;
    private readonly IRecentAddressStore addressStore;
    private readonly IReceiverLauncher receiverLauncher;
    private readonly IMirrorEngine mirrorEngine;
    private CastSession? session;
    private CancellationTokenSource? mirrorStop;
    private TaskCompletionSource? mirrorStopped;
    private ModeSessionCoordinator? coordinator;

    public CastPageViewModel(
        CapabilityProber prober,
        IRecentAddressStore? addressStore = null,
        IReceiverLauncher? receiverLauncher = null,
        IMirrorEngine? mirrorEngine = null)
    {
        this.prober = prober ?? throw new ArgumentNullException(nameof(prober));
        this.addressStore = addressStore ?? new FileRecentAddressStore();
        this.receiverLauncher = receiverLauncher ?? new AdbReceiverLauncher();
        this.mirrorEngine = mirrorEngine ?? new NativeMirrorEngine();

        var recent = this.addressStore.Load();
        RecentAddresses = new ObservableCollection<RecentAddress>(recent);
        if (recent is [var latest, ..])
        {
            ManualAddress = latest.Address;
            ManualPort = latest.Port?.ToString() ?? string.Empty;
        }
    }

    /// <summary>Wires the shell-level exclusive-surface coordinator.</summary>
    internal void AttachCoordinator(ModeSessionCoordinator modeCoordinator) =>
        coordinator = modeCoordinator ?? throw new ArgumentNullException(nameof(modeCoordinator));

    [ObservableProperty]
    private bool _isProbing;

    [ObservableProperty]
    private CapabilityReport? _report;

    [ObservableProperty]
    private string? _failure;

    [ObservableProperty]
    private string _manualAddress = string.Empty;

    [ObservableProperty]
    private string _manualPort = string.Empty;

    [ObservableProperty]
    private string _pairingCode = string.Empty;

    [ObservableProperty]
    private string _receiverPort = CastSession.DefaultPort.ToString();

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopMediaCommand))]
    private bool isMediaPlaying;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopScreenSessionCommand))]
    private bool _isMirroring;

    [ObservableProperty]
    private string? _mirrorStatus;

    /// <summary>Whether the Screen page should offer Stop rather than only Start.</summary>
    public bool CanStopMirror => IsMirroring;

    /// <summary>Whether Media is actively playing on the TV from this PC.</summary>
    public bool CanStopMedia => IsMediaPlaying && IsSessionConnected;

    /// <summary>The saved direct endpoints, newest first.</summary>
    public ObservableCollection<RecentAddress> RecentAddresses { get; }

    /// <summary>Whether a live Flint receiver session is connected.</summary>
    public bool ReceiverReady => IsConnected || Report?.Device?.IsReachable == true;

    /// <summary>Whether an authenticated receiver session can accept media commands.</summary>
    public bool IsSessionConnected => IsConnected && session?.IsConnected == true;

    /// <summary>Whether the current receiver can be brought to the foreground over ADB.</summary>
    public bool CanOpenReceiver => Report?.Device?.AdbState is AdbConnectionState.Connected && !IsBusy;

    /// <summary>
    /// Whether a pairing code can be entered and submitted.
    /// </summary>
    /// <remarks>
    /// Pairing by code talks directly to the receiver's own port using the wire protocol's own
    /// authentication; it never touches ADB. Gating it on ADB, as <see cref="CanOpenReceiver"/>
    /// does, would defeat its purpose: it exists precisely as the path that still works when ADB
    /// is unauthorised, refused, or wedged, which is a routine and separate failure mode. All it
    /// needs is a known address, which a completed probe always leaves behind.
    /// </remarks>
    public bool CanPairWithCode => Report?.Device is not null && !IsBusy;

    /// <summary>Whether the shell is processing a probe, launch, or pairing action.</summary>
    public bool IsBusy => IsProbing || IsConnecting;

    /// <summary>Whether the receiver is connected or ready for a session.</summary>
    public string SessionStatus => IsConnected ? "Connected" : ReceiverReady ? "Receiver ready" : "Not connected";

    /// <summary>Guidance and result text for the receiver-open and pairing sequence.</summary>
    public string PairingStatus { get; private set; } =
        "Open \"Flint Receiver\" on the TV and type its six-digit code above, or use OPEN RECEIVER "
        + "ON TV once ADB is authorised.";

    /// <summary>Current result of media selection and handoff.</summary>
    public string MediaStatus { get; private set; } = "Choose a local file to play on the TV.";

    /// <summary>Current result of preparing the receiver for live mirroring.</summary>

    /// <summary>Raised by the view to open the native media picker.</summary>
    public event EventHandler? MediaFileSelectionRequested;

    /// <summary>The per-mode verdict cards.</summary>
    public ObservableCollection<ModeVerdictViewModel> Modes { get; } = [];

    /// <summary>
    /// The Mirror verdict, for the Screen page.
    /// </summary>
    /// <remarks>
    /// The Screen page's own control must read this rather than keep a separate opinion about
    /// whether mirroring works — a second, independently-maintained gate is exactly how a page
    /// ends up offering a button the underlying capability report already knows is empty.
    /// </remarks>
    public ModeVerdictViewModel? MirrorVerdict =>
        Modes.FirstOrDefault(mode => mode.Verdict.Mode == CastMode.Mirror);

    /// <summary>Whether a report exists to display.</summary>
    public bool HasReport => Report is not null;

    /// <summary>Whether to show the pre-probe empty state.</summary>
    public bool ShowEmptyState => Report is null && !IsProbing && Failure is null;

    /// <summary>The device name, or a placeholder when none was found.</summary>
    public string DeviceName => Report?.Device?.FriendlyName ?? "No receiver found";

    /// <summary>The device's platform, spelled out.</summary>
    public string DevicePlatform =>
        Report?.Device?.Platform.ToDisplayLabel() ?? "—";

    /// <summary>The device address and port, when known.</summary>
    public string DeviceAddress => Report?.Device is { } device
        ? device.AdbPort is { } port ? $"{device.Address}:{port}" : device.Address.ToString()
        : "—";

    /// <summary>The receiver model exactly as reported, without guessing from its name.</summary>
    public string DeviceModel => Report?.Device?.Model ?? "Not reported";

    /// <summary>The result of the ADB identity exchange.</summary>
    public string AdbStatus => Report?.Device?.AdbState.ToString() ?? "Not probed";

    /// <summary>The Android API level read from the receiver.</summary>
    public string AndroidApiLevel => Report?.Device?.AndroidApiLevel is { } level
        ? level.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "Not reported";

    /// <summary>The underlying Android release read from the receiver.</summary>
    public string AndroidRelease => Report?.Device?.AndroidRelease ?? "Not reported";

    /// <summary>The Windows build used for platform gates.</summary>
    public string WindowsBuild => Report is { } report
        ? report.Host.WindowsBuild.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "—";

    /// <summary>Whether the native engine actually answered the encoder query.</summary>
    public string EncoderProbeStatus => Report?.Host.EncodersProbed switch
    {
        true => "Complete",
        false => "Engine unavailable or incompatible",
        null => "Not probed",
    };

    /// <summary>DXGI adapters and their desktop role.</summary>
    public string GraphicsAdapters => Report is { Host.Adapters.Count: > 0 } report
        ? string.Join(
            Environment.NewLine,
            report.Host.Adapters.Select(adapter =>
                adapter.Description
                    + (adapter.IsSoftware ? " (software)" : adapter.DrivesDisplay ? " (display)" : string.Empty)))
        : "None reported";

    /// <summary>The adapter owning the Windows primary display.</summary>
    public string PrimaryDisplayAdapter => Report is { } report
        ? report.Host.Adapters
            .FirstOrDefault(adapter => adapter.Luid == report.Host.PrimaryDisplayAdapterLuid)
            ?.Description ?? "Not identified"
        : "—";

    /// <summary>Hardware encoders and the exact codec set each one advertised.</summary>
    public string HardwareEncoders => Report switch
    {
        null => "—",
        { Host.EncodersProbed: false } => "Not probed",
        { Host.Encoders.Count: 0 } => "None found",
        { } report => string.Join(
            Environment.NewLine,
            report.Host.Encoders.Select(encoder =>
                $"{encoder.Vendor}: {string.Join(", ", encoder.Codecs.Order())}")),
    };

    /// <summary>Measured round-trip time.</summary>
    public string RoundTrip => Report?.Path is { } path ? $"{path.RoundTripMs:F1} ms" : "Not measured";

    /// <summary>Measured round-trip variation.</summary>
    public string Jitter => Report?.Path is { } path ? $"{path.JitterMs:F1} ms" : "Not measured";

    /// <summary>Measured or explicitly unavailable throughput.</summary>
    public string Throughput => Report?.Path?.ThroughputLabel ?? "Not measured";

    /// <summary>Observed connect-sample loss.</summary>
    public string PacketLoss => Report?.Path is { } path ? $"{path.PacketLossPercent:F0}%" : "Not measured";

    /// <summary>The status word for the page heading pill.</summary>
    public string HeadingStatus => (IsProbing, Report) switch
    {
        (true, _) => "Probing",
        (false, null) => "Not probed",
        (false, { Device: { IsReachable: false } }) => "Connect TV",
        (false, not null) => Report!.HasAnyAvailableMode ? "Ready" : "Limited",
    };

    /// <summary>The heading pill's colour.</summary>
    public Tone HeadingTone => Report?.HasAnyAvailableMode == true ? Tone.Signal : Tone.Neutral;

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
            Failure = exception.Message;
            PairingStatus = "Flint could not open the receiver on the TV.";
        }
        finally
        {
            IsConnecting = false;
            RaiseDerived();
        }
    }

    /// <summary>Starts media selection when a live receiver session exists.</summary>
    [RelayCommand]
    private void ChooseMediaFile()
    {
        if (!IsSessionConnected)
        {
            Failure = "Enter the pairing code on Cast and connect the receiver first.";
            RaiseDerived();
            return;
        }

        MediaFileSelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// What to tell the user when the TV's own player could not reach this PC's media server.
    /// </summary>
    /// <remarks>
    /// Pairing dials out from this PC to the TV and generally works even on the restrictive
    /// "Public" network profile, because outbound connections are allowed by default. Serving a
    /// media file needs the reverse: the TV dials in to an ephemeral port on this PC, which
    /// Windows Firewall blocks by default for an application with no inbound rule. The two paths
    /// fail independently, so pairing succeeding proves nothing about whether this will work.
    /// </remarks>
    internal const string FirewallGuidance =
        "The TV could not reach this PC to fetch the file. This is almost always Windows Firewall "
        + "blocking the connection, not the TV or the network: allow Flint through Windows "
        + "Defender Firewall for Private and Public networks, then try again.";

    /// <summary>
    /// Turns the receiver's own playback failure into a message that names a cause, when the
    /// wording lets it.
    /// </summary>
    /// <remarks>
    /// The receiver reports Media3's error code name, lowercased with underscores turned to
    /// spaces — for example "error code io network connection timeout". That text describes what
    /// ExoPlayer saw, not what to do about it, so a network-shaped failure is translated into the
    /// same actionable guidance <see cref="FirewallGuidance"/> gives when this PC times out
    /// waiting instead. Anything else — a bad file, an unsupported codec, a permission error — is
    /// shown as the receiver reported it rather than guessed at.
    /// </remarks>
    internal static string DescribePlaybackFailure(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return "The receiver ended playback before it started.";
        }

        return detail.Contains("network connection", StringComparison.OrdinalIgnoreCase)
            ? FirewallGuidance
            : detail;
    }

    /// <summary>Hosts and sends the selected local file to the authenticated receiver.</summary>
    [RelayCommand]
    private async Task LoadMediaFileAsync(string path)
    {
        if (!IsSessionConnected || session is null)
        {
            Failure = "The receiver session ended. Connect it again before choosing media.";
            RaiseDerived();
            return;
        }

        try
        {
            if (coordinator is not null)
            {
                await coordinator.PrepareForAsync(TvSurfaceKind.Media).ConfigureAwait(true);
            }

            var mimeType = GetMimeType(path);
            var fileName = System.IO.Path.GetFileName(path);
            MediaStatus = $"Sending {fileName} to the TV.";
            OnPropertyChanged(nameof(MediaStatus));
            var progress = new Progress<double>(fraction =>
            {
                MediaStatus = $"Sending {fileName} to the TV ({(int)(fraction * 100)}%).";
                OnPropertyChanged(nameof(MediaStatus));
            });
            FlintDiag.Info("FlintCast", $"media push begin mime={mimeType} nameLen={fileName.Length}");
            using var playbackTimeout = new CancellationTokenSource(MediaStartTimeout);
            var playback = await session.PushMediaAndWaitForPlaybackStartAsync(
                path,
                fileName,
                mimeType,
                progress: progress,
                cancellationToken: playbackTimeout.Token).ConfigureAwait(true);

            if (playback.State is PlaybackState.Playing)
            {
                IsMediaPlaying = true;
                MediaStatus = $"Playing {fileName} on the TV.";
                Failure = null;
                FlintDiag.Info("FlintCast", "media playback started");
            }
            else
            {
                IsMediaPlaying = false;
                MediaStatus = "The TV did not start playback.";
                Failure = DescribePlaybackFailure(playback.Detail);
                FlintDiag.Warn("FlintCast", $"media playback not started state={playback.State}");
            }
        }
        catch (OperationCanceledException)
        {
            IsMediaPlaying = false;
            MediaStatus = "The TV did not confirm playback.";
            Failure = FirewallGuidance;
            FlintDiag.Warn("FlintCast", "media push timed out waiting for playback");
        }
        catch (Exception exception)
        {
            IsMediaPlaying = false;
            MediaStatus = "Media was not sent.";
            Failure = exception.Message;
            FlintDiag.Error("FlintCast", $"media push failed: {exception.GetType().Name}");
        }

        RaiseDerived();
    }

    /// <summary>Stops media playback on the TV and returns it toward idle.</summary>
    [RelayCommand(CanExecute = nameof(CanStopMedia))]
    private Task StopMediaAsync(CancellationToken cancellationToken) =>
        StopMediaCoreAsync(cancellationToken);

    /// <summary>Stops media if this PC started it; safe no-op otherwise.</summary>
    internal async Task StopMediaCoreAsync(CancellationToken cancellationToken = default)
    {
        if (!IsMediaPlaying)
        {
            return;
        }

        try
        {
            if (session is { IsConnected: true })
            {
                await session.SendMediaAsync(new MediaCommandMessage(MediaAction.Clear), cancellationToken)
                    .ConfigureAwait(true);
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            // Best-effort stop: the next exclusive start still proceeds.
        }
        finally
        {
            IsMediaPlaying = false;
            MediaStatus = "Playback stopped.";
            OnPropertyChanged(nameof(MediaStatus));
            OnPropertyChanged(nameof(CanStopMedia));
            StopMediaCommand.NotifyCanExecuteChanged();
        }
    }

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

        if (coordinator is not null)
        {
            await coordinator.PrepareForAsync(TvSurfaceKind.Mirror).ConfigureAwait(true);
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

    /// <summary>Clears saved direct addresses from both memory and persistent storage.</summary>
    [RelayCommand]
    private void ClearRecentAddresses()
    {
        addressStore.Clear();
        RecentAddresses.Clear();
    }

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4",
        ".mkv" => "video/x-matroska",
        ".webm" => "video/webm",
        ".mp3" => "audio/mpeg",
        ".m4a" => "audio/mp4",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };

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
        FlintDiag.Info("FlintCast", $"open receiver adb={device.AdbState} address={device.Address}");
        await receiverLauncher.LaunchAsync(device, cancellationToken).ConfigureAwait(true);
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

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(DeviceName));
        OnPropertyChanged(nameof(DevicePlatform));
        OnPropertyChanged(nameof(DeviceAddress));
        OnPropertyChanged(nameof(DeviceModel));
        OnPropertyChanged(nameof(AdbStatus));
        OnPropertyChanged(nameof(AndroidApiLevel));
        OnPropertyChanged(nameof(AndroidRelease));
        OnPropertyChanged(nameof(WindowsBuild));
        OnPropertyChanged(nameof(EncoderProbeStatus));
        OnPropertyChanged(nameof(GraphicsAdapters));
        OnPropertyChanged(nameof(PrimaryDisplayAdapter));
        OnPropertyChanged(nameof(HardwareEncoders));
        OnPropertyChanged(nameof(RoundTrip));
        OnPropertyChanged(nameof(Jitter));
        OnPropertyChanged(nameof(Throughput));
        OnPropertyChanged(nameof(PacketLoss));
        OnPropertyChanged(nameof(HeadingStatus));
        OnPropertyChanged(nameof(HeadingTone));
        OnPropertyChanged(nameof(SessionStatus));
        OnPropertyChanged(nameof(ReceiverReady));
        OnPropertyChanged(nameof(IsSessionConnected));
        OnPropertyChanged(nameof(CanOpenReceiver));
        OnPropertyChanged(nameof(CanPairWithCode));
        OnPropertyChanged(nameof(MirrorVerdict));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(PairingStatus));
        OnPropertyChanged(nameof(MediaStatus));
        OnPropertyChanged(nameof(CanStopMedia));
        OnPropertyChanged(nameof(CanStopMirror));
    }

}
