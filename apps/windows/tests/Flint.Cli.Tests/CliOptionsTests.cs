using System.Net;
using Shouldly;

namespace Flint.Cli.Tests;

public sealed class CliOptionsTests
{
    [Fact]
    public void EmptyArguments_SelectNormalDiscovery()
    {
        CliOptions.TryParse([], out var options, out var error).ShouldBeTrue();

        error.ShouldBeNull();
        options.ShouldNotBeNull();
        options.ScanServices.ShouldBeFalse();
        options.Endpoint.ShouldBeNull();
    }

    [Fact]
    public void AddressAndPort_SelectOneExplicitEndpoint()
    {
        CliOptions.TryParse(
            ["--address", "10.46.161.42", "--port", "5557"],
            out var options,
            out var error).ShouldBeTrue();

        error.ShouldBeNull();
        options.ShouldNotBeNull();
        options.Endpoint.ShouldNotBeNull();
        options.Endpoint.Address.ShouldBe(IPAddress.Parse("10.46.161.42"));
        options.Endpoint.Port.ShouldBe(5557);
    }

    [Fact]
    public void AddressWithoutPort_SelectsTheBoundedRange()
    {
        CliOptions.TryParse(["--address", "192.168.1.8"], out var options, out _)
            .ShouldBeTrue();

        options.ShouldNotBeNull();
        options.Endpoint.ShouldNotBeNull();
        options.Endpoint.Port.ShouldBeNull();
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--address")]
    [InlineData("--port")]
    public void MalformedOption_IsRejected(string argument)
    {
        CliOptions.TryParse([argument], out var options, out var error).ShouldBeFalse();

        options.ShouldBeNull();
        error.ShouldNotBeNull();
    }

    [Fact]
    public void PortWithoutAddress_IsRejected()
    {
        CliOptions.TryParse(["--port", "5555"], out _, out var error).ShouldBeFalse();

        error.ShouldBe("--port requires --address.");
    }

    [Fact]
    public void ServiceScanCannotAlsoProbeAnEndpoint()
    {
        CliOptions.TryParse(
            ["--services", "--address", "192.168.1.8"],
            out _,
            out var error).ShouldBeFalse();

        error.ShouldNotBeNull();
        error.ShouldContain("cannot be combined");
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_IsAccepted(string argument)
    {
        CliOptions.TryParse([argument], out var options, out _).ShouldBeTrue();

        options.ShouldNotBeNull();
        options.ShowHelp.ShouldBeTrue();
    }

    [Fact]
    public void MediaWithAPairedEndpoint_SelectsLiveMediaPlayback()
    {
        CliOptions.TryParse(
            ["--address", "10.46.161.42", "--pairing-code", "123456", "--media", "C:\\clip.mp4"],
            out var options,
            out var error).ShouldBeTrue();

        error.ShouldBeNull();
        options.ShouldNotBeNull();
        options.MediaPath.ShouldBe("C:\\clip.mp4");
    }

    [Fact]
    public void PortOnAPairedEndpoint_SelectsTheReceiverAndLeavesAdbOnItsBoundedScan()
    {
        CliOptions.TryParse(
            ["--address", "10.46.161.42", "--port", "47856", "--pairing-code", "123456", "--mirror"],
            out var options,
            out var error).ShouldBeTrue();

        error.ShouldBeNull();
        options.ShouldNotBeNull();
        options.ReceiverPort.ShouldBe(47856);
        options.Endpoint.ShouldNotBeNull();
        options.Endpoint.Port.ShouldBeNull();
    }

    [Fact]
    public void ExplicitReceiverPortAlsoLeavesAdbOnItsBoundedScan()
    {
        CliOptions.TryParse(
            ["--address", "10.46.161.42", "--receiver-port", "47856", "--pairing-code", "123456"],
            out var options,
            out var error).ShouldBeTrue();

        error.ShouldBeNull();
        options.ShouldNotBeNull();
        options.ReceiverPort.ShouldBe(47856);
        options.Endpoint.ShouldNotBeNull();
        options.Endpoint.Port.ShouldBeNull();
    }

    [Fact]
    public void PairedEndpointRejectsTwoReceiverPortSpellings()
    {
        CliOptions.TryParse(
            [
                "--address", "10.46.161.42",
                "--port", "47855",
                "--receiver-port", "47856",
                "--pairing-code", "123456",
            ],
            out _,
            out var error).ShouldBeFalse();

        error.ShouldBe("Use either --port or --receiver-port for a paired receiver, not both.");
    }

    [Fact]
    public void MediaWithoutAPairingCode_IsRejected()
    {
        CliOptions.TryParse(
            ["--address", "10.46.161.42", "--media", "C:\\clip.mp4"],
            out _,
            out var error).ShouldBeFalse();

        error.ShouldBe("--media requires --address and --pairing-code.");
    }

    [Fact]
    public void MirrorWithAPairedEndpoint_SelectsLiveScreenMirroring()
    {
        CliOptions.TryParse(
            ["--address", "10.46.161.42", "--pairing-code", "123456", "--mirror"],
            out var options,
            out var error).ShouldBeTrue();

        error.ShouldBeNull();
        options.ShouldNotBeNull();
        options.Mirror.ShouldBeTrue();
    }

    [Fact]
    public void Mirror_DefaultsToCappingTheEncodeAtTheTelevisionsOwnResolution()
    {
        // A 4K desktop encoded at source resolution costs encode time and bandwidth for detail a
        // 1080p television throws away, so the default caps rather than matching the desktop.
        CliOptions.TryParse(
            ["--address", "10.46.161.42", "--pairing-code", "123456", "--mirror"],
            out var options,
            out _).ShouldBeTrue();

        options.ShouldNotBeNull();
        options.MirrorMaxWidth.ShouldBe(1920u);
    }

    [Fact]
    public void MirrorWidth_OverridesTheDefaultCap()
    {
        CliOptions.TryParse(
            ["--address", "10.46.161.42", "--pairing-code", "123456", "--mirror", "--mirror-width", "1280"],
            out var options,
            out _).ShouldBeTrue();

        options.ShouldNotBeNull();
        options.MirrorMaxWidth.ShouldBe(1280u);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("319")]
    [InlineData("7681")]
    [InlineData("not-a-number")]
    public void MirrorWidth_OutsideWhatAnEncoderWillAccept_IsRejected(string width)
    {
        CliOptions.TryParse(
            ["--address", "10.46.161.42", "--pairing-code", "123456", "--mirror", "--mirror-width", width],
            out _,
            out var error).ShouldBeFalse();

        error.ShouldNotBeNull();
        error.ShouldContain("--mirror-width");
    }

    [Fact]
    public void MirrorWithoutAPairingCode_IsRejected()
    {
        CliOptions.TryParse(["--address", "10.46.161.42", "--mirror"], out _, out var error)
            .ShouldBeFalse();

        error.ShouldBe("--mirror requires --address and --pairing-code.");
    }

    [Fact]
    public void MirrorAndMediaTogether_IsRejectedBecauseOneSurfaceCannotShowBoth()
    {
        CliOptions.TryParse(
            [
                "--address", "10.46.161.42",
                "--pairing-code", "123456",
                "--mirror",
                "--media", "C:\\clip.mp4",
            ],
            out _,
            out var error).ShouldBeFalse();

        error.ShouldBe("--mirror cannot be combined with --media.");
    }

    [Fact]
    public void NoMirrorFlag_LeavesMirroringOff()
    {
        CliOptions.TryParse([], out var options, out _).ShouldBeTrue();

        options.ShouldNotBeNull();
        options.Mirror.ShouldBeFalse();
    }
}
