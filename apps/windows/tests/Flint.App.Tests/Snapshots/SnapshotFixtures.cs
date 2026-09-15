using System.Net;
using Flint.App.ViewModels;
using Flint.Core;

namespace Flint.App.Tests.Snapshots;

/// <summary>
/// Fixed, fully-determined state for the pages under snapshot.
/// </summary>
/// <remarks>
/// <para>
/// Every value here is a constant, and that is the point. A screenshot test compares pixels, so
/// anything that varies between runs — a discovered device, a measured round-trip time, a clock —
/// becomes a permanent false failure, and the usual response is to widen the threshold until the
/// test cannot fail at all.
/// </para>
/// <para>
/// So the probes are fakes returning fixed answers, and the address store is empty rather than the
/// real one: <see cref="CastPageViewModel"/>'s default constructor reads persisted history from
/// this machine's <c>%LOCALAPPDATA%</c>, and a leftover address from real use would change what the
/// page renders depending on whose machine ran the tests.
/// </para>
/// </remarks>
public static class SnapshotFixtures
{
    /// <summary>A Fire TV that supports everything, at a fixed address.</summary>
    public static FireTvDevice CapableDevice() =>
        new(IPAddress.Parse("192.168.1.42"), "Living Room", DiscoverySource.MulticastDns)
        {
            Platform = FireTvPlatform.FireOs8,
            AdbState = AdbConnectionState.Connected,
            AdbPort = 5555,
        };

    /// <summary>A Vega OS device, which cannot run a sideloaded receiver at all.</summary>
    public static FireTvDevice VegaDevice() =>
        new(IPAddress.Parse("192.168.1.51"), "Bedroom", DiscoverySource.MulticastDns)
        {
            Platform = FireTvPlatform.Vega,
            AdbState = AdbConnectionState.Refused,
        };

    /// <summary>A PC with a hardware encoder on the display adapter.</summary>
    public static HostCapabilities CapableHost() =>
        new(
            [new DisplayAdapter(1, "NVIDIA GeForce RTX 4070 Laptop GPU", DrivesDisplay: true)],
            [
                new HostVideoEncoder(
                    EncoderVendor.Nvenc,
                    1,
                    new HashSet<VideoCodec> { VideoCodec.H264, VideoCodec.H265 })
            ],
            1,
            26100,
            EncodersProbed: true,
            ScreenCaptureBackend: CaptureApi.DesktopDuplication);

    /// <summary>A PC with no usable hardware encoder, which blocks mirroring.</summary>
    public static HostCapabilities HostThatCannotMirror() =>
        CapableHost() with
        {
            Encoders = [new HostVideoEncoder(EncoderVendor.Unknown, 0, new HashSet<VideoCodec>())],
        };

    /// <summary>A healthy local network path.</summary>
    public static NetworkPath GoodPath() => new(4.0, 1.0, 120.0, 0.0);

    /// <summary>A congested path, which the diagnostics page reports on.</summary>
    public static NetworkPath PoorPath() => new(48.0, 22.0, 9.0, 4.5);

    /// <summary>A capability prober wired to fakes, answering the same way every run.</summary>
    public static CapabilityProber Prober(
        FireTvDevice? device = null,
        HostCapabilities? host = null,
        NetworkPath? path = null) =>
        new(
            new FixedHostProbe(host ?? CapableHost()),
            new FixedDeviceProbe(device),
            new FixedNetworkProbe(path));

    /// <summary>
    /// A cast view model wired to fakes, with nothing probed yet.
    /// </summary>
    public static CastPageViewModel ViewModel(
        FireTvDevice? device = null,
        HostCapabilities? host = null,
        NetworkPath? path = null) =>
        new(Prober(device, host, path), new EmptyRecentAddressStore());

    /// <summary>
    /// The whole shell, with the introduction already seen so the pages are visible.
    /// </summary>
    public static MainWindowViewModel Shell(
        FireTvDevice? device = null,
        HostCapabilities? host = null,
        NetworkPath? path = null) =>
        MainWindowViewModel.CreateWith(
            Prober(device, host, path),
            new SeenOnboardingState(),
            new EmptyRecentAddressStore());

    /// <summary>The introduction, on its first step.</summary>
    public static OnboardingViewModel Onboarding() => new(new UnseenOnboardingState());

    /// <summary>
    /// A cast view model that has already probed, so the page renders its populated state.
    /// </summary>
    public static async Task<CastPageViewModel> ProbedViewModel(
        FireTvDevice? device = null,
        HostCapabilities? host = null,
        NetworkPath? path = null)
    {
        var viewModel = ViewModel(
            device ?? CapableDevice(),
            host ?? CapableHost(),
            path ?? GoodPath());
        await viewModel.ProbeCommand.ExecuteAsync(null);
        return viewModel;
    }

    private sealed class FixedHostProbe(HostCapabilities host) : IHostProbe
    {
        public Task<HostCapabilities> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(host);
    }

    private sealed class FixedDeviceProbe(FireTvDevice? device) : IAddressableDeviceProbe
    {
        public Task<IReadOnlyList<FireTvDevice>> DiscoverAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FireTvDevice>>(device is null ? [] : [device]);

        public Task<FireTvDevice> ProbeAddressAsync(
            IPAddress address,
            int? port = null,
            CancellationToken cancellationToken = default) =>
            // Never reached by the snapshots: they all discover rather than probe an address, and
            // the empty address store makes sure of it. Throwing beats returning a device, which
            // would let a page take the direct-address path without any test noticing.
            throw new NotSupportedException("snapshots do not probe a direct address");
    }

    private sealed class FixedNetworkProbe(NetworkPath? path) : INetworkProbe
    {
        public Task<NetworkPath?> MeasureAsync(
            FireTvDevice device,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(path);
    }

    private sealed class SeenOnboardingState : IOnboardingState
    {
        public bool HasCompleted => true;

        public void MarkCompleted()
        {
            // Snapshots must not write to this machine's real state.
        }

        public void Reset()
        {
            // As above.
        }
    }

    private sealed class UnseenOnboardingState : IOnboardingState
    {
        public bool HasCompleted => false;

        public void MarkCompleted()
        {
            // As above.
        }

        public void Reset()
        {
            // As above.
        }
    }

    private sealed class EmptyRecentAddressStore : IRecentAddressStore
    {
        public IReadOnlyList<RecentAddress> Load() => [];

        public void Remember(RecentAddress address)
        {
            // Snapshots must not write to this machine's real history.
        }

        public void Clear()
        {
            // Nothing is stored, so there is nothing to clear.
        }
    }
}
