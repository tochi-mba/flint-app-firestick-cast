using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Flint.App.Controls;
using Flint.App.ViewModels;
using Flint.App.Views;
using Flint.Core;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>Pins Diagnostics as a working destination backed only by facts from the probe.</summary>
public sealed class DiagnosticsPageTests
{
    [Fact]
    public void Navigation_AllRailDestinationsHaveConnectedPages()
    {
        var shell = MainWindowViewModel.CreateWith(Prober());

        shell.Destinations.Single(destination => destination.Label == "Diagnostics")
            .IsImplemented.ShouldBeTrue();
        shell.Destinations.Single(destination => destination.Label == "Settings")
            .IsImplemented.ShouldBeTrue();
        shell.Destinations.Single(destination => destination.Label == "Media")
            .IsImplemented.ShouldBeTrue();
        shell.Destinations.Single(destination => destination.Label == "Screen")
            .IsImplemented.ShouldBeTrue();
    }

    [Fact]
    public void Navigation_SelectingDiagnosticsChangesTheVisibleDestinationState()
    {
        var shell = MainWindowViewModel.CreateWith(Prober());

        shell.Selected = shell.Destinations.Single(destination => destination.Label == "Diagnostics");

        shell.IsDiagnosticsSelected.ShouldBeTrue();
        shell.IsCastSelected.ShouldBeFalse();
        shell.IsPlaceholderSelected.ShouldBeFalse();
    }

    [Fact]
    public async Task Probe_DerivesHostReceiverAndNetworkDiagnostics()
    {
        var shell = MainWindowViewModel.CreateWith(Prober());

        await shell.Cast.ProbeCommand.ExecuteAsync(null);

        shell.Cast.DeviceModel.ShouldBe("AFTKA");
        shell.Cast.AdbStatus.ShouldBe("Connected");
        shell.Cast.AndroidApiLevel.ShouldBe("30");
        shell.Cast.AndroidRelease.ShouldBe("11");
        shell.Cast.WindowsBuild.ShouldBe("26100");
        shell.Cast.EncoderProbeStatus.ShouldBe("Complete");
        shell.Cast.GraphicsAdapters.ShouldContain("Integrated GPU (display)");
        shell.Cast.GraphicsAdapters.ShouldContain("Basic Renderer (software)");
        shell.Cast.PrimaryDisplayAdapter.ShouldBe("Integrated GPU");
        shell.Cast.HardwareEncoders.ShouldContain("Nvenc: H264, H265");
        shell.Cast.RoundTrip.ShouldBe("4.2 ms");
        shell.Cast.Jitter.ShouldBe("0.8 ms");
        shell.Cast.Throughput.ShouldBe("Not measured");
        shell.Cast.PacketLoss.ShouldBe("14%");
    }

    [AvaloniaFact]
    public async Task View_BindsProbeFactsIntoDiagnosticRowsInsteadOfShowingABlankPage()
    {
        var shell = MainWindowViewModel.CreateWith(Prober());
        var diagnostics = new DiagnosticsPage { DataContext = shell.Cast };
        var window = new Window { Width = 900, Height = 700, Content = diagnostics };
        try
        {
            window.Show();
            window.UpdateLayout();

            diagnostics.GetVisualDescendants().OfType<TextBlock>()
                .Select(text => text.Text)
                .ShouldContain("Probe before diagnosing");

            await shell.Cast.ProbeCommand.ExecuteAsync(null);
            window.UpdateLayout();

            var rows = diagnostics.GetVisualDescendants().OfType<DiagnosticRow>().ToList();
            rows.ShouldNotBeEmpty();
            rows.Single(row => row.Label == "Model").Value.ShouldBe("AFTKA");
            rows.Single(row => row.Label == "Primary display adapter").Value
                .ShouldBe("Integrated GPU");
            rows.Single(row => row.Label == "Throughput").Value.ShouldBe("Not measured");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_SelectingDiagnosticsShowsItsPageAndHidesCast()
    {
        var shell = MainWindowViewModel.CreateWith(Prober());
        var window = new MainWindow { DataContext = shell };
        try
        {
            window.Show();
            window.UpdateLayout();

            shell.Selected = shell.Destinations.Single(destination => destination.Label == "Diagnostics");
            window.UpdateLayout();

            window.GetVisualDescendants().OfType<DiagnosticsPage>().Single().IsVisible.ShouldBeTrue();
            window.GetVisualDescendants().OfType<CastPage>().Single().IsVisible.ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_SelectingMediaOrScreenShowsTheDedicatedPage()
    {
        var shell = MainWindowViewModel.CreateWith(Prober());
        var window = new MainWindow { DataContext = shell };
        try
        {
            window.Show();
            shell.Selected = shell.Destinations.Single(destination => destination.Label == "Media");
            window.UpdateLayout();
            window.GetVisualDescendants().OfType<MediaPage>().Single().IsVisible.ShouldBeTrue();

            shell.Selected = shell.Destinations.Single(destination => destination.Label == "Screen");
            window.UpdateLayout();
            window.GetVisualDescendants().OfType<ScreenPage>().Single().IsVisible.ShouldBeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    private static CapabilityProber Prober()
    {
        var device = new FireTvDevice(
            IPAddress.Parse("192.168.1.42"),
            "Living Room",
            DiscoverySource.MulticastDns)
        {
            Platform = FireTvPlatform.FireOs8,
            AdbState = AdbConnectionState.Connected,
            AdbPort = 5555,
            Model = "AFTKA",
            AndroidApiLevel = 30,
            AndroidRelease = "11",
        };
        var host = new HostCapabilities(
            [
                new DisplayAdapter(1, "Integrated GPU", DrivesDisplay: true),
                new DisplayAdapter(2, "Discrete GPU", DrivesDisplay: false),
                new DisplayAdapter(3, "Basic Renderer", DrivesDisplay: false, IsSoftware: true),
            ],
            [
                new HostVideoEncoder(
                    EncoderVendor.Nvenc,
                    2,
                    new HashSet<VideoCodec> { VideoCodec.H265, VideoCodec.H264 }),
            ],
            PrimaryDisplayAdapterLuid: 1,
            WindowsBuild: 26100,
            EncodersProbed: true);
        var path = new NetworkPath(
            RoundTripMs: 4.2,
            JitterMs: 0.8,
            ThroughputMbps: TcpProbeFixtures.UnmeasuredThroughput,
            PacketLossPercent: 14,
            ThroughputMeasured: false);

        return new CapabilityProber(
            new FixedHostProbe(host),
            new FixedDeviceProbe(device),
            new FixedNetworkProbe(path));
    }

    private static class TcpProbeFixtures
    {
        internal const double UnmeasuredThroughput = -1;
    }

    private sealed class FixedHostProbe(HostCapabilities host) : IHostProbe
    {
        public Task<HostCapabilities> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(host);
    }

    private sealed class FixedDeviceProbe(FireTvDevice device) : IDeviceProbe
    {
        public Task<IReadOnlyList<FireTvDevice>> DiscoverAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FireTvDevice>>([device]);
    }

    private sealed class FixedNetworkProbe(NetworkPath path) : INetworkProbe
    {
        public Task<NetworkPath?> MeasureAsync(
            FireTvDevice device,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<NetworkPath?>(path);
    }
}
