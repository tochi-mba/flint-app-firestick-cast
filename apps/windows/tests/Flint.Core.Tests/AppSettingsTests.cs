using Flint.Core.Settings;
using Shouldly;

namespace Flint.Core.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void DefaultsMatchTheCatalogue()
    {
        var value = AppSettings.Default;
        value.SchemaVersion.ShouldBe(AppSettings.CurrentSchemaVersion);
        value.General.ShouldBe(new GeneralSettings());
        value.Screen.ShouldBe(new ScreenSettings());
        value.Media.ShouldBe(new MediaSettings());
        value.Shortcuts.ShouldBe(new ShortcutSettings());
        value.Tray.ShouldBe(new TraySettings());
    }

    [Fact]
    public void NormalizeClampsEveryNumericKindAndRepairsEnums()
    {
        var damaged = new AppSettings
        {
            General = new GeneralSettings { InterfaceScalePercent = 126, ReconnectSeconds = 9999, AfterReconnect = (ReconnectOutcome)99 },
            Screen = new ScreenSettings
            {
                CustomFramesPerSecond = 28,
                CustomMegabitsPerSecond = -4,
                CountdownSeconds = 4,
                SoundKbps = 180,
                SoundDelayMilliseconds = 900,
                PictureMode = (PictureMode)99,
            },
            Media = new MediaSettings
            {
                SkipBackSeconds = -1,
                SkipForwardSeconds = 50,
                VolumeStepPercent = 0,
                PictureSeconds = 99,
                Repeat = (RepeatMode)99,
            },
            Shortcuts = new ShortcutSettings { Disconnect = "  Ctrl+D  ", ShowWindow = " " },
        }.Normalize();

        damaged.General.InterfaceScalePercent.ShouldBe(130);
        damaged.General.ReconnectSeconds.ShouldBe(600);
        damaged.General.AfterReconnect.ShouldBe(ReconnectOutcome.Ask);
        damaged.Screen.CustomFramesPerSecond.ShouldBe(30);
        damaged.Screen.CustomMegabitsPerSecond.ShouldBe(2);
        damaged.Screen.CountdownSeconds.ShouldBe(3);
        damaged.Screen.SoundKbps.ShouldBe(192);
        damaged.Screen.SoundDelayMilliseconds.ShouldBe(500);
        damaged.Screen.PictureMode.ShouldBe(PictureMode.Balanced);
        damaged.Media.SkipBackSeconds.ShouldBe(5);
        damaged.Media.SkipForwardSeconds.ShouldBe(60);
        damaged.Media.VolumeStepPercent.ShouldBe(1);
        damaged.Media.PictureSeconds.ShouldBe(60);
        damaged.Media.Repeat.ShouldBe(RepeatMode.Off);
        damaged.Shortcuts.Disconnect.ShouldBe("Ctrl+D");
        damaged.Shortcuts.ShowWindow.ShouldBeNull();
    }

    [Fact]
    public void NormalizeIsIdempotent() =>
        new AppSettings { General = new GeneralSettings { InterfaceScalePercent = 108 } }
            .Normalize().Normalize().ShouldBe(new AppSettings { General = new GeneralSettings { InterfaceScalePercent = 108 } }.Normalize());

    [Fact]
    public void JsonRoundTripsEverySectionAndIgnoresNewerFields()
    {
        var value = new AppSettings
        {
            General = new GeneralSettings { AskBeforeSwitching = false, InterfaceScalePercent = 115 },
            Screen = new ScreenSettings { ShowLiveNumbers = true, SoundDelayMilliseconds = 77 },
            Media = new MediaSettings { Shuffle = true, VolumeStepPercent = 7 },
            Shortcuts = new ShortcutSettings { Disconnect = "Ctrl+D" },
            Tray = new TraySettings { ShowIcon = false },
        };
        var text = AppSettingsJson.Serialize(value).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99,\n  \"future\": true");

        var parsed = AppSettingsJson.Parse(text);

        parsed.ShouldBe(value.Normalize());
        parsed.SchemaVersion.ShouldBe(AppSettings.CurrentSchemaVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void BadOuterJsonReturnsDefaults(string? text) => AppSettingsJson.Parse(text).ShouldBe(AppSettings.Default);
}
