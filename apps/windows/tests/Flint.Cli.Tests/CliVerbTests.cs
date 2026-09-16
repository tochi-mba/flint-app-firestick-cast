using Flint.Platform.Windows;
using Shouldly;

namespace Flint.Cli.Tests;

/// <summary>The words `flint` accepts in front of its options, and what they do.</summary>
public sealed class CliVerbTests
{
    [Fact]
    public void VersionAsAWordMeansTheSameAsTheOption()
    {
        CliOptions.TryParse(["version"], out var options, out var error).ShouldBeTrue();

        error.ShouldBeNull();
        options.ShouldNotBeNull();
        options.Verb.ShouldBe(CliVerb.Version);
        options.ShowVersion.ShouldBeTrue();
    }

    [Fact]
    public void DoctorProbesExactlyAsNoWordDoes()
    {
        CliOptions.TryParse(["doctor"], out var options, out _).ShouldBeTrue();

        options.ShouldNotBeNull();
        options.Verb.ShouldBe(CliVerb.Doctor);
        options.ShowVersion.ShouldBeFalse();
        options.Endpoint.ShouldBeNull();
        options.ScanServices.ShouldBeFalse();
    }

    [Fact]
    public void DoctorStillTakesTheProbeOptions()
    {
        CliOptions.TryParse(["doctor", "--address", "10.0.0.8", "--json"], out var options, out _)
            .ShouldBeTrue();

        options.ShouldNotBeNull();
        options.Verb.ShouldBe(CliVerb.Doctor);
        options.Json.ShouldBeTrue();
        options.Endpoint.ShouldNotBeNull();
    }

    [Fact]
    public void CompletionNamesTheOneShellItHas()
    {
        CliOptions.TryParse(["completion", "powershell"], out var options, out _).ShouldBeTrue();

        options.ShouldNotBeNull();
        options.Verb.ShouldBe(CliVerb.Completion);

        CliOptions.TryParse(["completion", "bash"], out _, out var error).ShouldBeFalse();
        error.ShouldNotBeNull();
        error!.ShouldContain("powershell");
    }

    [Fact]
    public void UpdateTakesNoOptions()
    {
        CliOptions.TryParse(["update"], out var options, out _).ShouldBeTrue();
        options.ShouldNotBeNull();
        options.Verb.ShouldBe(CliVerb.Update);

        CliOptions.TryParse(["update", "--json"], out _, out var error).ShouldBeFalse();
        error.ShouldNotBeNull();
        error!.ShouldContain("no other options");
    }

    [Fact]
    public void AnUnknownWordIsRejectedAndTheKnownOnesAreListed()
    {
        CliOptions.TryParse(["upgrade"], out var options, out var error).ShouldBeFalse();

        options.ShouldBeNull();
        error.ShouldNotBeNull();
        error!.ShouldContain("upgrade");
        error!.ShouldContain("update");
    }

    [Fact]
    public void TheOptionsThisCommandAlwaysTookAreUnchanged()
    {
        CliOptions.TryParse(
            ["--address", "10.0.0.8", "--pairing-code", "123456", "--mirror"],
            out var options,
            out var error).ShouldBeTrue();

        error.ShouldBeNull();
        options.ShouldNotBeNull();
        options.Verb.ShouldBe(CliVerb.None);
        options.Mirror.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateOnAPortableBuildSaysSoRatherThanDownloadingAnything()
    {
        var source = new FakeUpdateSource { IsInstalled = false, NewVersion = "0.2.0" };
        var output = new StringWriter();

        var code = await UpdateRunner.RunAsync(source, output, CancellationToken.None);

        code.ShouldBe(FlintExitCode.Success);
        output.ToString().ShouldContain("portable");
        source.Checks.ShouldBe(0);
        source.Applied.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateInstallsANewerBuildAndSaysSoBeforeRestarting()
    {
        var source = new FakeUpdateSource { IsInstalled = true, NewVersion = "0.2.0" };
        var output = new StringWriter();

        var code = await UpdateRunner.RunAsync(source, output, CancellationToken.None);

        code.ShouldBe(FlintExitCode.Success);
        output.ToString().ShouldContain("0.2.0");
        source.Applied.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateOnACurrentBuildAppliesNothing()
    {
        var source = new FakeUpdateSource { IsInstalled = true, NewVersion = null };
        var output = new StringWriter();

        await UpdateRunner.RunAsync(source, output, CancellationToken.None);

        output.ToString().ShouldContain("up to date");
        source.Applied.ShouldBeFalse();
    }

    private sealed class FakeUpdateSource : IUpdateSource
    {
        public bool IsInstalled { get; init; }

        public string? NewVersion { get; init; }

        public int Checks { get; private set; }

        public bool Applied { get; private set; }

        public Task<string?> CheckForNewVersionAsync(CancellationToken cancellationToken)
        {
            Checks++;
            return Task.FromResult(NewVersion);
        }

        public Task DownloadAsync(IProgress<int>? progress, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void ApplyAndRestart() => Applied = true;
    }
}
