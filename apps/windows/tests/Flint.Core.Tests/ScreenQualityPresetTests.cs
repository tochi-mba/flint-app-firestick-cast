using Flint.Core.Settings;
using Shouldly;

namespace Flint.Core.Tests;

/// <summary>The picture modes' table, the sizes it allows, and the sentence that describes each.</summary>
public sealed class ScreenQualityPresetTests
{
    [Theory]
    [InlineData(PictureMode.Balanced, SizeLimit.P1080, 30, 12)]
    [InlineData(PictureMode.Movie, SizeLimit.P1080, 30, 20)]
    [InlineData(PictureMode.Game, SizeLimit.P1080, 60, 16)]
    [InlineData(PictureMode.TextAndSlides, SizeLimit.P1080, 15, 12)]
    [InlineData(PictureMode.DataSaver, SizeLimit.P720, 30, 4)]
    public void EachMode_SendsWhatTheTableSays(PictureMode mode, SizeLimit size, int fps, int mbps)
    {
        var settings = new ScreenSettings { PictureMode = mode, CustomFramesPerSecond = 24, CustomMegabitsPerSecond = 2 };

        ScreenQualityPreset.Resolve(settings).ShouldBe(new ScreenQuality(size, fps, mbps), "custom values do not leak into a fixed mode");
    }

    [Fact]
    public void Custom_SendsThePersonsOwnValues()
    {
        var settings = new ScreenSettings
        {
            PictureMode = PictureMode.Custom,
            CustomSizeLimit = SizeLimit.Native,
            CustomFramesPerSecond = 24,
            CustomMegabitsPerSecond = 9,
        };

        ScreenQualityPreset.Resolve(settings).ShouldBe(new ScreenQuality(SizeLimit.Native, 24, 9));
        ScreenQualityPreset.FixedModes.ShouldNotContain(PictureMode.Custom);
        ScreenQualityPreset.FixedModes.Count.ShouldBe(5);
    }

    [Fact]
    public void CustomValuesOutsideTheirRange_AreClampedBeforeTheyAreSent()
    {
        var settings = new AppSettings
        {
            Screen = new ScreenSettings { PictureMode = PictureMode.Custom, CustomFramesPerSecond = 500, CustomMegabitsPerSecond = 900 },
        }.Normalize().Screen;

        var options = ScreenQualityPreset.ToOptions(settings, 0, tvWidth: null, displayWidth: null);

        options.FrameRate.ShouldBe(60u);
        options.BitrateBitsPerSecond.ShouldBe((uint)ScreenSettings.MaximumMegabitsPerSecond * 1_000_000);
    }

    [Theory]
    [InlineData(SizeLimit.P720, null, null, 1280u)]
    [InlineData(SizeLimit.P1080, null, null, 1920u)]
    [InlineData(SizeLimit.P1080, 1280, null, 1280u)]
    [InlineData(SizeLimit.P1080, 3840, null, 1920u)]
    [InlineData(SizeLimit.MatchTv, 1280, null, 1280u)]
    [InlineData(SizeLimit.MatchTv, 3840, null, 3840u)]
    [InlineData(SizeLimit.MatchTv, null, null, 1920u)]
    [InlineData(SizeLimit.MatchTv, 1, null, 1920u)]
    [InlineData(SizeLimit.Native, null, 2560, 2560u)]
    [InlineData(SizeLimit.Native, 1920, 2560, 1920u)]
    [InlineData(SizeLimit.Native, 1920, null, 1920u)]
    [InlineData(SizeLimit.Native, null, null, 0u)]
    [InlineData(SizeLimit.Native, null, 0, 0u)]
    public void ThePictureIsNeverWiderThanTheTv(SizeLimit limit, int? tvWidth, int? displayWidth, uint expected)
    {
        ScreenQualityPreset.MaxWidth(limit, tvWidth, displayWidth).ShouldBe(expected);
    }

    [Fact]
    public void Options_CarryTheDisplayTheModeAndTheTvsSize()
    {
        var options = ScreenQualityPreset.ToOptions(new ScreenSettings { PictureMode = PictureMode.Game }, 2, tvWidth: 1280, displayWidth: 2560);

        options.ShouldBe(new MirrorSessionOptions(2, 60, 16_000_000, 1280));
    }

    [Fact]
    public void EachSentence_SaysWhatTheModeIsForAndTheNumbersItSends()
    {
        foreach (var mode in Enum.GetValues<PictureMode>())
        {
            var options = ScreenQualityPreset.ToOptions(new ScreenSettings { PictureMode = mode }, 0, null, null);

            var sentence = ScreenQualityPreset.Describe(mode, options);

            sentence.ShouldContain($"{options.FrameRate} frames a second");
            sentence.ShouldContain($"{options.BitrateBitsPerSecond / 1_000_000} Mbps");
            foreach (var unmeasured in new[] { "latency", "delay", "lag" })
            {
                sentence.ShouldNotContain(unmeasured, Case.Insensitive);
            }
        }

        ScreenQualityPreset.Describe(PictureMode.Balanced, new MirrorSessionOptions(0, 30, 12_000_000, 1920))
            .ShouldBe("For everyday use. Up to 1080p, 30 frames a second, 12 Mbps.");
        ScreenQualityPreset.Describe(PictureMode.Custom, new MirrorSessionOptions(0, 24, 9_000_000, 0))
            .ShouldBe("Your own size, frame rate and data rate. Up to the display's own size, 24 frames a second, 9 Mbps.");
        Should.Throw<ArgumentNullException>(() => ScreenQualityPreset.Describe(PictureMode.Movie, null!));
        Should.Throw<ArgumentNullException>(() => ScreenQualityPreset.Resolve(null!));
    }

    [Theory]
    [InlineData(1280u, "720p")]
    [InlineData(2560u, "1440p")]
    [InlineData(3840u, "4K")]
    [InlineData(1366u, "1366 pixels wide")]
    public void Widths_AreNamedAsPeopleNameThem(uint width, string expected)
    {
        ScreenQualityPreset.DescribeWidth(width).ShouldBe(expected);
    }
}
