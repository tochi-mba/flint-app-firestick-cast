using Shouldly;

namespace Flint.Core.Tests;

/// <summary>Spotting a VPN on this PC from its network adapters, and what Flint says about it.</summary>
public sealed class VpnDetectionTests
{
    private static readonly NetworkAdapterFacts WiFi = new("WiFi", "Intel(R) Wi-Fi 6 AX201 160MHz", IsUp: true, IsPointToPoint: false);

    [Theory]
    [InlineData("ProtonVPN", "ProtonVPN Tunnel")]
    [InlineData("Local Area Connection 3", "WireGuard Tunnel")]
    [InlineData("Ethernet 7", "TAP-Windows Adapter V9")]
    [InlineData("NordLynx", "NordLynx Tunnel")]
    [InlineData("Ethernet 2", "Cisco AnyConnect Secure Mobility Client Virtual Miniport Adapter")]
    public void AnAdapterThatIsAVpn_IsNamed(string name, string description) =>
        VpnDetection.ActiveVpn([WiFi, new(name, description, IsUp: true, IsPointToPoint: false)]).ShouldBe(name);

    [Fact]
    public void WindowsOwnVpnConnection_IsAPointToPointLink() =>
        VpnDetection.ActiveVpn([WiFi, new("Work", "WAN Miniport (IKEv2)", IsUp: true, IsPointToPoint: true)]).ShouldBe("Work");

    [Fact]
    public void AVpnThatIsOff_OrOrdinaryAdapters_AreNotAVpn()
    {
        VpnDetection.ActiveVpn([WiFi, new("ProtonVPN", "ProtonVPN Tunnel", IsUp: false, IsPointToPoint: false)]).ShouldBeNull();
        VpnDetection.ActiveVpn([WiFi, new("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter", IsUp: true, IsPointToPoint: false)])
            .ShouldBeNull();
        VpnDetection.ActiveVpn([]).ShouldBeNull();
        Should.Throw<ArgumentNullException>(() => VpnDetection.ActiveVpn(null!));
    }

    [Fact]
    public void TheAdvice_NamesTheVpn_AndWhatToChange()
    {
        var advice = VpnDetection.Advice("ProtonVPN");

        advice.ShouldStartWith("ProtonVPN is on.");
        advice.ShouldContain("Allow local network access in ProtonVPN's settings");
        Should.Throw<ArgumentException>(() => VpnDetection.Advice(" "));
    }

    [Fact]
    public void NoTvFound_WithAVpnOn_SaysTheVpnRatherThanTheUsualChecks()
    {
        var host = new HostCapabilities([], [], 0, 26100, ActiveVpn: "ProtonVPN");

        var report = CapabilityAssessor.Assess(device: null, host, path: null);

        report.Verdicts.ShouldAllBe(verdict => verdict.Remedy!.StartsWith("ProtonVPN is on.", StringComparison.Ordinal));
        CapabilityAssessor.Assess(device: null, host with { ActiveVpn = null }, path: null).Verdicts
            .ShouldAllBe(verdict => verdict.Remedy!.StartsWith("Check that the television is powered on", StringComparison.Ordinal));
    }
}
