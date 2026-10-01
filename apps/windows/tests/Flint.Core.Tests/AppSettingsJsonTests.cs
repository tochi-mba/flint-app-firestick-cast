using Flint.Core.Settings;
using Shouldly;

namespace Flint.Core.Tests;

/// <summary>The saved file: what it looks like, and what happens to each kind of damage.</summary>
public sealed class AppSettingsJsonTests
{
    [Fact]
    public void Choices_AreWrittenByName_SoTheFileReadsPlainly()
    {
        var text = AppSettingsJson.Serialize(new AppSettings
        {
            Screen = new ScreenSettings { PictureMode = PictureMode.TextAndSlides },
        });

        text.ShouldContain("\"pictureMode\": \"textAndSlides\"");
        text.ShouldNotContain("\"pictureMode\": 3");
    }

    [Fact]
    public void ChoicesWrittenAsNumbers_AreStillRead()
    {
        var parsed = AppSettingsJson.Parse("{\"schemaVersion\":1,\"screen\":{\"pictureMode\":2}}");

        parsed.Screen.PictureMode.ShouldBe(PictureMode.Game);
    }

    [Fact]
    public void ADamagedSection_CostsOnlyThatSection()
    {
        var parsed = AppSettingsJson.Parse(
            "{\"schemaVersion\":1,"
            + "\"general\":{\"keepAwake\":\"sometimes\"},"
            + "\"screen\":{\"pictureMode\":\"cinematic\"},"
            + "\"media\":{\"shuffle\":true}}");

        parsed.General.ShouldBe(new GeneralSettings());
        parsed.Screen.ShouldBe(new ScreenSettings());
        parsed.Media.Shuffle.ShouldBeTrue();
    }

    [Fact]
    public void ASectionThatIsNotAnObject_IsItsDefaults()
    {
        var parsed = AppSettingsJson.Parse("{\"schemaVersion\":1,\"general\":[],\"media\":{\"shuffle\":true}}");

        parsed.General.ShouldBe(new GeneralSettings());
        parsed.Media.Shuffle.ShouldBeTrue();
    }

    [Fact]
    public void NumbersWrittenAsText_AndNamesInAnyCase_AreRead()
    {
        var parsed = AppSettingsJson.Parse("{\"SCHEMAVERSION\":1,\"General\":{\"InterfaceScalePercent\":\"115\"}}");

        parsed.General.InterfaceScalePercent.ShouldBe(115);
    }

    [Fact]
    public void Serialize_RefusesNothingToWrite() =>
        Should.Throw<ArgumentNullException>(() => AppSettingsJson.Serialize(null!));

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("not json", false)]
    [InlineData("[]", false)]
    [InlineData("{\"general\":{}}", false)]
    [InlineData("{\"schemaVersion\":1}", true)]
    [InlineData("{\"schemaVersion\":99,\"general\":{\"keepAwake\":\"broken\"}}", true)]
    public void LooksLikeSettings_ChecksOnlyTheOuterShape(string? text, bool expected) =>
        AppSettingsJson.LooksLikeSettings(text).ShouldBe(expected);
}
