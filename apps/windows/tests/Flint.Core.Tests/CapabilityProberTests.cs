using System.Net;
using Flint.Core.Tests.TestData;
using Shouldly;

namespace Flint.Core.Tests;

/// <summary>
/// The prober only composes; the judgement lives in <see cref="CapabilityAssessor"/>. What is worth
/// testing here is which device it picks, what it does when a stage returns nothing, and that it
/// does not measure a network it has no reason to measure.
/// </summary>
public sealed class CapabilityProberTests
{
    [Fact]
    public async Task ProbeAsync_PrefersAReachableDeviceOverAMerelyAdvertisedOne()
    {
        // A report about a device Flint can talk to is more useful than one about a name it saw.

        // Arrange
        var unreachable = Device("192.168.1.10", AdbConnectionState.Refused);
        var reachable = Device("192.168.1.42", AdbConnectionState.Connected);
        var prober = Prober([unreachable, reachable], Build.Host(), Build.Path());

        // Act
        var report = await prober.ProbeAsync(TestContext.Current.CancellationToken);

        // Assert
        report.Device.ShouldNotBeNull();
        report.Device.Address.ToString().ShouldBe("192.168.1.42");
    }

    [Fact]
    public async Task ProbeAsync_NoReachableDevice_StillReportsTheFirstOneSeen()
    {
        // Knowing a television is there but unidentifiable is the ambiguous case worth explaining.

        // Arrange
        var seen = Device("192.168.1.10", AdbConnectionState.Refused);
        var prober = Prober([seen], Build.Host(), Build.Path());

        // Act
        var report = await prober.ProbeAsync(TestContext.Current.CancellationToken);

        // Assert
        report.Device.ShouldNotBeNull();
        report.Device.Address.ToString().ShouldBe("192.168.1.10");
    }

    [Fact]
    public async Task ProbeAsync_NoDevices_ReportsNullWithoutFailing()
    {
        // Arrange
        var prober = Prober([], Build.Host(), Build.Path());

        // Act
        var report = await prober.ProbeAsync(TestContext.Current.CancellationToken);

        // Assert
        report.Device.ShouldBeNull();
        report.Verdicts.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ProbeAsync_NoDevices_DoesNotMeasureTheNetwork()
    {
        // There is nothing to measure against, and inventing a measurement would be worse than none.

        // Arrange
        var network = new RecordingNetworkProbe(Build.Path());
        var prober = new CapabilityProber(
            new StubHostProbe(Build.Host()),
            new StubDeviceProbe([]),
            network);

        // Act
        var report = await prober.ProbeAsync(TestContext.Current.CancellationToken);

        // Assert
        network.Calls.ShouldBe(0);
        report.Path.ShouldBeNull();
    }

    [Fact]
    public async Task ProbeAsync_MeasuresExactlyOnceForTheChosenDevice()
    {
        // Arrange
        var network = new RecordingNetworkProbe(Build.Path());
        var prober = new CapabilityProber(
            new StubHostProbe(Build.Host()),
            new StubDeviceProbe([Device("192.168.1.42", AdbConnectionState.Connected)]),
            network);

        // Act
        await prober.ProbeAsync(TestContext.Current.CancellationToken);

        // Assert
        network.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task ProbeAsync_CarriesTheHostAndPathIntoTheReport()
    {
        // Arrange
        var host = Build.Host();
        var path = Build.Path();
        var prober = Prober([Device("192.168.1.42", AdbConnectionState.Connected)], host, path);

        // Act
        var report = await prober.ProbeAsync(TestContext.Current.CancellationToken);

        // Assert
        report.Host.ShouldBe(host);
        report.Path.ShouldBe(path);
    }

    [Fact]
    public async Task ProbeAsync_UnmeasurablePath_LeavesItNullRatherThanZero()
    {
        // A zeroed path would read as a perfect link, which is the opposite of the truth.

        // Arrange
        var prober = new CapabilityProber(
            new StubHostProbe(Build.Host()),
            new StubDeviceProbe([Device("192.168.1.42", AdbConnectionState.Connected)]),
            new RecordingNetworkProbe(null));

        // Act
        var report = await prober.ProbeAsync(TestContext.Current.CancellationToken);

        // Assert
        report.Path.ShouldBeNull();
    }

    [Fact]
    public async Task ProbeAsync_PropagatesAFailureRatherThanReturningAnEmptyReport()
    {
        // A silent empty report would be indistinguishable from "nothing is here".

        // Arrange
        var prober = new CapabilityProber(
            new ThrowingHostProbe(),
            new StubDeviceProbe([]),
            new RecordingNetworkProbe(null));

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(
            () => prober.ProbeAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProbeAddressAsync_UsesOnlyTheNamedEndpointAndMeasuresItWhenReachable()
    {
        var device = Device("192.168.1.88", AdbConnectionState.Connected) with
        {
            Source = DiscoverySource.Manual,
        };
        var addressable = new RecordingAddressableDeviceProbe(device);
        var network = new RecordingNetworkProbe(Build.Path());
        var prober = new CapabilityProber(new StubHostProbe(Build.Host()), addressable, network);

        var report = await prober.ProbeAddressAsync(
            IPAddress.Parse("192.168.1.88"),
            5557,
            TestContext.Current.CancellationToken);

        addressable.DiscoveryCalls.ShouldBe(0);
        addressable.AddressCalls.ShouldBe(1);
        addressable.LastAddress.ShouldBe(IPAddress.Parse("192.168.1.88"));
        addressable.LastPort.ShouldBe(5557);
        network.Calls.ShouldBe(1);
        report.Device.ShouldBe(device);
    }

    [Fact]
    public async Task ProbeAddressAsync_UnreachableEndpoint_DoesNotInventAPathMeasurement()
    {
        var device = Device("192.168.1.88", AdbConnectionState.Refused) with
        {
            Source = DiscoverySource.Manual,
        };
        var network = new RecordingNetworkProbe(Build.Path());
        var prober = new CapabilityProber(
            new StubHostProbe(Build.Host()),
            new RecordingAddressableDeviceProbe(device),
            network);

        var report = await prober.ProbeAddressAsync(
            device.Address,
            cancellationToken: TestContext.Current.CancellationToken);

        network.Calls.ShouldBe(0);
        report.Path.ShouldBeNull();
    }

    [Fact]
    public async Task ProbeAddressAsync_DiscoveryOnlyProbe_ReportsUnsupportedInsteadOfScanning()
    {
        var prober = Prober([], Build.Host(), Build.Path());

        await Should.ThrowAsync<NotSupportedException>(
            () => prober.ProbeAddressAsync(
                IPAddress.Parse("192.168.1.88"),
                cancellationToken: TestContext.Current.CancellationToken));
    }

    private static CapabilityProber Prober(
        IReadOnlyList<FireTvDevice> devices,
        HostCapabilities host,
        NetworkPath? path) =>
        new(new StubHostProbe(host), new StubDeviceProbe(devices), new RecordingNetworkProbe(path));

    private static FireTvDevice Device(string address, AdbConnectionState state) =>
        new(IPAddress.Parse(address), address, DiscoverySource.MulticastDns)
        {
            AdbState = state,
            AdbPort = 5555,
        };

    private sealed class StubHostProbe(HostCapabilities host) : IHostProbe
    {
        public Task<HostCapabilities> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(host);
    }

    private sealed class ThrowingHostProbe : IHostProbe
    {
        public Task<HostCapabilities> ProbeAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("probe failed");
    }

    private sealed class StubDeviceProbe(IReadOnlyList<FireTvDevice> devices) : IDeviceProbe
    {
        public Task<IReadOnlyList<FireTvDevice>> DiscoverAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(devices);
    }

    private sealed class RecordingAddressableDeviceProbe(FireTvDevice device) : IAddressableDeviceProbe
    {
        internal int DiscoveryCalls { get; private set; }

        internal int AddressCalls { get; private set; }

        internal IPAddress? LastAddress { get; private set; }

        internal int? LastPort { get; private set; }

        public Task<IReadOnlyList<FireTvDevice>> DiscoverAsync(
            CancellationToken cancellationToken = default)
        {
            DiscoveryCalls++;
            return Task.FromResult<IReadOnlyList<FireTvDevice>>([device]);
        }

        public Task<FireTvDevice> ProbeAddressAsync(
            IPAddress address,
            int? port = null,
            CancellationToken cancellationToken = default)
        {
            AddressCalls++;
            LastAddress = address;
            LastPort = port;
            return Task.FromResult(device);
        }
    }

    private sealed class RecordingNetworkProbe(NetworkPath? path) : INetworkProbe
    {
        internal int Calls { get; private set; }

        public Task<NetworkPath?> MeasureAsync(
            FireTvDevice device,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(path);
        }
    }
}
