using System.Net;
using Shouldly;

namespace Flint.Core.Tests;

public sealed class ManualProbeEndpointTests
{
    [Fact]
    public void TryParse_AddressWithBlankPort_UsesTheBoundedDefaultScan()
    {
        ManualProbeEndpoint.TryParse(" 10.46.161.42 ", " ", out var endpoint, out var error)
            .ShouldBeTrue();

        error.ShouldBeNull();
        endpoint.ShouldNotBeNull();
        endpoint.Address.ShouldBe(IPAddress.Parse("10.46.161.42"));
        endpoint.Port.ShouldBeNull();
    }

    [Fact]
    public void TryParse_ExplicitPort_PreservesIt()
    {
        ManualProbeEndpoint.TryParse("192.168.1.8", "5557", out var endpoint, out _)
            .ShouldBeTrue();

        endpoint.ShouldNotBeNull();
        endpoint.Port.ShouldBe(5557);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("television.local")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("224.0.0.251")]
    [InlineData("ff02::fb")]
    public void TryParse_NonUnicastOrNonNumericAddress_IsRejected(string? address)
    {
        ManualProbeEndpoint.TryParse(address, null, out var endpoint, out var error)
            .ShouldBeFalse();

        endpoint.ShouldBeNull();
        error.ShouldNotBeNull();
        error.ShouldContain("unicast IP address");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("5555.0")]
    [InlineData("not-a-port")]
    public void TryParse_InvalidPort_IsRejected(string port)
    {
        ManualProbeEndpoint.TryParse("192.168.1.8", port, out var endpoint, out var error)
            .ShouldBeFalse();

        endpoint.ShouldBeNull();
        error.ShouldNotBeNull();
        error.ShouldContain("1 to 65535");
    }
}
