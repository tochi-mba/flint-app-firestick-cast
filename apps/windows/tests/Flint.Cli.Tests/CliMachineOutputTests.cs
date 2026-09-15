using System.Net;
using System.Text.Json;
using Flint.Cli;
using Flint.Core;
using Shouldly;

namespace Flint.Cli.Tests;

/// <summary>
/// The parts of the CLI that something other than a person consumes.
/// </summary>
/// <remarks>
/// Exit codes and <c>--json</c> are a contract. Scripts, CI steps and the hardware runners branch
/// on them, so a number that quietly changes meaning, or a field that disappears in a rename, is a
/// broken caller that nothing else catches.
/// </remarks>
public sealed class CliMachineOutputTests
{
    [Fact]
    public void Version_IsAnsweredWithoutARestOfTheCommandLineBeingValid()
    {
        // Someone asking what this build is should not have to make the rest of their arguments
        // parse first.
        var parsed = CliOptions.TryParse(["--version", "--address", "not-an-address"], out var options, out var error);

        parsed.ShouldBeTrue(error);
        options!.ShowVersion.ShouldBeTrue();
    }

    [Fact]
    public void Help_IsAnsweredWithoutARestOfTheCommandLineBeingValid()
    {
        var parsed = CliOptions.TryParse(["--help", "--port", "not-a-port"], out var options, out var error);

        parsed.ShouldBeTrue(error);
        options!.ShowHelp.ShouldBeTrue();
    }

    [Fact]
    public void Json_IsCarriedThroughToAProbeRun()
    {
        var parsed = CliOptions.TryParse(
            ["--json", "--address", "10.46.161.42"], out var options, out var error);

        parsed.ShouldBeTrue(error);
        options!.Json.ShouldBeTrue();
        options.ShowHelp.ShouldBeFalse();
        options.Endpoint.ShouldNotBeNull();
    }

    [Fact]
    public void Version_DescribesTheBuildRatherThanAPlaceholder()
    {
        // Read from the assembly, so it cannot drift from what shipped. A version string that
        // lies about the build is worse than none, because bug reports quote it.
        var described = FlintVersion.Describe();

        described.ShouldStartWith("flint ");
        described.ShouldNotContain("unknown");
        described.ShouldContain(".NET");
    }

    [Fact]
    public void ExitCodes_AreDistinct()
    {
        // Two outcomes sharing a number is a caller that cannot tell them apart.
        int[] codes =
        [
            FlintExitCode.Success,
            FlintExitCode.ProbeTimedOut,
            FlintExitCode.MirrorProducedNoFrames,
            FlintExitCode.MirrorUnsupported,
            FlintExitCode.UsageError,
            FlintExitCode.ReceiverUnavailable,
        ];

        codes.Distinct().Count().ShouldBe(codes.Length);
    }

    [Fact]
    public void ExitCodes_KeepTheirPublishedValues()
    {
        // Pinned because scripts and CI branch on the numbers, not the names. Changing one is a
        // breaking change and should have to be made deliberately, here.
        FlintExitCode.Success.ShouldBe(0);
        FlintExitCode.ProbeTimedOut.ShouldBe(2);
        FlintExitCode.MirrorProducedNoFrames.ShouldBe(3);
        FlintExitCode.MirrorUnsupported.ShouldBe(4);
        FlintExitCode.UsageError.ShouldBe(64);
        FlintExitCode.ReceiverUnavailable.ShouldBe(69);
    }

    [Fact]
    public void Json_RendersEveryTopLevelSectionOfTheVerdict()
    {
        var json = CapabilityReportJson.Render(Report(), TimeSpan.FromSeconds(1.5));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("schema").GetInt32().ShouldBe(1);
        root.GetProperty("elapsedSeconds").GetDouble().ShouldBe(1.5);
        foreach (var section in new[] { "host", "device", "path", "browser", "verdicts" })
        {
            root.TryGetProperty(section, out _).ShouldBeTrue($"{section} is missing from the output");
        }
    }

    [Fact]
    public void Json_WritesEnumsAsNamesRatherThanNumbers()
    {
        // A script asserting "Available" keeps working when a value is inserted above it in the
        // enum; one asserting 1 does not.
        var json = CapabilityReportJson.Render(Report(), TimeSpan.Zero);

        using var document = JsonDocument.Parse(json);
        var verdict = document.RootElement.GetProperty("verdicts")[0];

        verdict.GetProperty("mode").GetString().ShouldBe(nameof(CastMode.Mirror));
        verdict.GetProperty("status").GetString().ShouldBe(nameof(ModeStatus.Available));
    }

    [Fact]
    public void Json_KeepsAnAbsentDeviceNullRatherThanInventingOne()
    {
        // A probe that found nothing must not look like a probe that found something empty.
        var report = Report() with { Device = null, Path = null };

        var json = CapabilityReportJson.Render(report, TimeSpan.Zero);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("device").ValueKind.ShouldBe(JsonValueKind.Null);
        document.RootElement.GetProperty("path").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void Json_SaysWhetherThroughputWasMeasuredRatherThanReportingZero()
    {
        // Zero throughput that was never measured is not a slow network, and a script must be able
        // to tell those apart before it fails a run.
        var report = Report() with
        {
            Path = new NetworkPath(0, 0, 0, 0, ThroughputMeasured: false),
        };

        var json = CapabilityReportJson.Render(report, TimeSpan.Zero);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("path").GetProperty("throughputMeasured")
            .GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void Json_SaysWhetherEncodersWereProbedRatherThanReportingNone()
    {
        // Same distinction on the host side: "no encoder" and "not looked" are different verdicts.
        var report = Report() with
        {
            Host = new HostCapabilities([], [], 0, 26100, EncodersProbed: false),
        };

        var json = CapabilityReportJson.Render(report, TimeSpan.Zero);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("host").GetProperty("encodersProbed")
            .GetBoolean().ShouldBeFalse();
    }

    private static CapabilityReport Report() => new(
        new FireTvDevice(IPAddress.Parse("10.46.161.42"), "Living Room", DiscoverySource.Manual),
        new HostCapabilities([], [], 0, 26100),
        new NetworkPath(4.2, 0.5, 120.0, 0.0),
        [new ModeVerdict(CastMode.Mirror, ModeStatus.Available, "Ready.")]);
}
