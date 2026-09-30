using System.Net;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Core;
using Flint.Discovery;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// The receiver button does the right next thing — install, update or open — from what the
/// television reported and what this build carries, and never installs on a guess.
/// </summary>
public sealed class CastPageReceiverSetupTests
{
    private static readonly BundledReceiver Bundled =
        new(BundledReceiver.DebugPackage, "0.1.0-debug", 1118, 12 * 1024 * 1024);

    [Fact]
    public async Task Probe_WithAdbAuthorised_AsksWhatIsInstalledAndOffersToInstall()
    {
        var installer = new FakeReceiverInstaller { Installed = null };
        var page = Page(installer, new FakeBundledSource(Bundled));

        await page.ProbeCommand.ExecuteAsync(null);

        installer.CandidatesAsked.ShouldNotBeNull();
        installer.CandidatesAsked[0].ShouldBe(BundledReceiver.DebugPackage, "the bundled package is asked for first");
        installer.CandidatesAsked.ShouldContain(BundledReceiver.ReleasePackage);
        page.ReceiverAction.ShouldBe(ReceiverSetupAction.Install);
        page.ReceiverActionLabel.ShouldBe("INSTALL FLINT ON TV");
        page.CanRunReceiverAction.ShouldBeTrue();
        page.ReceiverSetupStatus.ShouldContain("Living Room");
        page.ReceiverSetupStatus.ShouldContain("12.0 MB");
    }

    [Fact]
    public async Task Probe_WithAdbRefused_NeverAsksAndKeepsThePlainOpenButton()
    {
        var installer = new FakeReceiverInstaller();
        var page = Page(installer, new FakeBundledSource(Bundled), Device() with { AdbState = AdbConnectionState.Refused });

        await page.ProbeCommand.ExecuteAsync(null);

        installer.CandidatesAsked.ShouldBeNull();
        page.ReceiverAction.ShouldBe(ReceiverSetupAction.Unknown);
        page.ReceiverActionLabel.ShouldBe("OPEN RECEIVER ON TV");
        page.CanRunReceiverAction.ShouldBeFalse();
    }

    [Fact]
    public async Task Probe_OlderBuildInstalled_OffersAnUpdate()
    {
        var installer = new FakeReceiverInstaller
        {
            Installed = new InstalledReceiver(BundledReceiver.DebugPackage, "0.1.0-debug", 1100),
        };
        var page = Page(installer, new FakeBundledSource(Bundled));

        await page.ProbeCommand.ExecuteAsync(null);

        page.ReceiverAction.ShouldBe(ReceiverSetupAction.Update);
        page.ReceiverActionLabel.ShouldBe("UPDATE FLINT ON TV");
        page.ReceiverSetupStatus.ShouldContain("update it in place");
    }

    [Fact]
    public async Task Probe_NewerBuildInstalled_OpensAndNeverDowngrades()
    {
        var installer = new FakeReceiverInstaller
        {
            Installed = new InstalledReceiver(BundledReceiver.DebugPackage, "0.2.0-debug", 1500),
        };
        var launcher = new FakeLauncher();
        var page = Page(installer, new FakeBundledSource(Bundled), launcher: launcher);
        await page.ProbeCommand.ExecuteAsync(null);
        page.ReceiverAction.ShouldBe(ReceiverSetupAction.OpenNewer);

        await page.RunReceiverActionCommand.ExecuteAsync(null);

        installer.InstalledBytes.ShouldBeNull();
        launcher.LaunchedPackage.ShouldBe(BundledReceiver.DebugPackage);
        page.ReceiverSetupStatus.ShouldContain("will not downgrade");
    }

    [Fact]
    public async Task Probe_NothingBundledAndNothingInstalled_DisablesTheButtonAndSaysWhy()
    {
        var page = Page(new FakeReceiverInstaller { Installed = null }, new FakeBundledSource(null));

        await page.ProbeCommand.ExecuteAsync(null);

        page.ReceiverAction.ShouldBe(ReceiverSetupAction.NothingBundled);
        page.CanRunReceiverAction.ShouldBeFalse();
        page.ReceiverSetupStatus.ShouldContain("carries no receiver");
    }

    [Fact]
    public async Task Probe_IdentifyFails_FallsBackToOpenRatherThanBlockingTheButton()
    {
        var installer = new FakeReceiverInstaller { FindFailure = new IOException("the exchange was lost") };
        var page = Page(installer, new FakeBundledSource(Bundled));

        await page.ProbeCommand.ExecuteAsync(null);

        page.Failure.ShouldBeNull();
        page.ReceiverAction.ShouldBe(ReceiverSetupAction.Unknown);
        page.CanRunReceiverAction.ShouldBeTrue();
    }

    [Fact]
    public async Task RunReceiverAction_Install_StreamsTheBundledPackageVerifiesItThenOpensIt()
    {
        var apk = new byte[] { 1, 2, 3, 4 };
        var installer = new FakeReceiverInstaller
        {
            Installed = null,
            AfterInstall = new InstalledReceiver(BundledReceiver.DebugPackage, "0.1.0-debug", 1118),
        };
        var launcher = new FakeLauncher();
        var page = Page(installer, new FakeBundledSource(Bundled, apk), launcher: launcher);
        await page.ProbeCommand.ExecuteAsync(null);

        await page.RunReceiverActionCommand.ExecuteAsync(null);

        installer.InstalledBytes.ShouldBe(apk);
        installer.InstalledOn.ShouldNotBeNull();
        installer.InstalledOn.Address.ShouldBe(IPAddress.Parse("192.168.1.42"));
        launcher.LaunchedPackage.ShouldBe(BundledReceiver.DebugPackage);
        page.Failure.ShouldBeNull();
        page.ReceiverAction.ShouldBe(ReceiverSetupAction.Open);
        page.ReceiverActionLabel.ShouldBe("OPEN RECEIVER ON TV");
        page.PairingStatus.ShouldContain("0.1.0-debug is on the TV and open");
        page.InstallProgress.ShouldBeEmpty();
        page.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task RunReceiverAction_TvRefuses_RelaysTheRefusalAndDoesNotOpen()
    {
        var installer = new FakeReceiverInstaller
        {
            Installed = null,
            Outcome = new AdbInstallOutcome(false, "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE]"),
        };
        var launcher = new FakeLauncher();
        var page = Page(installer, new FakeBundledSource(Bundled), launcher: launcher);
        await page.ProbeCommand.ExecuteAsync(null);

        await page.RunReceiverActionCommand.ExecuteAsync(null);

        launcher.LaunchedPackage.ShouldBeNull();
        page.Failure.ShouldNotBeNull();
        page.Failure.ShouldContain("INSTALL_FAILED_UPDATE_INCOMPATIBLE");
        page.ReceiverAction.ShouldBe(ReceiverSetupAction.Install, "the button still offers the install");
    }

    [Fact]
    public async Task RunReceiverAction_SuccessButPackageMissingAfterwards_IsReportedNotTrusted()
    {
        var installer = new FakeReceiverInstaller { Installed = null, AfterInstall = null };
        var launcher = new FakeLauncher();
        var page = Page(installer, new FakeBundledSource(Bundled), launcher: launcher);
        await page.ProbeCommand.ExecuteAsync(null);

        await page.RunReceiverActionCommand.ExecuteAsync(null);

        launcher.LaunchedPackage.ShouldBeNull();
        page.Failure.ShouldNotBeNull();
        page.Failure.ShouldContain("not on the TV afterwards");
    }

    [Fact]
    public async Task RunReceiverAction_InstallThrows_LeavesAMessageAndAnIdlePage()
    {
        var installer = new FakeReceiverInstaller { Installed = null, InstallFailure = new IOException("lost the TV") };
        var page = Page(installer, new FakeBundledSource(Bundled));
        await page.ProbeCommand.ExecuteAsync(null);

        await page.RunReceiverActionCommand.ExecuteAsync(null);

        page.Failure.ShouldBe("lost the TV");
        page.IsBusy.ShouldBeFalse();
        page.PairingStatus.ShouldContain("could not install");
    }

    [Fact]
    public async Task RunReceiverAction_BeforeAnyProbe_AsksForTheTvFirst()
    {
        var page = Page(new FakeReceiverInstaller(), new FakeBundledSource(Bundled));

        await page.RunReceiverActionCommand.ExecuteAsync(null);

        page.Failure.ShouldBe("Connect to the TV first.");
    }

    [Fact]
    public async Task OpenReceiver_UsesThePackageTheTvReportedRatherThanAFixedName()
    {
        var installer = new FakeReceiverInstaller
        {
            Installed = new InstalledReceiver(BundledReceiver.ReleasePackage, "0.1.0", 2000),
        };
        var launcher = new FakeLauncher();
        var page = Page(installer, new FakeBundledSource(Bundled), launcher: launcher);
        await page.ProbeCommand.ExecuteAsync(null);
        page.ReceiverAction.ShouldBe(ReceiverSetupAction.OpenDifferentPackage);

        await page.OpenReceiverCommand.ExecuteAsync(null);

        launcher.LaunchedPackage.ShouldBe(BundledReceiver.ReleasePackage);
    }

    [Fact]
    public async Task ANewProbe_ForgetsWhatTheLastTvHad()
    {
        var installer = new FakeReceiverInstaller
        {
            Installed = new InstalledReceiver(BundledReceiver.DebugPackage, "0.1.0-debug", 1118),
        };
        var page = Page(installer, new FakeBundledSource(Bundled));
        await page.ProbeCommand.ExecuteAsync(null);
        page.InstalledReceiver.ShouldNotBeNull();

        installer.Installed = null;
        await page.ProbeCommand.ExecuteAsync(null);

        page.InstalledReceiver.ShouldBeNull();
        page.ReceiverAction.ShouldBe(ReceiverSetupAction.Install);
    }

    private static FireTvDevice Device() =>
        new(IPAddress.Parse("192.168.1.42"), "Living Room", DiscoverySource.MulticastDns)
        {
            Platform = FireTvPlatform.FireOs8,
            AdbState = AdbConnectionState.Connected,
            AdbPort = 5555,
        };

    private static CastPageViewModel Page(
        IReceiverInstaller installer,
        IBundledReceiverSource bundled,
        FireTvDevice? device = null,
        IReceiverLauncher? launcher = null) =>
        new(
            new CapabilityProber(new FakeHostProbe(), new FakeDeviceProbe(device ?? Device()), new FakeNetworkProbe()),
            new EmptyRecentAddressStore(),
            launcher ?? new FakeLauncher(),
            receiverInstaller: installer,
            bundledReceiver: bundled);

    private sealed class FakeReceiverInstaller : IReceiverInstaller
    {
        public InstalledReceiver? Installed { get; set; }

        public InstalledReceiver? AfterInstall { get; set; }

        public AdbInstallOutcome Outcome { get; set; } = new(true, "Success");

        public Exception? FindFailure { get; set; }

        public Exception? InstallFailure { get; set; }

        public IReadOnlyList<string>? CandidatesAsked { get; private set; }

        public byte[]? InstalledBytes { get; private set; }

        public FireTvDevice? InstalledOn { get; private set; }

        public Task<InstalledReceiver?> FindInstalledAsync(
            FireTvDevice device,
            IReadOnlyList<string> candidates,
            CancellationToken cancellationToken = default)
        {
            if (FindFailure is not null)
            {
                throw FindFailure;
            }

            if (InstalledBytes is not null)
            {
                return Task.FromResult(AfterInstall);
            }

            CandidatesAsked = candidates;
            return Task.FromResult(Installed);
        }

        public Task<AdbInstallOutcome> InstallAsync(
            FireTvDevice device,
            ReadOnlyMemory<byte> apk,
            IProgress<double>? progress,
            CancellationToken cancellationToken = default)
        {
            if (InstallFailure is not null)
            {
                throw InstallFailure;
            }

            InstalledBytes = apk.ToArray();
            InstalledOn = device;
            progress?.Report(1.0);
            return Task.FromResult(Outcome);
        }
    }

    private sealed class FakeBundledSource(BundledReceiver? bundled, byte[]? apk = null) : IBundledReceiverSource
    {
        public BundledReceiver? Describe() => bundled;

        public Task<byte[]> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(apk ?? new byte[] { 9 });
    }

    private sealed class FakeLauncher : IReceiverLauncher
    {
        public string? LaunchedPackage { get; private set; }

        public Task LaunchAsync(FireTvDevice device, string packageName, CancellationToken cancellationToken = default)
        {
            LaunchedPackage = packageName;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHostProbe : IHostProbe
    {
        public Task<HostCapabilities> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HostCapabilities(
                [new DisplayAdapter(1, "Test Adapter", DrivesDisplay: true)],
                [new HostVideoEncoder(EncoderVendor.Nvenc, 1, new HashSet<VideoCodec> { VideoCodec.H264 })],
                1,
                26100,
                EncodersProbed: true,
                ScreenCaptureBackend: CaptureApi.DesktopDuplication));
    }

    private sealed class FakeDeviceProbe(FireTvDevice device) : IAddressableDeviceProbe
    {
        public Task<IReadOnlyList<FireTvDevice>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FireTvDevice>>([device]);

        public Task<FireTvDevice> ProbeAddressAsync(IPAddress address, int? port = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(device);
    }

    private sealed class FakeNetworkProbe : INetworkProbe
    {
        public Task<NetworkPath?> MeasureAsync(FireTvDevice device, CancellationToken cancellationToken = default) =>
            Task.FromResult<NetworkPath?>(new NetworkPath(4.0, 1.0, 120.0, 0.0));
    }

    private sealed class EmptyRecentAddressStore : IRecentAddressStore
    {
        public IReadOnlyList<RecentAddress> Load() => [];

        public void Remember(RecentAddress address)
        {
        }

        public void Clear()
        {
        }
    }
}
