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

    [Fact]
    public void ParsesAnAnswerEndingInCarriageReturnAndNewline()
    {
        var answer = ReceiverProbeScanner.Parse(Address, "REXCAST RECEIVER/1\tLiving Room\t47855\r\n"u8);

        answer.ShouldNotBeNull();
        answer.ModelName.ShouldBe("Living Room");
        answer.ServicePort.ShouldBe(47_855);
    }

    [Theory]
    [InlineData("")]
    [InlineData("REXCAST RECEIVER/2\tLiving Room\t47855\n")]
    [InlineData("REXCAST RECEIVER/1\t\t47855\n")]
    [InlineData("REXCAST RECEIVER/1\tLiving Room\t0\n")]
    [InlineData("REXCAST RECEIVER/1\tLiving Room\t70000\n")]
    [InlineData("REXCAST RECEIVER/1\tLiving Room\tabc\n")]
    [InlineData("REXCAST RECEIVER/1\tLiving Room\n")]
    public void RejectsAnythingThatIsNotAnExactReceiverAnswer(string response)
    {
        ReceiverProbeScanner.Parse(Address, System.Text.Encoding.UTF8.GetBytes(response)).ShouldBeNull();
    }

    [Theory]
    [InlineData(512, true)]
    [InlineData(513, false)]
    public void AnAnswerIsAcceptedUpToTheBoundAndNoFurther(int length, bool accepted)
    {
        // A well-formed answer whose name pads it to exactly `length` bytes, so only the bound differs.
        const string Prefix = "REXCAST RECEIVER/1\t";
        const string Suffix = "\t47855\n";
        var response = Prefix + new string('n', length - Prefix.Length - Suffix.Length) + Suffix;
        System.Text.Encoding.UTF8.GetByteCount(response).ShouldBe(length);

        (ReceiverProbeScanner.Parse(Address, System.Text.Encoding.UTF8.GetBytes(response)) is not null).ShouldBe(accepted);
    }
}
