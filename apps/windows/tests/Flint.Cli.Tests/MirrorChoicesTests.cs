using Flint.Core;
using Flint.Core.Settings;
using Shouldly;

namespace Flint.Cli.Tests;

/// <summary>Picking the display and the picture for a share, the way the app picks its own.</summary>
public sealed class MirrorChoicesTests
{
    private static readonly DisplayInfo Left =
        new(0, 1, "Display 1", "left", 0, 0, 1920, 1080, DisplayRotation.Upright, IsMain: false);

    private static readonly DisplayInfo Main =
        new(1, 2, "DELL U2720Q", "dell", 1920, 0, 2560, 1440, DisplayRotation.Upright, IsMain: true);

    [Fact]
    public void ByDefault_AShareIsBalanced_NoWiderThan1080p()
    {
        var choices = new MirrorChoices();

        var options = choices.Resolve(null, null);

        options.ShouldBe(new MirrorSessionOptions(0, 30, 12_000_000, 1920));
        choices.IsCustom.ShouldBeFalse();
        choices.Sound.ShouldBeTrue();
        choices.Describe(options).ShouldBe("For everyday use. Up to 1080p, 30 frames a second, 12 Mbps.");
    }

    [Fact]
    public void AMode_SendsItsOwnNumbers_FromTheChosenDisplay_NoWiderThanTheTv()
    {
        var options = new MirrorChoices(Mode: PictureMode.Movie).Resolve(Main, 1280);

        options.ShouldBe(new MirrorSessionOptions(1, 30, 20_000_000, 1280));
    }

    [Fact]
    public void NumbersGivenOnTheCommandLine_ReplaceTheModes_AsACustomPicture()
    {
        var choices = new MirrorChoices(Mode: PictureMode.DataSaver, FrameRate: 60, MegabitsPerSecond: 8, MaxWidth: 2560);

        var options = choices.Resolve(Main, 1920);

        options.ShouldBe(new MirrorSessionOptions(1, 60, 8_000_000, 2560), "a width asked for outright is not capped");
        choices.Describe(options).ShouldBe("Your own size, frame rate and data rate. Up to 1440p, 60 frames a second, 8 Mbps.");
    }

    [Theory]
    [InlineData(24, null, null)]
    [InlineData(null, 6, null)]
    [InlineData(null, null, 1280u)]
    public void AnyOneNumber_MakesThePictureCustom(int? frameRate, int? megabits, uint? width)
    {
        var choices = new MirrorChoices(FrameRate: frameRate, MegabitsPerSecond: megabits, MaxWidth: width);

        choices.IsCustom.ShouldBeTrue();
        var options = choices.Resolve(null, null);
        options.FrameRate.ShouldBe(frameRate is { } rate ? (uint)rate : 30u);
        options.BitrateBitsPerSecond.ShouldBe(megabits is { } data ? (uint)data * 1_000_000 : 12_000_000u);
        options.MaxWidth.ShouldBe(width ?? 1920u);
    }

    [Theory]
    [InlineData("balanced", PictureMode.Balanced)]
    [InlineData("MOVIE", PictureMode.Movie)]
    [InlineData("Game", PictureMode.Game)]
    [InlineData("text", PictureMode.TextAndSlides)]
    [InlineData("data-saver", PictureMode.DataSaver)]
    public void ModeNames_AreFound_WhateverTheirCase(string name, PictureMode mode) =>
        MirrorChoices.FindMode(name).ShouldBe(mode);

    [Theory]
    [InlineData("custom")]
    [InlineData("datasaver")]
    [InlineData("")]
    [InlineData(null)]
    public void OtherNames_AreNotModes(string? name) =>
        MirrorChoices.FindMode(name).ShouldBeNull();

    [Fact]
    public void EveryModeWithFixedNumbers_HasAName_InTheAppsOrder() =>
        MirrorChoices.ModeNames.Select(named => named.Mode).ShouldBe(ScreenQualityPreset.FixedModes);

    [Fact]
    public void WithoutADisplayNumber_TheMainDisplayIsShared_AsInTheApp()
    {
        new MirrorChoices().TryChooseDisplay([Left, Main], out var display, out var error).ShouldBeTrue();

        display.ShouldBe(Main);
        error.ShouldBeNull();
    }

    [Fact]
    public void WithNoDisplaysListed_CaptureIsLeftToFindOne()
    {
        new MirrorChoices().TryChooseDisplay([], out var display, out var error).ShouldBeTrue();

        display.ShouldBeNull();
        error.ShouldBeNull();
    }

    [Fact]
    public void ADisplayNumber_PicksThatDisplay()
    {
        new MirrorChoices(Display: 1).TryChooseDisplay([Left, Main], out var display, out var error).ShouldBeTrue();

        display.ShouldBe(Left);
        error.ShouldBeNull();
    }

    [Fact]
    public void ADisplayThatIsNotConnected_IsNamed_WithWhereToLook()
    {
        new MirrorChoices(Display: 3).TryChooseDisplay([Left, Main], out var display, out var error).ShouldBeFalse();

        display.ShouldBeNull();
        error.ShouldBe("There is no display 3. This PC has 2, and flint --list-displays names them.");
    }

    [Fact]
    public void ADisplayNumber_WhenNoneCouldBeListed_SaysSo()
    {
        new MirrorChoices(Display: 1).TryChooseDisplay([], out var display, out var error).ShouldBeFalse();

        display.ShouldBeNull();
        error.ShouldBe("--display has nothing to choose from: no displays could be listed on this PC.");
    }
}
