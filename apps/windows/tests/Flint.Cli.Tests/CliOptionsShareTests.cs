using Flint.Core.Settings;
using Shouldly;

namespace Flint.Cli.Tests;

/// <summary>The options that shape a share, and <c>--list-displays</c>.</summary>
public sealed class CliOptionsShareTests
{
    private static readonly string[] Paired = ["--address", "10.46.161.42", "--pairing-code", "123456", "--mirror"];

    /// <summary>Every option that shapes a share, with each value it refuses, for the parser and for the exit code.</summary>
    public static TheoryData<string, string[]> Refused => new()
    {
        { "--display", ["--display", "0"] },
        { "--display", ["--display", "-1"] },
        { "--display", ["--display", "second"] },
        { "--display", ["--display"] },
        { "--display", ["--display", "1", "--display", "2"] },
        { "--mode", ["--mode", "fast"] },
        { "--mode", ["--mode", "custom"] },
        { "--mode", ["--mode"] },
        { "--mode", ["--mode", "game", "--mode", "movie"] },
        { "--fps", ["--fps", "25"] },
        { "--fps", ["--fps", "0"] },
        { "--fps", ["--fps", "sixty"] },
        { "--fps", ["--fps"] },
        { "--fps", ["--fps", "30", "--fps", "60"] },
        { "--bitrate", ["--bitrate", "1"] },
        { "--bitrate", ["--bitrate", "31"] },
        { "--bitrate", ["--bitrate", "8M"] },
        { "--bitrate", ["--bitrate"] },
        { "--bitrate", ["--bitrate", "8", "--bitrate", "9"] },
    };

    [Fact]
    public void EverySharingOption_IsCarriedIntoTheShare()
    {
        CliOptions.TryParse(
            [.. Paired, "--display", "2", "--mode", "GAME", "--fps", "24", "--bitrate", "8", "--mirror-width", "1280", "--no-sound", "--no-pointer"],
            out var options,
            out var error).ShouldBeTrue(error);

        options!.Mirror.ShouldBe(new MirrorChoices(2, PictureMode.Game, 24, 8, 1280, Sound: false, Pointer: false));
    }

    [Fact]
    public void AShareWithNoOptions_IsBalanced_OnTheMainDisplay_WithSound()
    {
        CliOptions.TryParse(Paired, out var options, out var error).ShouldBeTrue(error);

        options!.Mirror.ShouldBe(new MirrorChoices());
        options.ListDisplays.ShouldBeFalse();
    }

    [Theory]
    [InlineData("15")]
    [InlineData("24")]
    [InlineData("30")]
    [InlineData("60")]
    public void EachFrameRateTheAppOffers_IsAccepted(string rate)
    {
        CliOptions.TryParse([.. Paired, "--fps", rate], out var options, out var error).ShouldBeTrue(error);

        options!.Mirror!.FrameRate.ShouldBe(int.Parse(rate, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("30")]
    public void TheDataRatesAtEitherEnd_AreAccepted(string megabits)
    {
        CliOptions.TryParse([.. Paired, "--bitrate", megabits], out var options, out var error).ShouldBeTrue(error);

        options!.Mirror!.MegabitsPerSecond.ShouldBe(int.Parse(megabits, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public void AValueOutsideWhatTheAppOffers_IsRefused_ByName(string option, string[] arguments)
    {
        CliOptions.TryParse([.. Paired, .. arguments], out var options, out var error).ShouldBeFalse();

        options.ShouldBeNull();
        error.ShouldNotBeNull();
        error.ShouldStartWith(option + " must be followed by");
    }

    [Fact]
    public void TheRefusals_SayWhatWouldBeAccepted()
    {
        Refusal(["--mode", "fast"]).ShouldBe("--mode must be followed by one of balanced, movie, game, text or data-saver.");
        Refusal(["--fps", "25"]).ShouldBe("--fps must be followed by 15, 24, 30 or 60.");
        Refusal(["--bitrate", "31"]).ShouldBe("--bitrate must be followed by megabits a second, from 2 to 30.");
        Refusal(["--display", "0"]).ShouldBe("--display must be followed by one display number, as --list-displays shows them.");
    }

    [Theory]
    [InlineData("--display", new[] { "--display", "1" })]
    [InlineData("--mode", new[] { "--mode", "game" })]
    [InlineData("--fps", new[] { "--fps", "30" })]
    [InlineData("--bitrate", new[] { "--bitrate", "8" })]
    [InlineData("--no-sound", new[] { "--no-sound" })]
    [InlineData("--no-pointer", new[] { "--no-pointer" })]
    [InlineData("--mirror-width", new[] { "--mirror-width", "1280" })]
    [InlineData("--fps", new[] { "--fps", "30", "--display", "1" })]
    public void ASharingOption_WithoutAShare_IsRefused_RatherThanIgnored(string first, string[] arguments)
    {
        CliOptions.TryParse(
            ["--address", "10.46.161.42", "--pairing-code", "123456", .. arguments],
            out _,
            out var error).ShouldBeFalse();

        error.ShouldBe($"{first} only applies with --mirror.");
    }

    [Fact]
    public void ListingDisplays_StandsAlone()
    {
        CliOptions.TryParse(["--LIST-DISPLAYS"], out var options, out var error).ShouldBeTrue(error);

        options!.ListDisplays.ShouldBeTrue();
        options.Mirror.ShouldBeNull();
        options.Endpoint.ShouldBeNull();
        options.Json.ShouldBeFalse();
    }

    [Theory]
    [InlineData("--list-displays --json")]
    [InlineData("--services --list-displays")]
    [InlineData("doctor --list-displays")]
    [InlineData("--address 10.46.161.42 --list-displays")]
    public void ListingDisplays_WithAnythingElse_IsRefused(string commandLine)
    {
        CliOptions.TryParse(commandLine.Split(' '), out _, out var error).ShouldBeFalse();

        error.ShouldBe("--list-displays takes no other options.");
    }

    [Fact]
    public void Completion_OffersEveryNewOption()
    {
        foreach (var option in new[] { "--display", "--mode", "--fps", "--bitrate", "--no-sound", "--no-pointer", "--list-displays" })
        {
            CliCompletion.PowerShellScript.ShouldContain($"'{option}'");
        }
    }

    private static string? Refusal(string[] arguments)
    {
        CliOptions.TryParse([.. Paired, .. arguments], out _, out var error).ShouldBeFalse();
        return error;
    }
}
