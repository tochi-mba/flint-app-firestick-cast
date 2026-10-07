using System.Net;
using System.Net.Sockets;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Core;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// A VPN on this PC that takes local traffic into its tunnel, named when the TV cannot be reached.
/// </summary>
public sealed class CastPageVpnTests
{
    [Fact]
    public async Task APairingThatCannotReachTheTv_NamesTheVpnOnThisPc()
    {
        var page = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(
            BrowserFixtures.EligibleDevice() with { Address = IPAddress.Loopback })).Cast;
        page.FindActiveVpn = () => "ProtonVPN";
        await page.ProbeCommand.ExecuteAsync(null);
        page.PairingCode = "123456";
        page.ReceiverPort = ClosedPort().ToString(System.Globalization.CultureInfo.InvariantCulture);

        await page.ConnectCommand.ExecuteAsync(null);

        page.Failure.ShouldNotBeNull();
        page.Failure.ShouldEndWith(VpnDetection.Advice("ProtonVPN"));
        page.IsSessionConnected.ShouldBeFalse();
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(SocketException))]
    [InlineData(typeof(TimeoutException))]
    public void ANetworkFailure_WithAVpnOn_SaysSo(Type kind)
    {
        var page = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice())).Cast;
        page.FindActiveVpn = () => "WireGuard";
        var failure = (Exception)Activator.CreateInstance(kind)!;

        page.DescribeReachFailure(failure).ShouldBe($"{failure.Message} {VpnDetection.Advice("WireGuard")}");
    }

    [Fact]
    public void AFailureThatIsNotTheNetwork_OrNoVpn_IsSaidAsItIs()
    {
        var page = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice())).Cast;
        page.FindActiveVpn = () => "WireGuard";
        page.DescribeReachFailure(new InvalidOperationException("The pairing code was not accepted."))
            .ShouldBe("The pairing code was not accepted.", "a wrong code is not the VPN's doing");

        page.FindActiveVpn = () => null;
        page.DescribeReachFailure(new IOException("The TV stopped answering.")).ShouldBe("The TV stopped answering.");
    }

    [Fact]
    public void ThisPcsAdapters_AreReadWithoutTheLoopback()
    {
        var adapters = SystemNetworkAdapters.Read();

        adapters.ShouldNotContain(adapter => adapter.Description.Contains("Loopback", StringComparison.OrdinalIgnoreCase));
        Should.NotThrow(() => SystemNetworkAdapters.ActiveVpn());
    }

    [Fact]
    public async Task TheHostProbe_SaysWhetherAVpnIsOn_AsTheAdaptersDo()
    {
        var host = await new WindowsHostProbe().ProbeAsync(TestContext.Current.CancellationToken);

        host.WindowsBuild.ShouldBeGreaterThan(0);
        host.ActiveVpn.ShouldBe(SystemNetworkAdapters.ActiveVpn());
    }

    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
