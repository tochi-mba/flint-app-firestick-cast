using System.Net;
using Flint.Core;
using Shouldly;

namespace Flint.Discovery.Tests;

public sealed class BrowserDiscoveryEvidenceTests
{
    [Fact]
    public void FromServiceInstance_ValidBrowserTxt_ProducesEligibleRoutingEvidence()
    {
        var instance = new ServiceInstance(
            "Living Room",
            IPAddress.Parse("192.168.1.42"),
            17420,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["browser_port"] = "8443",
                ["browser_protocol"] = "2",
            },
            ["browser_port=8443", "browser_protocol=2"]);

        var evidence = BrowserDiscoveryEvidence.FromServiceInstance(instance);

        evidence.ShouldNotBeNull();
        evidence!.SecureEndpointAvailable.ShouldBeTrue();
        evidence.SecureEndpointPort.ShouldBe(8443);
        evidence.MaximumProtocolVersion.ShouldBe(2);
        evidence.WebViewProbe.ShouldBe(BrowserWebViewProbe.Passed);
    }

    [Fact]
    public void FromServiceInstance_NoBrowserTxt_ReturnsNull()
    {
        var instance = new ServiceInstance(
            "Living Room",
            IPAddress.Parse("192.168.1.42"),
            17420,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["fn"] = "Living Room",
            });

        BrowserDiscoveryEvidence.FromServiceInstance(instance).ShouldBeNull();
    }
}
