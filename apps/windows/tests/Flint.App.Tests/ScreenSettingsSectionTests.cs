using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flint.App.ViewModels;
using Flint.App.ViewModels.Settings;
using Flint.App.Views;
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
        screen.Settings.Count.ShouldBe(9);
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
}
