using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Flint.App.ViewModels;
using Flint.App.ViewModels.Settings;
using Flint.App.Views;
using Flint.App.Services;
using Flint.Core;
using Flint.Core.Settings;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>The Screen sharing section of Settings, and the one setting the window itself carries out.</summary>
public sealed class ScreenSettingsSectionTests : IDisposable
{
    private readonly SettingsService service = new(new InMemoryAppSettingsStore());

    public void Dispose() => service.Dispose();

    [Fact]
    public void EveryChoice_ReadsAndWritesItsSetting()
    {
        var screen = new ScreenSettingsViewModel(service);

        screen.Display!.Label.ShouldBe("The main display");
        screen.Prompt!.Label.ShouldBe("Use the last display");
        screen.Picture!.Label.ShouldBe("Balanced");
        screen.Countdown!.Label.ShouldBe("Off");
        screen.CustomSize!.Label.ShouldBe("1080p");
        screen.CustomFrames!.Label.ShouldBe("30 a second");

        screen.Display = screen.DisplayChoices[1];
        screen.Prompt = screen.PromptChoices[1];
        screen.Picture = screen.PictureChoices.Single(choice => choice.Value is PictureMode.Custom);
        screen.CustomSize = screen.SizeChoices.Single(choice => choice.Value is SizeLimit.MatchTv);
        screen.CustomFrames = screen.FrameChoices.Single(choice => choice.Value == 60);
        screen.CustomRate = 17.4;
        screen.Countdown = screen.CountdownChoices.Single(choice => choice.Value == 3);
        screen.ShowLiveNumbers = true;
        screen.MinimiseWhenSharing = true;

        var saved = service.Current.Screen;
        saved.Display.ShouldBe(ShareDisplayChoice.Remembered);
        saved.DisplayPrompt.ShouldBe(ShareDisplayPrompt.AskEveryTime);
        saved.PictureMode.ShouldBe(PictureMode.Custom);
        saved.CustomSizeLimit.ShouldBe(SizeLimit.MatchTv);
        saved.CustomFramesPerSecond.ShouldBe(60);
        saved.CustomMegabitsPerSecond.ShouldBe(17);
        saved.CountdownSeconds.ShouldBe(3);
        saved.ShowLiveNumbers.ShouldBeTrue();
        saved.MinimiseWhenSharing.ShouldBeTrue();
        screen.IsCustom.ShouldBeTrue();
        screen.CustomRate.ShouldBe(17);
        screen.ShowLiveNumbers.ShouldBeTrue();
        screen.MinimiseWhenSharing.ShouldBeTrue();
        screen.Countdown!.Label.ShouldBe("3 seconds");
    }

    [Fact]
    public void ThePauseSettings_ReadAndWriteTheirValues()
    {
        var screen = new ScreenSettingsViewModel(service);
        screen.PausedPictureChoice!.Label.ShouldBe("The last picture");
        screen.PauseWhenLocked.ShouldBeTrue();
        screen.StayPausedAfterUnlock.ShouldBeFalse();

        screen.PausedPictureChoice = screen.PausedPictureChoices[1];
        screen.PauseWhenLocked = false;
        screen.StayPausedAfterUnlock = true;
        screen.PausedPictureChoice = null;

        service.Current.Screen.PausedPicture.ShouldBe(PausedPicture.Black);
        service.Current.Screen.PauseWhenLocked.ShouldBeFalse();
        service.Current.Screen.StayPausedAfterUnlock.ShouldBeTrue();
        screen.StayPausedAfterUnlock.ShouldBeTrue();
        screen.Settings.Single(setting => setting.Matches("locks")).ShouldBe(screen.PauseWhenLockedText);
    }

    [Fact]
    public void TheWindowTitle_SaysWhatIsOnTheTv()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var raised = new List<string?>();
        shell.PropertyChanged += (_, change) => raised.Add(change.PropertyName);

        shell.WindowTitle.ShouldBe("Flint - REX Technologies");
        shell.Cast.IsMirroring = true;
        shell.WindowTitle.ShouldBe("Flint - sharing your screen");
        shell.Cast.MirrorPause = Flint.Core.MirrorPause.HoldingLastPicture;
        shell.WindowTitle.ShouldBe("Flint - sharing paused");
        shell.Cast.MirrorPause = Flint.Core.MirrorPause.Running;
        shell.Cast.IsMirroring = false;
        shell.Cast.IsReconnecting = true;
        shell.WindowTitle.ShouldBe("Flint - reconnecting");

        raised.Count(name => name == nameof(MainWindowViewModel.WindowTitle)).ShouldBe(5);
    }

    [Fact]
    public void AnEmptyChoice_ChangesNothing()
    {
        var screen = new ScreenSettingsViewModel(service);

        screen.Display = null;
        screen.Prompt = null;
        screen.Picture = null;
        screen.CustomSize = null;
        screen.CustomFrames = null;
        screen.Countdown = null;

        service.Current.Screen.ShouldBe(new ScreenSettings());
    }

    [Fact]
    public void AChangeMadeElsewhere_IsShownHere_AndResetPutsEverythingBack()
    {
        var screen = new ScreenSettingsViewModel(service);
        var raised = new List<string?>();
        screen.PropertyChanged += (_, change) => raised.Add(change.PropertyName);

        service.Update(current => current with { Screen = current.Screen with { PictureMode = PictureMode.Game } });
        service.Update(current => current with { Media = current.Media with { AutoPlayNext = false } });

        raised.ShouldBe([string.Empty], "only a Screen sharing change refreshes this section");
        screen.Picture!.Value.ShouldBe(PictureMode.Game);

        screen.Reset.AskCommand.Execute(null);
        screen.Reset.ConfirmCommand.Execute(null);
        service.Current.Screen.ShouldBe(new ScreenSettings());
        Should.Throw<ArgumentNullException>(() => new ScreenSettingsViewModel(null!));
    }

    [Fact]
    public void EverySetting_CanBeFoundBySearching()
    {
        var screen = new ScreenSettingsViewModel(service);

        screen.Title.ShouldBe("Screen sharing");
        screen.Settings.Count.ShouldBe(15);
        screen.Settings.Single(setting => setting.Matches("minimise")).ShouldBe(screen.MinimiseText);
        screen.Settings.Count(setting => setting.Matches("countdown") || setting.Matches("count down")).ShouldBe(1);
    }

    [AvaloniaFact]
    public void TheSection_ShowsTheCustomRowsOnlyForCustom()
    {
        var screen = new ScreenSettingsViewModel(service);
        var section = new ScreenSettingsSection { DataContext = screen };
        var window = new Window { Width = 900, Height = 900, Content = section };
        try
        {
            window.Show();
            window.UpdateLayout();
            Shown(section.FindControl<ComboBox>("CustomSize")!).ShouldBeFalse();

            screen.Picture = screen.PictureChoices.Single(choice => choice.Value is PictureMode.Custom);
            window.UpdateLayout();

            Shown(section.FindControl<ComboBox>("CustomSize")!).ShouldBeTrue();
            section.FindControl<Slider>("CustomRate")!.Maximum.ShouldBe(ScreenSettings.MaximumMegabitsPerSecond);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StartingAShare_MinimisesTheWindow_WhenTheSettingSaysTo()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var window = new MainWindow { DataContext = shell, Width = 1180, Height = 780 };
        var requests = 0;
        shell.MinimiseRequested += (_, _) => requests++;
        try
        {
            window.Show();
            shell.Cast.IsMirroring = true;
            shell.Cast.IsMirroring = false;
            requests.ShouldBe(0, "the setting is off");

            shell.Settings.Sections.OfType<ScreenSettingsViewModel>().Single().MinimiseWhenSharing = true;
            shell.Cast.IsMirroring = true;
            requests.ShouldBe(1);
            window.WindowState.ShouldBe(WindowState.Minimized);

            shell.Cast.IsMirroring = false;
            requests.ShouldBe(1, "a share ending does not minimise anything");
            window.DataContext = null;
            shell.Cast.IsMirroring = true;
            requests.ShouldBe(2, "the shell still asks");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void AShareStarting_WithNothingToMinimise_IsFine()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        shell.Settings.Sections.OfType<ScreenSettingsViewModel>().Single().MinimiseWhenSharing = true;

        Should.NotThrow(() => shell.Cast.IsMirroring = true);
    }

    private static bool Shown(Control control) => TopLevel.GetTopLevel(control) is not null && control.IsEffectivelyVisible;

    [Fact]
    public void TheSoundSettings_ReadAndWriteTheirValues()
    {
        var outputs = new List<AudioDevice> { new("speakers", "Speakers", true), new("usb", "USB headset", false) };
        var screen = new ScreenSettingsViewModel(service, () => outputs);

        screen.SoundSourceChoices.Select(choice => choice.Label).ShouldBe(["The output Windows plays through", "Speakers", "USB headset"]);
        screen.SoundSource!.Value.ShouldBeNull();
        screen.SoundQuality!.Label.ShouldBe("128 kbps");
        screen.SoundQualityChoices.Select(choice => choice.Label).ShouldBe(["96 kbps, the least data", "128 kbps", "160 kbps", "192 kbps, the best"]);
        screen.SoundDelay.ShouldBe(0);

        screen.SoundSource = screen.SoundSourceChoices[2];
        screen.SoundQuality = screen.SoundQualityChoices[3];
        screen.SoundDelay = 84.6;

        var saved = service.Current.Screen;
        saved.SoundSource.ShouldBe(SoundSource.NamedDevice);
        saved.SoundDeviceIdentity.ShouldBe("usb");
        saved.SoundKbps.ShouldBe(192);
        saved.SoundDelayMilliseconds.ShouldBe(85);
        screen.SoundSource!.Label.ShouldBe("USB headset");

        screen.SoundSource = screen.SoundSourceChoices[0];
        service.Current.Screen.SoundSource.ShouldBe(SoundSource.DefaultOutput);
        service.Current.Screen.SoundDeviceIdentity.ShouldBe("usb", "remembered, should it be chosen again");
        ScreenSettingsViewModel.MaximumSoundDelay.ShouldBe(ScreenSettings.MaximumSoundDelayMilliseconds);
    }

    [Fact]
    public void AChosenOutputThatIsNotConnected_StaysInTheList_AndTheListIsReadAgainAfterAChange()
    {
        var outputs = new List<AudioDevice> { new("speakers", "Speakers", true) };
        service.Update(current => current with { Screen = current.Screen with { SoundSource = SoundSource.NamedDevice, SoundDeviceIdentity = "usb" } });
        var screen = new ScreenSettingsViewModel(service, () => outputs);

        screen.SoundSource!.Label.ShouldBe("An output that is not connected");
        outputs.Add(new("usb", "USB headset", false));
        screen.SoundSource!.Label.ShouldBe("An output that is not connected", "read once until something changes");

        screen.SoundDelay = 20;

        screen.SoundSource!.Label.ShouldBe("USB headset");
        new ScreenSettingsViewModel(service).SoundSourceChoices.Count.ShouldBe(2, "with no outputs listed, only the default and the chosen one");
    }

    [Fact]
    public void TheSoundSettings_CanBeFound_ByWhatTheyDo()
    {
        var screen = new ScreenSettingsViewModel(service);

        screen.Settings.Where(setting => setting.Matches("lips")).ShouldHaveSingleItem().ShouldBe(screen.SoundDelayText);
        screen.Settings.ShouldContain(screen.SoundSourceText);
        screen.Settings.ShouldContain(screen.SoundQualityText);
    }

    [AvaloniaFact]
    public void TheSection_ShowsTheSoundRows()
    {
        var screen = new ScreenSettingsViewModel(service, () => [new("speakers", "Speakers", true)]);
        var section = new ScreenSettingsSection { DataContext = screen };
        var window = new Window { Width = 900, Height = 900, Content = section };
        try
        {
            window.Show();
            window.UpdateLayout();

            section.FindControl<ComboBox>("SoundSource")!.SelectedItem.ShouldBe(screen.SoundSourceChoices[0]);
            section.FindControl<ComboBox>("SoundQuality")!.SelectedItem.ShouldBe(screen.SoundQuality);
            section.FindControl<Slider>("SoundDelay")!.Maximum.ShouldBe(ScreenSettings.MaximumSoundDelayMilliseconds);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AMuteLeftBehind_IsToldOnce_AndPutAway()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var window = new MainWindow { DataContext = shell, Width = 1180, Height = 780 };
        try
        {
            window.Show();
            shell.ShowLaunchNotice.ShouldBeFalse();
            shell.TellLeftOverMute(LeftOverMute.None);
            shell.ShowLaunchNotice.ShouldBeFalse();
            shell.TellLeftOverMute(LeftOverMute.OutputGone);
            shell.ShowLaunchNotice.ShouldBeFalse("an output that has gone is forgotten quietly");

            shell.TellLeftOverMute(LeftOverMute.Restored);
            shell.LaunchNotice.ShouldBe("Flint closed last time while this PC was muted for the TV. Its sound is back as it was.");
            window.UpdateLayout();
            window.GetVisualDescendants().OfType<TextBlock>().ShouldContain(text => text.Text == shell.LaunchNotice && text.IsEffectivelyVisible);
            shell.DismissLaunchNoticeCommand.Execute(null);
            shell.ShowLaunchNotice.ShouldBeFalse();

            shell.TellLeftOverMute(LeftOverMute.NotRestored);
            shell.LaunchNotice!.ShouldContain("Unmute it from the taskbar");
        }
        finally
        {
            window.Close();
        }
    }
}
