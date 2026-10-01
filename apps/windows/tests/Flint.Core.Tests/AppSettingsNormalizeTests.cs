using Flint.Core.Settings;
using Shouldly;

namespace Flint.Core.Tests;

/// <summary>
/// Every way a saved value can be wrong, and the value each one becomes.
/// </summary>
/// <remarks>
/// The settings file is a thing people open in Notepad. Whatever they leave in it, the app must
/// only ever see values its own controls could have produced.
/// </remarks>
public sealed class AppSettingsNormalizeTests
{
    [Fact]
    public void MissingSections_BecomeTheirDefaults()
    {
        var normalized = new AppSettings
        {
            General = null!,
            Screen = null!,
            Media = null!,
            Shortcuts = null!,
            Tray = null!,
        }.Normalize();

        normalized.ShouldBe(AppSettings.Default);
    }

    [Fact]
    public void EveryChoiceOutsideItsEnum_BecomesItsDefault()
    {
        var normalized = new AppSettings
        {
            General = new GeneralSettings
            {
                AfterReconnect = (ReconnectOutcome)42,
                CloseWindow = (CloseWindowOutcome)42,
            },
            Screen = new ScreenSettings
            {
                Display = (ShareDisplayChoice)42,
                DisplayPrompt = (ShareDisplayPrompt)42,
                PictureMode = (PictureMode)42,
                CustomSizeLimit = (SizeLimit)42,
                PausedPicture = (PausedPicture)42,
                SoundDestination = (SoundDestination)42,
                SoundSource = (SoundSource)42,
            },
            Media = new MediaSettings
            {
                Repeat = (RepeatMode)42,
                PlayedBefore = (ResumeMode)42,
                DropOrder = (DropOrder)42,
                QueueEnd = (QueueEnd)42,
            },
        }.Normalize();

        normalized.ShouldBe(AppSettings.Default);
    }

    [Fact]
    public void EveryChoiceInsideItsEnum_IsKept()
    {
        var chosen = new AppSettings
        {
            General = new GeneralSettings { AfterReconnect = ReconnectOutcome.CarryOn, CloseWindow = CloseWindowOutcome.Quit },
            Screen = new ScreenSettings
            {
                Display = ShareDisplayChoice.Remembered,
                DisplayPrompt = ShareDisplayPrompt.AskEveryTime,
                PictureMode = PictureMode.Custom,
                CustomSizeLimit = SizeLimit.Native,
                PausedPicture = PausedPicture.Black,
                SoundDestination = SoundDestination.TvAndPc,
                SoundSource = SoundSource.NamedDevice,
            },
            Media = new MediaSettings
            {
                Repeat = RepeatMode.All,
                PlayedBefore = ResumeMode.StartOver,
                DropOrder = DropOrder.AsDropped,
                QueueEnd = QueueEnd.TvHome,
            },
        };

        chosen.Normalize().ShouldBe(chosen);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 6)]
    [InlineData(6, 6)]
    [InlineData(45, 45)]
    [InlineData(61, 60)]
    public void PictureTime_IsZeroOrInsideWhatTheTvCanHonour(int saved, int expected) =>
        new MediaSettings { PictureSeconds = saved }.Normalize().PictureSeconds.ShouldBe(expected);

    [Theory]
    [InlineData(29, 30)]
    [InlineData(30, 30)]
    [InlineData(600, 600)]
    [InlineData(601, 600)]
    public void ReconnectTime_StaysInsideItsRange(int saved, int expected) =>
        new GeneralSettings { ReconnectSeconds = saved }.Normalize().ReconnectSeconds.ShouldBe(expected);

    [Theory]
    [InlineData(107, 100)]
    [InlineData(108, 115)]
    [InlineData(0, 90)]
    [InlineData(int.MaxValue, 130)]
    [InlineData(int.MinValue, 90)]
    public void InterfaceSize_LandsOnTheNearestOffered_SmallerOnATie(int saved, int expected) =>
        new GeneralSettings { InterfaceScalePercent = saved }.Normalize().InterfaceScalePercent.ShouldBe(expected);

    [Fact]
    public void RememberedDeviceNames_AreTrimmed_AndBlankOnesForgotten()
    {
        var screen = new ScreenSettings { DisplayIdentity = "  \\\\.\\DISPLAY2  ", SoundDeviceIdentity = "   " }.Normalize();

        screen.DisplayIdentity.ShouldBe("\\\\.\\DISPLAY2");
        screen.SoundDeviceIdentity.ShouldBeNull();
    }

    [Fact]
    public void EveryShortcut_IsTrimmed_AndBlankOnesUnassigned()
    {
        var blank = new ShortcutSettings
        {
            StartStopSharing = " ",
            PauseResumeSharing = "",
            PlayPauseTv = "\t",
            NextItem = " Ctrl+N ",
            PreviousItem = null,
            VolumeUp = " ",
            VolumeDown = " ",
            ShowWindow = " ",
            Disconnect = " ",
        }.Normalize();

        blank.ShouldBe(new ShortcutSettings
        {
            StartStopSharing = null,
            PauseResumeSharing = null,
            PlayPauseTv = null,
            NextItem = "Ctrl+N",
            PreviousItem = null,
            VolumeUp = null,
            VolumeDown = null,
            ShowWindow = null,
            Disconnect = null,
        });
    }

    [Fact]
    public void ChangedEvent_RefusesAMissingSide()
    {
        Should.Throw<ArgumentNullException>(() => new SettingsChangedEventArgs(null!, AppSettings.Default));
        Should.Throw<ArgumentNullException>(() => new SettingsChangedEventArgs(AppSettings.Default, null!));
    }
}
