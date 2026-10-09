using Shouldly;

namespace Flint.Cli.Tests;

/// <summary>Tests that swap the process's console, so they never run beside another test.</summary>
[CollectionDefinition(nameof(SharedConsole), DisableParallelization = true)]
public sealed class SharedConsole;

/// <summary>The whole command as a script sees it: its exit code and what it printed.</summary>
/// <remarks>Only command lines that end before anything is probed or connected, so no TV is needed.</remarks>
[Collection(nameof(SharedConsole))]
public sealed class ProgramTests
{
    private static readonly string[] Paired = ["--address", "10.46.161.42", "--pairing-code", "123456", "--mirror"];

    [Theory]
    [MemberData(nameof(CliOptionsShareTests.Refused), MemberType = typeof(CliOptionsShareTests))]
    public async Task EachRefusedValue_ExitsWithTheUsageCode_AndPrintsTheUsage(string option, string[] arguments)
    {
        var (exit, text) = await RunAsync([.. Paired, .. arguments]);

        exit.ShouldBe(FlintExitCode.UsageError);
        text.ShouldContain($"  {option} must be followed by");
        text.ShouldContain("Sharing options, with --mirror");
    }

    [Fact]
    public async Task ADisplayThatIsNotConnected_ExitsWithTheUsageCode_BeforeAnythingIsProbed()
    {
        var (exit, text) = await RunAsync([.. Paired, "--display", "99"]);

        exit.ShouldBe(FlintExitCode.UsageError);
        text.ShouldContain("display");
        text.ShouldNotContain("Capability probe");
    }

    [Fact]
    public async Task ListingDisplays_Answers_AndSucceeds()
    {
        var (exit, text) = await RunAsync(["--list-displays"]);

        exit.ShouldBe(FlintExitCode.Success);
        text.ShouldContain("  DISPLAYS");
        text.ShouldNotContain("Capability probe");
    }

    [Fact]
    public async Task Help_NamesEverySharingOption_AndWhatTheUsageCodeNowCovers()
    {
        var (exit, text) = await RunAsync(["--help"]);

        exit.ShouldBe(FlintExitCode.Success);
        foreach (var option in new[] { "--list-displays", "--display <number>", "--mode <name>", "--fps <15|24|30|60>", "--bitrate <2-30>", "--mirror-width <320-7680>", "--no-sound", "--no-pointer" })
        {
            text.ShouldContain(option);
        }

        text.ShouldContain("64  bad command line, or a --display that is not connected");
    }

    private static async Task<(int Exit, string Text)> RunAsync(string[] arguments)
    {
        var output = new StringWriter();
        var (was, wasError) = (Console.Out, Console.Error);
        Console.SetOut(output);
        Console.SetError(output);
        try
        {
            return (await Program.Main(arguments), output.ToString());
        }
        finally
        {
            Console.SetOut(was);
            Console.SetError(wasError);
        }
    }
}
