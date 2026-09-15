using System.Net;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Core;
using Flint.Discovery;
using Flint.Protocol;
using Flint.Session.Browser;

namespace Flint.App.Tests;

/// <summary>
/// Shared setup for the browser page's tests.
/// </summary>
/// <remarks>
/// Reaching a verified session takes a probed device, an advertised endpoint, a pairing code and a
/// first-use acceptance, and every test that exercises anything past that point needs all four.
/// Written once here so a change to the verification flow updates one place rather than each test
/// class that happens to depend on it.
/// </remarks>
public static class BrowserFixtures
{
    /// <summary>A Fire TV that advertises a browser endpoint and passes the WebView probe.</summary>
    public static FireTvDevice EligibleDevice() =>
        new(IPAddress.Parse("192.168.1.42"), "Living Room", DiscoverySource.MulticastDns)
        {
            Platform = FireTvPlatform.FireOs8,
            AdbState = AdbConnectionState.Connected,
            AdbPort = 5555,
            BrowserEvidence = new BrowserReceiverEvidence(2, true, BrowserWebViewProbe.Passed)
            {
                SecureEndpointPort = 8443,
            },
        };

    /// <summary>A browser page with no session, as the app opens it.</summary>
    public static BrowserPageViewModel ViewModel(IBrowserSessionConnector? connector = null)
    {
        var shell = MainWindowViewModel.CreateWith(Prober(EligibleDevice()));
        return new BrowserPageViewModel(
            shell.Cast,
            connector ?? new RecordingBrowserSessionConnector(new RecordingBrowserRemote()),
            new InMemoryBrowserTrustStore(),
            new ImmediateDispatcher(),
            profileLibraryStore: new InMemoryBrowserProfileLibraryStore(),
            help: new BrowserHelpViewModel(false));
    }

    /// <summary>A browser page that has probed, verified and reached a usable session.</summary>
    public static async Task<BrowserPageViewModel> ReadyViewModelAsync(RecordingBrowserRemote remote)
    {
        var shell = MainWindowViewModel.CreateWith(Prober(EligibleDevice()));
        await shell.Cast.ProbeCommand.ExecuteAsync(null).ConfigureAwait(false);
        shell.Cast.PairingCode = "123456";

        var viewModel = new BrowserPageViewModel(
            shell.Cast,
            new RecordingBrowserSessionConnector(remote),
            new InMemoryBrowserTrustStore(),
            new ImmediateDispatcher(),
            profileLibraryStore: new InMemoryBrowserProfileLibraryStore(),
            help: new BrowserHelpViewModel(false));
        viewModel.Address = "https://example.test/";
        await viewModel.VerifySecureReceiverCommand.ExecuteAsync(null).ConfigureAwait(false);
        return viewModel;
    }

    /// <summary>Runs dispatched UI work inline so unit tests can assert after PublishState.</summary>
    public sealed class ImmediateDispatcher : IBrowserUiDispatcher
    {
        public void Dispatch(Action action) => action();
    }

    /// <summary>A prober that answers with one fixed device and a healthy host.</summary>
    public static CapabilityProber Prober(FireTvDevice device) =>
        new(new FixedHostProbe(), new FixedDeviceProbe(device), new FixedNetworkProbe());

    /// <summary>An identity with a stable, readable fingerprint.</summary>
    public static BrowserPeerIdentity Identity(BrowserEndpoint endpoint)
    {
        var fingerprint = BrowserFingerprint.ParseFullPin(
            "sha256/" + Convert.ToBase64String(Enumerable.Range(0, 32).Select(index => (byte)index).ToArray()));
        return new BrowserPeerIdentity(endpoint, fingerprint);
    }

    private sealed class FixedHostProbe : IHostProbe
    {
        public Task<HostCapabilities> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HostCapabilities(
                [new DisplayAdapter(1, "Test GPU", DrivesDisplay: true)],
                [new HostVideoEncoder(EncoderVendor.Nvenc, 1, new HashSet<VideoCodec> { VideoCodec.H264 })],
                1,
                26100,
                EncodersProbed: true,
                ScreenCaptureBackend: CaptureApi.DesktopDuplication));
    }

    private sealed class FixedDeviceProbe(FireTvDevice device) : IDeviceProbe
    {
        public Task<IReadOnlyList<FireTvDevice>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FireTvDevice>>([device]);
    }

    private sealed class FixedNetworkProbe : INetworkProbe
    {
        public Task<NetworkPath?> MeasureAsync(FireTvDevice device, CancellationToken cancellationToken = default) =>
            Task.FromResult<NetworkPath?>(new NetworkPath(4.0, 1.0, 120.0, 0.0));
    }
}

/// <summary>A connector that always hands back one prepared remote.</summary>
public sealed class RecordingBrowserSessionConnector(RecordingBrowserRemote remote) : IBrowserSessionConnector
{
    /// <summary>How many times a session was established.</summary>
    public int ConnectCount { get; private set; }

    /// <inheritdoc />
    public async Task<ISecureBrowserRemote> ConnectAsync(
        BrowserEndpoint endpoint,
        string pairingCode,
        IBrowserTrustStore trustStore,
        IBrowserTrustPrompter trustPrompter,
        CancellationToken cancellationToken = default)
    {
        ConnectCount++;
        var identity = BrowserFixtures.Identity(endpoint);
        var remembered = await trustStore.FindAsync(endpoint.ReceiverIdentity, cancellationToken)
            .ConfigureAwait(false);
        if (remembered is null)
        {
            var accepted = await trustPrompter.ConfirmFirstUseAsync(identity, cancellationToken)
                .ConfigureAwait(false);
            if (!accepted)
            {
                throw new BrowserTrustException("The user rejected the first-use security code.");
            }

            await trustStore.SaveAsync(
                    new BrowserTrustedReceiver(
                        endpoint.ReceiverIdentity,
                        identity.Fingerprint,
                        DateTimeOffset.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        remote.Identity = identity;
        remote.ResetForReconnect();
        return remote;
    }
}

/// <summary>
/// A secure remote that records what the shell sent instead of talking to a television.
/// </summary>
/// <remarks>
/// Records rather than asserts, so each test states its own expectation. The ordering guarantees the
/// receiver depends on — a strictly advancing sequence, a matching epoch — are only observable from
/// the outside as the series of messages that arrived, which is exactly what this keeps.
/// </remarks>
public sealed class RecordingBrowserRemote : ISecureBrowserRemote
{
    /// <summary>Commands the shell sent, in order.</summary>
    public List<BrowserCommandMessage> Commands { get; } = [];

    /// <summary>Inputs the shell sent, in order.</summary>
    public List<BrowserInputMessage> Inputs { get; } = [];

    /// <summary>Dialog replies the shell sent, in order.</summary>
    public List<BrowserDialogReplyMessage> DialogReplies { get; } = [];

    /// <summary>The cockpit channel handed to the shell, once it has asked for one.</summary>
    public RecordingCockpitRemote? Cockpit { get; private set; }

    /// <summary>Whether the TLS session can still send commands.</summary>
    public bool IsConnected { get; set; } = true;

    /// <summary>Makes every send fail, standing in for a dropped television.</summary>
    public bool ThrowOnSend { get; set; }

    private TaskCompletionSource completionSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public Task Completion
    {
        get => completionSource.Task;
        set
        {
            // Tests that inject a custom completion replace the owned source.
            completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            value.ContinueWith(
                t =>
                {
                    if (t.IsFaulted)
                    {
                        completionSource.TrySetException(t.Exception!.InnerExceptions);
                    }
                    else if (t.IsCanceled)
                    {
                        completionSource.TrySetCanceled();
                    }
                    else
                    {
                        completionSource.TrySetResult();
                    }
                },
                TaskScheduler.Default);
        }
    }

    /// <summary>Ends the session the way a dropped TLS receive loop does.</summary>
    public void EndSession()
    {
        IsConnected = false;
        completionSource.TrySetResult();
    }

    /// <summary>Prepares this remote to be handed out again after a reconnect.</summary>
    public void ResetForReconnect()
    {
        IsConnected = true;
        completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Commands.Clear();
        Inputs.Clear();
        DialogReplies.Clear();
    }

    /// <summary>The accepted peer identity.</summary>
    public BrowserPeerIdentity Identity { get; set; } =
        BrowserFixtures.Identity(new BrowserEndpoint(IPAddress.Parse("192.168.1.42"), 8443, "living-room"));

    /// <inheritdoc />
    public BrowserPeerIdentity PeerIdentity => Identity;

    /// <inheritdoc />
    public BrowserCapabilityMessage Capability { get; set; } = new(
        BrowserCapabilityStatus.Available,
        SecureEndpointPort: 8443,
        ApiLevel: 30,
        WebViewVersion: "test",
        PreviewSupported: true,
        PreviewMaxWidth: 960,
        PreviewMaxHeight: 540,
        InteractivePreviewFramesPerSecond: 5,
        IdlePreviewFramesPerSecond: 1,
        PreviewMaxBytes: BrowserWireLimits.MaxPreviewBytes);

    /// <inheritdoc />
    public event Action<BrowserStateMessage>? StateReceived;

    /// <inheritdoc />
    public event Action<BrowserPreviewMessage>? PreviewReceived;

    /// <inheritdoc />
    public event Action<BrowserDialogMessage>? DialogReceived;

    /// <summary>Raises a receiver state update, as a television would.</summary>
    public void PublishState(BrowserStateMessage state) => StateReceived?.Invoke(state);

    /// <summary>Raises an opt-in preview frame, as a television would.</summary>
    public void PublishPreview(BrowserPreviewMessage preview) => PreviewReceived?.Invoke(preview);

    /// <summary>Raises a native page dialog, as a television would.</summary>
    public void PublishDialog(BrowserDialogMessage dialog) => DialogReceived?.Invoke(dialog);

    /// <inheritdoc />
    public Task SendCommandAsync(BrowserCommandMessage command, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSend)
        {
            return Task.FromException(new IOException("the session dropped"));
        }

        Commands.Add(command);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendInputAsync(BrowserInputMessage input, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSend)
        {
            return Task.FromException(new IOException("the session dropped"));
        }

        Inputs.Add(input);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendDialogReplyAsync(
        BrowserDialogReplyMessage reply,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnSend)
        {
            return Task.FromException(new IOException("the session dropped"));
        }

        DialogReplies.Add(reply);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IBrowserCockpitRemote? CreateCockpit(Func<long> epoch, Func<long> nextCommandId)
    {
        Cockpit ??= new RecordingCockpitRemote(epoch, nextCommandId);
        return Cockpit;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// A cockpit channel that records tab, view and library traffic instead of reaching a television.
/// </summary>
/// <remarks>
/// Families start unavailable and are turned on by <see cref="Announce"/>, which is how a real
/// receiver reveals them: by sending a snapshot. A test that wants to drive a cockpit control has to
/// say the receiver supports it first, which is the same order the product enforces.
/// </remarks>
public sealed class RecordingCockpitRemote : IBrowserCockpitRemote, IBrowserWorkspaceCommandSink
{
    private readonly Func<long> epoch;
    private readonly Func<long> nextCommandId;

    /// <summary>Creates a recording cockpit; epoch helpers mirror the production binding.</summary>
    public RecordingCockpitRemote(Func<long>? epoch = null, Func<long>? nextCommandId = null)
    {
        this.epoch = epoch ?? (() => 1);
        this.nextCommandId = nextCommandId ?? (() => 1);
    }

    /// <summary>Tab operations the cockpit sent, in order.</summary>
    public List<BrowserTabRequest> TabRequests { get; } = [];

    /// <summary>Epoch captured with the last accepted tab send, or null when none were sent.</summary>
    public long? LastTabEpoch { get; private set; }

    /// <summary>View and find operations the cockpit sent, in order.</summary>
    public List<BrowserViewRequest> ViewRequests { get; } = [];

    /// <summary>Library operations the cockpit sent, in order.</summary>
    public List<BrowserLibraryRequest> LibraryRequests { get; } = [];

    /// <summary>Windows-device library projections sent to the television.</summary>
    public List<BrowserLibrarySnapshot> DeviceLibrarySnapshots { get; } = [];

    /// <summary>Profile selections sent to the television.</summary>
    public List<BrowserProfileRequest> ProfileRequests { get; } = [];

    /// <summary>Network/VPN commands sent to the television.</summary>
    public List<BrowserNetworkRequest> NetworkRequests { get; } = [];

    /// <inheritdoc />
    public BrowserCockpitFeatures CockpitFeatures { get; private set; } = BrowserCockpitFeatures.None;

    /// <inheritdoc />
    public event Action<BrowserTabsSnapshot>? TabsReceived;

    /// <inheritdoc />
    public event Action<BrowserViewSnapshot>? ViewReceived;

    /// <inheritdoc />
    public event Action<BrowserLibrarySnapshot>? LibraryReceived;

    /// <inheritdoc />
    public event Action<BrowserLibraryRequest>? DeviceLibraryRequestReceived;

    /// <inheritdoc />
    public event Action<BrowserProfilesSnapshot>? ProfilesReceived;

    /// <inheritdoc />
    public event Action<BrowserNetworkSnapshot>? NetworkReceived;

    /// <inheritdoc />
    public event Action<BrowserWorkspaceSnapshot>? WorkspaceReceived;

    /// <summary>Semantic workspace commands the cockpit sent, in order.</summary>
    public List<BrowserWorkspaceCommand> WorkspaceCommands { get; } = [];

    public IBrowserWorkspaceCommandSink WorkspaceCommandSink => this;

    public Task SendAsync(BrowserWorkspaceCommand command, CancellationToken cancellationToken = default)
    {
        WorkspaceCommands.Add(command);
        return Task.CompletedTask;
    }

    /// <summary>Marks a family supported, as a receiver does by sending its first snapshot.</summary>
    public void Announce(BrowserCockpitFeatures features) => CockpitFeatures |= features;

    /// <summary>Publishes a tab snapshot as the receiver would.</summary>
    public void PublishTabs(BrowserTabsSnapshot snapshot)
    {
        Announce(BrowserCockpitFeatures.Tabs);
        TabsReceived?.Invoke(snapshot);
    }

    /// <summary>Publishes a view snapshot as the receiver would.</summary>
    public void PublishView(BrowserViewSnapshot snapshot)
    {
        Announce(BrowserCockpitFeatures.View);
        ViewReceived?.Invoke(snapshot);
    }

    /// <summary>Publishes a library snapshot as the receiver would.</summary>
    public void PublishLibrary(BrowserLibrarySnapshot snapshot)
    {
        Announce(BrowserCockpitFeatures.Library);
        LibraryReceived?.Invoke(snapshot);
    }

    /// <summary>Publishes the TV profile catalog and active storage owner.</summary>
    public void PublishProfiles(BrowserProfilesSnapshot snapshot)
    {
        Announce(BrowserCockpitFeatures.Profiles);
        ProfilesReceived?.Invoke(snapshot);
    }

    /// <summary>Publishes a network/VPN snapshot as the receiver would.</summary>
    public void PublishNetwork(BrowserNetworkSnapshot snapshot)
    {
        Announce(BrowserCockpitFeatures.Network);
        NetworkReceived?.Invoke(snapshot);
    }

    /// <summary>Publishes a workspace snapshot as the receiver would.</summary>
    public void PublishWorkspace(BrowserWorkspaceSnapshot snapshot)
    {
        Announce(BrowserCockpitFeatures.Workspace);
        WorkspaceReceived?.Invoke(snapshot);
    }

    /// <summary>Asks the active Windows-device profile to mutate or publish its snapshot.</summary>
    public void RequestDeviceLibrary(BrowserLibraryRequest request)
    {
        Announce(BrowserCockpitFeatures.Library);
        DeviceLibraryRequestReceived?.Invoke(request);
    }

    /// <inheritdoc />
    public Task SendTabCommandAsync(BrowserTabRequest request, CancellationToken cancellationToken = default)
    {
        if (!CockpitFeatures.HasFlag(BrowserCockpitFeatures.Tabs))
        {
            return Task.CompletedTask;
        }

        var currentEpoch = epoch();
        if (currentEpoch <= 0)
        {
            return Task.CompletedTask;
        }

        LastTabEpoch = currentEpoch;
        _ = nextCommandId();
        TabRequests.Add(request);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendViewCommandAsync(BrowserViewRequest request, CancellationToken cancellationToken = default)
    {
        if (CockpitFeatures.HasFlag(BrowserCockpitFeatures.View))
        {
            ViewRequests.Add(request);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendLibraryCommandAsync(
        BrowserLibraryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (CockpitFeatures.HasFlag(BrowserCockpitFeatures.Library))
        {
            LibraryRequests.Add(request);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendLibraryStateAsync(
        BrowserLibrarySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (CockpitFeatures.HasFlag(BrowserCockpitFeatures.Profiles))
        {
            DeviceLibrarySnapshots.Add(snapshot);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendProfileCommandAsync(
        BrowserProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (CockpitFeatures.HasFlag(BrowserCockpitFeatures.Profiles))
        {
            ProfileRequests.Add(request);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SendNetworkCommandAsync(
        BrowserNetworkRequest request,
        CancellationToken cancellationToken = default)
    {
        if (CockpitFeatures.HasFlag(BrowserCockpitFeatures.Network))
        {
            NetworkRequests.Add(request);
        }

        return Task.CompletedTask;
    }
}
