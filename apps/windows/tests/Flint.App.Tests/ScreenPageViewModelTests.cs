using Flint.App.Tests.Snapshots;
using Flint.App.ViewModels;
using Flint.Core;
using Flint.Core.Settings;
using Flint.Session;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>The Screen page's choices: which display, and the picture it sends.</summary>
public sealed partial class ScreenPageViewModelTests : IDisposable
{
    private static readonly DisplayInfo Main = new(0, 1, "Main", "main", 0, 0, 1920, 1080, DisplayRotation.Upright, IsMain: true);
    private static readonly DisplayInfo Side = new(1, 2, "Side", "side", 1920, 0, 2560, 1440, DisplayRotation.Upright, IsMain: false);
    private static readonly DisplayInfo Tall = new(2, 3, "Tall", "tall", -1080, 0, 1080, 1920, DisplayRotation.QuarterClockwise, IsMain: false);

    private readonly SettingsService settings = new(new InMemoryAppSettingsStore());
    private readonly ManualTime clock = new();
    private readonly SnapshotFixtures.FixedDisplays displays = new([Main, Side]);

    public void Dispose() => settings.Dispose();

    [Fact]
    public void TheMainDisplay_IsChosenAtFirst_FromSeveral()
    {
        using var screen = Page();

        screen.Displays.ShouldBe([Main, Side]);
        screen.SelectedDisplay.ShouldBe(Main);
        screen.HasSeveralDisplays.ShouldBeTrue();
        screen.OnlyDisplay.ShouldBeNull();
        screen.MissingDisplayNotice.ShouldBeNull();
        screen.RotatedDisplayNotice.ShouldBeNull();
    }

    [Fact]
    public void OneDisplay_IsNamedInALine_InsteadOfAMap()
    {
        displays.Displays = [Main];

        using var screen = Page();

        screen.HasSeveralDisplays.ShouldBeFalse();
        screen.OnlyDisplay.ShouldBe("1 · Main · 1920 × 1080 · main");
    }

    [Fact]
    public void ChoosingADisplay_RemembersItByIdentity_AndAgainNextTime()
    {
        using (var screen = Page())
        {
            screen.SelectedDisplay = Side;

            settings.Current.Screen.Display.ShouldBe(ShareDisplayChoice.Remembered);
            settings.Current.Screen.DisplayIdentity.ShouldBe("side");
        }

        displays.Displays = [Side with { Index = 0, Number = 1 }, Main with { Index = 1, Number = 2 }];
        using var again = Page();
        again.SelectedDisplay!.Identity.ShouldBe("side", "the same monitor, wherever it now sits in the list");
    }

    [Fact]
    public void AMissingUsualDisplay_FallsBackToTheMainOne_AndSaysSo_UntilAnotherIsChosen()
    {
        settings.Update(current => current with { Screen = current.Screen with { Display = ShareDisplayChoice.Remembered, DisplayIdentity = "gone" } });

        using var screen = Page();

        screen.SelectedDisplay.ShouldBe(Main);
        screen.MissingDisplayNotice.ShouldBe("Your usual display is not connected. Using the main display.");
        screen.SelectedDisplay = Side;
        screen.MissingDisplayNotice.ShouldBeNull();
    }

    [Fact]
    public void ADisplayChange_KeepsTheChoice_WhileItIsStillConnected()
    {
        using var screen = Page();
        screen.SelectedDisplay = Side;
        var raised = new List<string?>();
        screen.PropertyChanged += (_, change) => raised.Add(change.PropertyName);

        displays.Displays = [Main, Side, Tall];
        screen.RefreshDisplays();

        screen.SelectedDisplay.ShouldBe(Side);
        screen.Displays.Count.ShouldBe(3);
        raised.ShouldContain(nameof(ScreenPageViewModel.Displays));

        displays.Displays = [Main];
        screen.RefreshDisplays();
        screen.SelectedDisplay.ShouldBe(Main);
        screen.MissingDisplayNotice.ShouldNotBeNull("the remembered display was unplugged");
    }

    [Fact]
    public void ADisplayChange_KeepsShowingTheDisplayBeingShared_NotJustTheRememberedOne()
    {
        using var screen = Page();
        screen.SelectedDisplay = Side;
        screen.OnSwitched(new MirrorSwitch(new MirrorSessionOptions(OutputIndex: 0), "Display 2 cannot be captured."));
        screen.SelectedDisplay.ShouldBe(Main);

        displays.Displays = [Main, Side, Tall];
        screen.RefreshDisplays();

        screen.SelectedDisplay.ShouldBe(Main, "the share carried on there, whatever the settings remember");
    }

    [Fact]
    public void ARotatedDisplay_IsSaidToAppearSideways()
    {
        displays.Displays = [Main, Tall];
        using var screen = Page();

        screen.SelectedDisplay = Tall;

        screen.RotatedDisplayNotice.ShouldBe("This display is rotated. It will appear sideways on the TV.");
    }

    [Fact]
    public void ChoosingNothing_OrTheSameDisplay_ChangesNothing()
    {
        using var screen = Page();
        var changes = 0;
        settings.Changed += (_, _) => changes++;

        screen.SelectedDisplay = null;
        screen.SelectedDisplay = Main;

        changes.ShouldBe(0);
        screen.SelectedDisplay.ShouldBe(Main);
    }

    [Fact]
    public void APictureMode_IsSavedAndDescribedInItsOwnNumbers()
    {
        using var screen = Page();
        screen.ChosenMode.Mode.ShouldBe(PictureMode.Balanced);
        screen.ModeSentence.ShouldBe("For everyday use. Up to 1080p, 30 frames a second, 12 Mbps.");

        screen.ChosenMode = screen.PictureModes.Single(choice => choice.Mode is PictureMode.Game);

        settings.Current.Screen.PictureMode.ShouldBe(PictureMode.Game);
        screen.ModeSentence.ShouldBe("Smoother motion. Up to 1080p, 60 frames a second, 16 Mbps.");
        screen.IsCustom.ShouldBeFalse();
        screen.ChosenMode = null!;
        settings.Current.Screen.PictureMode.ShouldBe(PictureMode.Game, "an empty choice is not a choice");
        screen.PictureModes.Select(choice => choice.ToString()).ShouldBe(["Balanced", "Movie", "Game", "Text and slides", "Data saver", "Custom"]);
    }

    [Fact]
    public void Custom_ShowsItsControls_AndSavesEachOne()
    {
        using var screen = Page();

        screen.ChosenMode = screen.PictureModes.Single(choice => choice.Mode is PictureMode.Custom);
        screen.CustomSizeLimit = screen.SizeLimits.Single(choice => choice.Limit is SizeLimit.Native);
        screen.CustomFrameRate = screen.FrameRates.Single(choice => choice.FramesPerSecond == 24);
        screen.CustomMegabits = 8.6;

        screen.IsCustom.ShouldBeTrue();
        settings.Current.Screen.CustomSizeLimit.ShouldBe(SizeLimit.Native);
        settings.Current.Screen.CustomFramesPerSecond.ShouldBe(24);
        settings.Current.Screen.CustomMegabitsPerSecond.ShouldBe(9);
        screen.CustomMegabits.ShouldBe(9);
        screen.ModeSentence.ShouldBe("Your own size, frame rate and data rate. Up to 1080p, 24 frames a second, 9 Mbps.");
        screen.SizeLimits.Select(choice => choice.ToString()).ShouldContain("Match the TV");
        screen.FrameRates[0].ToString().ShouldBe("15 a second");

        screen.CustomSizeLimit = null!;
        screen.CustomFrameRate = null!;
        settings.Current.Screen.CustomFramesPerSecond.ShouldBe(24);
    }

    [Fact]
    public void ANativeSizedCustomPicture_IsLimitedByTheChosenDisplay()
    {
        settings.Update(current => current with
        {
            Screen = current.Screen with { PictureMode = PictureMode.Custom, CustomSizeLimit = SizeLimit.Native },
        });
        using var screen = Page();

        screen.SelectedDisplay = Side;

        screen.ModeSentence.ShouldContain("1440p");
    }

    [Fact]
    public void ChangingThePicture_WhileNothingIsShared_OnlySavesIt()
    {
        using var screen = Page();

        screen.ChosenMode = screen.PictureModes.Single(choice => choice.Mode is PictureMode.Movie);

        screen.IsSwitching.ShouldBeFalse();
        screen.SwitchStatus.ShouldBeNull();
    }

    [Fact]
    public void ARefusedSwitch_ShowsTheDisplayStillShared_AndSaysWhy()
    {
        using var screen = Page();
        screen.SelectedDisplay = Side;

        screen.OnSwitched(new MirrorSwitch(new MirrorSessionOptions(OutputIndex: 0), "Display 2 cannot be captured."));

        screen.SelectedDisplay.ShouldBe(Main);
        screen.IsSwitching.ShouldBeFalse();
        screen.SwitchStatus.ShouldBe("Flint could not switch, so sharing carries on as before. Display 2 cannot be captured.");

        screen.OnSwitched(new MirrorSwitch(new MirrorSessionOptions(OutputIndex: 0), "Still no."));
        screen.SelectedDisplay.ShouldBe(Main, "already showing the display kept");

        screen.OnSwitched(new MirrorSwitch(new MirrorSessionOptions(OutputIndex: 7), "Gone."));
        screen.SelectedDisplay.ShouldBe(Main, "a display no longer listed leaves the choice alone");

        screen.OnSwitched(new MirrorSwitch(new MirrorSessionOptions(OutputIndex: 1), null));
        screen.SwitchStatus.ShouldBeNull();
    }

    [Fact]
    public void WithNoDisplaysListed_TheFirstCaptureCanOpenIsShared()
    {
        displays.Displays = [];

        using var screen = Page();

        screen.SelectedDisplay.ShouldBeNull();
        screen.OnlyDisplay.ShouldBeNull();
        screen.RotatedDisplayNotice.ShouldBeNull();
        screen.MissingDisplayNotice.ShouldBeNull();
        screen.ModeSentence.ShouldBe("For everyday use. Up to 1080p, 30 frames a second, 12 Mbps.");
        ScreenPageText.DescribeDisplay.Convert(null, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture).ShouldBeNull();
        ScreenPageText.DescribeDisplay.Convert(Main, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture).ShouldBe(Main.Describe());
    }

    [Fact]
    public void ThePage_NeedsItsParts()
    {
        var cast = SnapshotFixtures.ViewModel();
        Should.Throw<ArgumentNullException>(() => new ScreenPageViewModel(null!, settings, displays));
        Should.Throw<ArgumentNullException>(() => new ScreenPageViewModel(cast, null!, displays));
        Should.Throw<ArgumentNullException>(() => new ScreenPageViewModel(cast, settings, null!));
    }

    private ScreenPageViewModel Page(CastPageViewModel? cast = null) =>
        new(cast ?? SnapshotFixtures.ViewModel(), settings, displays, clock);
}
