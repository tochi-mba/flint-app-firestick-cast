using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using Flint.App.Services;
using Flint.Core;
using Flint.Engine.Interop;
using Flint.Session;

namespace Flint.App.ViewModels;

/// <summary>
/// The Cast page and local receiver connection screen.
/// </summary>
/// <remarks>
/// This page answers one question - what can this PC and this television actually do together - and
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
    /// Covers the whole push - not just decode - because the file's bytes travel over this same
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
    private readonly IReceiverInstaller receiverInstaller;
    private readonly IBundledReceiverSource bundledReceiver;
    private CastSession? session;
    private CancellationTokenSource? mirrorStop;
    private TaskCompletionSource? mirrorStopped;
    private ModeSessionCoordinator? coordinator;
    private readonly TimeProvider time;

    /// <summary>The clock this page and everything built on it runs on.</summary>
    /// <remarks>One clock for the whole page, so a test that holds it still holds every timer still.</remarks>
    internal TimeProvider Time => time;

    public CastPageViewModel(
        CapabilityProber prober,
        IRecentAddressStore? addressStore = null,
        IReceiverLauncher? receiverLauncher = null,
        IMirrorEngine? mirrorEngine = null,
        IReceiverInstaller? receiverInstaller = null,
        IBundledReceiverSource? bundledReceiver = null,
        TimeProvider? time = null)
    {
        this.prober = prober ?? throw new ArgumentNullException(nameof(prober));
        this.addressStore = addressStore ?? new FileRecentAddressStore();
        this.receiverLauncher = receiverLauncher ?? new AdbReceiverLauncher();
        this.mirrorEngine = mirrorEngine ?? new NativeMirrorEngine();
        this.receiverInstaller = receiverInstaller ?? new AdbReceiverInstaller();
        this.bundledReceiver = bundledReceiver ?? BundledReceiverPackage.BesideTheApp();
        this.time = time ?? TimeProvider.System;
        NowPlaying = new NowPlayingViewModel(this, this.time);

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
    private bool isMediaPlaying;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopScreenSessionCommand))]
    private bool _isMirroring;

    [ObservableProperty]
    private string? _mirrorStatus;

    /// <summary>The saved direct endpoints, newest first.</summary>
    public ObservableCollection<RecentAddress> RecentAddresses { get; }

    /// <summary>Whether a live Flint receiver session is connected.</summary>
    public bool ReceiverReady => IsConnected || Report?.Device?.IsReachable == true;

    /// <summary>Whether an authenticated receiver session can accept media commands.</summary>
    public bool IsSessionConnected => IsConnected && session?.IsConnected == true;

    /// <summary>Whether the current receiver can be brought to the foreground over ADB.</summary>
    public bool CanOpenReceiver => Report?.Device?.AdbState is AdbConnectionState.Connected && !IsBusy;

    /// <summary>Whether discovery has identified a destination.</summary>
    public bool HasDevice => Report?.Device is not null;

    /// <summary>Show code entry only while a known TV is not connected.</summary>
    public bool ShowPairing => HasDevice && !IsConnected;

    /// <summary>
    /// Whether a complete pairing code can be submitted.
    /// </summary>
    /// <remarks>
    /// Pairing by code talks directly to the receiver's own port using the wire protocol's own
    /// authentication; it never touches ADB. Gating it on ADB, as <see cref="CanOpenReceiver"/>
    /// does, would defeat its purpose: it exists precisely as the path that still works when ADB
    /// is unauthorised, refused, or wedged, which is a routine and separate failure mode. All it
    /// needs is a known address and the complete code shown on the TV.
    /// </remarks>
    public bool CanPairWithCode
    {
        get
        {
            var code = PairingCode.Trim();
            return Report?.Device is not null
                && !IsBusy
                && code.Length == 6
                && code.All(char.IsDigit);
        }
    }

    partial void OnPairingCodeChanged(string value) => OnPropertyChanged(nameof(CanPairWithCode));

    /// <summary>Whether the shell is processing a probe, launch, or pairing action.</summary>
    public bool IsBusy => IsProbing || IsConnecting;

    /// <summary>Whether the receiver is connected or ready for a session.</summary>
    public string SessionStatus => IsConnected ? "Connected" : ReceiverReady ? "TV found" : "Not connected";

    /// <summary>Guidance and result text for the receiver-open and pairing sequence.</summary>
    public string PairingStatus { get; private set; } =
        "No code on the TV? Open the setup options below.";

    /// <summary>Current result of media selection and handoff.</summary>
    public string MediaStatus { get; private set; } = "Choose a local file to play on the TV.";

    /// <summary>Raised by the view to open the native media picker.</summary>
    public event EventHandler? MediaFileSelectionRequested;

    /// <summary>The per-mode verdict cards.</summary>
    public ObservableCollection<ModeVerdictViewModel> Modes { get; } = [];

    /// <summary>
    /// The Mirror verdict, for the Screen page.
    /// </summary>
    /// <remarks>
    /// The Screen page's own control must read this rather than keep a separate opinion about
    /// whether mirroring works - a second, independently-maintained gate is exactly how a page
    /// ends up offering a button the underlying capability report already knows is empty.
    /// </remarks>
    public ModeVerdictViewModel? MirrorVerdict =>
        Modes.FirstOrDefault(mode => mode.Verdict.Mode == CastMode.Mirror);

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(HasDevice));
        OnPropertyChanged(nameof(ShowPairing));
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
        OnPropertyChanged(nameof(CanRunReceiverAction));
        OnPropertyChanged(nameof(ReceiverSetupStatus));
        OnPropertyChanged(nameof(ReceiverReady));
        OnPropertyChanged(nameof(IsSessionConnected));
        OnPropertyChanged(nameof(CanOpenReceiver));
        OnPropertyChanged(nameof(CanPairWithCode));
        OnPropertyChanged(nameof(MirrorVerdict));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(PairingStatus));
        OnPropertyChanged(nameof(MediaStatus));
        OnPropertyChanged(nameof(CanStopMirror));
        OnPropertyChanged(nameof(CanDisconnect));
        DisconnectCommand.NotifyCanExecuteChanged();
    }
}
