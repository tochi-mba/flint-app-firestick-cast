using Flint.App.ViewModels;
using Flint.Core;
using Flint.Core.Settings;
using Flint.Protocol;
using Flint.Session;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>The live counters, and the suggestion made when the TV cannot keep up.</summary>
public sealed partial class ScreenPageViewModelTests
{
    [Fact]
    public void TheFirstSample_ShowsADash_NotAWrongNumber()
    {
        using var screen = Page();

        screen.OnStats(new MirrorSessionStats(10, 0, 0, 125_000));

        screen.LiveFramesPerSecond.ShouldBe(ScreenPageViewModel.NoFigure);
        screen.LiveDataRate.ShouldBe(ScreenPageViewModel.NoFigure);
        screen.LiveRestarts.ShouldBe("0");
    }

    [Fact]
    public void Rates_AreWorkedOutFromTheCountersOverAtLeastASecond()
    {
        using var screen = Page();
        screen.OnStats(new MirrorSessionStats(10, 0, 0, 1_000_000));

        clock.Advance(TimeSpan.FromMilliseconds(500));
        screen.OnStats(new MirrorSessionStats(25, 0, 0, 1_500_000));
        screen.LiveFramesPerSecond.ShouldBe(ScreenPageViewModel.NoFigure, "half a second is too short to call a rate");

        clock.Advance(TimeSpan.FromMilliseconds(1500));
        screen.OnStats(new MirrorSessionStats(70, 0, 2, 4_000_000));

        screen.LiveFramesPerSecond.ShouldBe("30");
        screen.LiveDataRate.ShouldBe("12.0 Mbps");
        screen.LiveRestarts.ShouldBe("2");
    }

    [Theory]
    [InlineData(MirrorEncoderKind.Hardware, "Graphics card")]
    [InlineData(MirrorEncoderKind.Software, "Software, on the processor")]
    [InlineData(MirrorEncoderKind.Unknown, ScreenPageViewModel.NoFigure)]
    public void ThePictureAndItsEncoder_AreNamed(MirrorEncoderKind encoder, string expected)
    {
        using var screen = Page();

        screen.OnPictureStarted(new MirrorPicture(1280, 720, encoder));

        screen.LivePictureSize.ShouldBe("1280 × 720");
        screen.LiveEncoder.ShouldBe(expected);
    }

    [Fact]
    public void Struggling_IsSuggestedAfterTenSecondsOfDrops_NotBefore()
    {
        using var screen = Page();

        Drops(screen, 0);
        clock.Advance(TimeSpan.FromSeconds(1));
        Drops(screen, 5);
        for (var second = 1; second < 10; second++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            Drops(screen, 5 + (second * 5));
            screen.ShowStrugglingSuggestion.ShouldBeFalse($"only {second} seconds in");
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        Drops(screen, 60);

        screen.ShowStrugglingSuggestion.ShouldBeTrue();
        screen.LiveDroppedFrames.ShouldBe("60");
        screen.LiveTvQueue.ShouldBe("2");
    }

    [Fact]
    public void ABriefPauseInDropping_IsStillOneRun_ButALongOneEndsIt()
    {
        using var screen = Page();
        long[] samples = [0, 5, 10, 15, 20, 20, 20, 25, 30, 35, 40, 45];
        foreach (var dropped in samples)
        {
            Drops(screen, dropped);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Drops(screen, 50);
        screen.ShowStrugglingSuggestion.ShouldBeTrue("two seconds without a drop did not end the run");

        for (var quiet = 0; quiet < 4; quiet++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            Drops(screen, 50);
        }

        screen.ShowStrugglingSuggestion.ShouldBeFalse("four seconds without a drop did");
    }

    [Fact]
    public void DropsAfterAQuietSpell_StartANewRun()
    {
        using var screen = Page();
        Drops(screen, 0);
        clock.Advance(TimeSpan.FromSeconds(1));
        Drops(screen, 5);

        clock.Advance(TimeSpan.FromSeconds(30));
        Drops(screen, 6);

        screen.ShowStrugglingSuggestion.ShouldBeFalse("one drop half a minute later is not ten seconds of trouble");
    }

    [Fact]
    public void ADismissedSuggestion_StaysAwayForFiveMinutes()
    {
        using var screen = Page();
        Struggle(screen);
        screen.DismissSuggestionCommand.Execute(null);
        screen.ShowStrugglingSuggestion.ShouldBeFalse();

        clock.Advance(TimeSpan.FromMinutes(4));
        Struggle(screen);
        screen.ShowStrugglingSuggestion.ShouldBeFalse();

        clock.Advance(TimeSpan.FromMinutes(1));
        Struggle(screen);
        screen.ShowStrugglingSuggestion.ShouldBeTrue();
    }

    [Fact]
    public void TakingTheSuggestion_SwitchesToDataSaver_WhichIsNeverSuggestedAgain()
    {
        using var screen = Page();
        Struggle(screen);

        screen.UseDataSaverCommand.Execute(null);

        settings.Current.Screen.PictureMode.ShouldBe(PictureMode.DataSaver);
        screen.ShowStrugglingSuggestion.ShouldBeFalse();
        Struggle(screen);
        screen.ShowStrugglingSuggestion.ShouldBeFalse("there is nothing lighter to suggest");
    }

    [Fact]
    public void LiveNumbers_AreShownAsTheSettingsSay()
    {
        using var screen = Page();
        var raised = new List<string?>();
        screen.PropertyChanged += (_, change) => raised.Add(change.PropertyName);

        screen.ShowLiveNumbers = true;

        settings.Current.Screen.ShowLiveNumbers.ShouldBeTrue();
        screen.ShowLiveNumbers.ShouldBeTrue();
        raised.ShouldContain(nameof(ScreenPageViewModel.ShowLiveNumbers));
    }

    private static void Drops(ScreenPageViewModel screen, long dropped) =>
        screen.OnReceiverStats(new StatsMessage(2, 0, 0, dropped));

    /// <summary>Eleven seconds of the TV dropping frames, from wherever the clock now is.</summary>
    private void Struggle(ScreenPageViewModel screen)
    {
        var start = screen.LiveDroppedFrames == ScreenPageViewModel.NoFigure ? 0 : long.Parse(screen.LiveDroppedFrames, System.Globalization.CultureInfo.InvariantCulture);
        for (var second = 0; second <= 11; second++)
        {
            Drops(screen, start + 1 + second);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Drops(screen, start + 20);
    }
}
