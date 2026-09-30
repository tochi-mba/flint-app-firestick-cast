using System.Net;
using Shouldly;

namespace Flint.Discovery.Tests;

/// <summary>The fallback scanner accepts only an exact Flint receiver answer.</summary>
public sealed class ReceiverProbeScannerTests
{
    private static readonly IPAddress Address = IPAddress.Parse("192.168.1.42");

    [Fact]
    public void ParsesAReceiverAnswer()
    {
        var answer = ReceiverProbeScanner.Parse(
            Address,
            "REXCAST RECEIVER/1\tLiving Room\t47855\n"u8);

        answer.ShouldNotBeNull();
        answer.Address.ShouldBe(Address);
        answer.ModelName.ShouldBe("Living Room");
        answer.ServicePort.ShouldBe(47_855);
    }

    [Theory]
    [InlineData("")]
    [InlineData("REXCAST RECEIVER/2\tLiving Room\t47855\n")]
    [InlineData("REXCAST RECEIVER/1\t\t47855\n")]
    [InlineData("REXCAST RECEIVER/1\tLiving Room\t0\n")]
    [InlineData("REXCAST RECEIVER/1\tLiving Room\t70000\n")]
    [InlineData("REXCAST RECEIVER/1\tLiving Room\n")]
    public void RejectsAnythingThatIsNotAnExactReceiverAnswer(string response)
    {
        ReceiverProbeScanner.Parse(Address, System.Text.Encoding.UTF8.GetBytes(response)).ShouldBeNull();
    }

    [Fact]
    public void RejectsAnUnboundedAnswer()
    {
        ReceiverProbeScanner.Parse(Address, new byte[513]).ShouldBeNull();
    }
}
