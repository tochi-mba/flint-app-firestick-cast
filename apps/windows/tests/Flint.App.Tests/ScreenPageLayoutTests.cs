using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Flint.App.Controls;
using Flint.App.Tests.Snapshots;
using Flint.App.ViewModels;
using Flint.App.Views;
using Flint.Core;
using Flint.Core.Settings;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>The Screen page's parts appear when they apply, and only then.</summary>
public sealed class ScreenPageLayoutTests : IDisposable
{
    private static readonly DisplayInfo Main = SnapshotFixtures.MainDisplay;
    private static readonly DisplayInfo Side = new(1, 2, "Side", "side", 2560, 0, 1920, 1080, DisplayRotation.Upright, IsMain: false);

    private readonly SettingsService settings = new(new InMemoryAppSettingsStore());

    public void Dispose() => settings.Dispose();

    [AvaloniaFact]
    public void SeveralDisplays_AreShownAsAMap()
    {
        var (page, window) = Render(Main, Side);
        try
        {
            page.FindControl<DisplayArrangementPicker>("DisplayMap")!.IsEffectivelyVisible.ShouldBeTrue();
            Texts(page).ShouldContain("1 · DELL U2720Q · 2560 × 1440 · main", "the chosen display is named under the map");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OneDisplay_IsALine_WithNoMap()
    {
        var (page, window) = Render(Main);
        try
        {
            page.FindControl<DisplayArrangementPicker>("DisplayMap")!.IsEffectivelyVisible.ShouldBeFalse();
            Texts(page).ShouldContain("1 · DELL U2720Q · 2560 × 1440 · main");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheCustomControls_AppearOnlyInCustom()
    {
        var (page, window) = Render(Main);
        try
        {
            Shown(page.FindControl<ComboBox>("CustomSize")!).ShouldBeFalse();

            settings.Update(current => current with { Screen = current.Screen with { PictureMode = PictureMode.Custom } });
            window.UpdateLayout();

            Shown(page.FindControl<ComboBox>("CustomSize")!).ShouldBeTrue();
            Shown(page.FindControl<ComboBox>("CustomFrames")!).ShouldBeTrue();
            page.FindControl<Slider>("CustomRate")!.Minimum.ShouldBe(ScreenSettings.MinimumMegabitsPerSecond);
            page.FindControl<Slider>("CustomRate")!.Maximum.ShouldBe(ScreenSettings.MaximumMegabitsPerSecond);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheLiveNumbers_AppearWhenSwitchedOn()
    {
        var (page, window) = Render(Main);
        try
        {
            var numbers = page.GetVisualDescendants().OfType<LiveNumbersView>().Single();
            numbers.IsEffectivelyVisible.ShouldBeFalse();

            page.FindControl<ToggleSwitch>("LiveNumbers")!.IsChecked = true;
            window.UpdateLayout();

            numbers.IsEffectivelyVisible.ShouldBeTrue();
            Texts(page).ShouldContain("These are counters. They are not a measurement of delay.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheDisplays_AreReadAgain_WhenThePageOpensAndWhenWindowsChangesThem()
    {
        var catalog = new CountingDisplays([Main]);
        var screen = new ScreenPageViewModel(SnapshotFixtures.ViewModel(), settings, catalog);
        var page = new ScreenPage { DataContext = screen };
        var window = new Window { Width = 1180, Height = 900, Content = page };
        try
        {
            window.Show();
            var reads = catalog.Reads;

            catalog.Displays = [Main, Side];
            page.OnScreensChanged(null, EventArgs.Empty);
            catalog.Reads.ShouldBe(reads + 1);
            screen.Displays.Count.ShouldBe(2);

            page.IsVisible = false;
            page.IsVisible = true;
            catalog.Reads.ShouldBe(reads + 2, "opening the page reads them again");

            page.DataContext = null;
            Should.NotThrow(() => page.OnScreensChanged(null, EventArgs.Empty));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Pause_IsOfferedOnlyWhileSharing_AndOncePausedResumeHasFocus()
    {
        var (page, window) = Render(Main);
        try
        {
            var screen = (ScreenPageViewModel)page.DataContext!;
            var pause = page.FindControl<Button>("PauseButton")!;
            var banner = page.FindControl<Border>("PausedBanner")!;
            pause.IsEffectivelyVisible.ShouldBeFalse("nothing is being shared");

            screen.Cast.IsMirroring = true;
            window.UpdateLayout();
            pause.IsEffectivelyVisible.ShouldBeTrue();
            banner.IsEffectivelyVisible.ShouldBeFalse();

            screen.Cast.MirrorPause = MirrorPause.HoldingLastPicture;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            pause.IsEffectivelyVisible.ShouldBeFalse();
            banner.IsEffectivelyVisible.ShouldBeTrue();
            Avalonia.Automation.AutomationProperties.GetLiveSetting(banner).ShouldBe(Avalonia.Automation.AutomationLiveSetting.Assertive);
            page.FindControl<Button>("ResumeButton")!.IsFocused.ShouldBeTrue("RESUME is where the next key press goes");

            screen.Cast.MirrorPause = MirrorPause.Running;
            screen.Cast.IsMirroring = false;
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Diagnostics_ShowTheScreenPagesCounters()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var window = new MainWindow { DataContext = shell, Width = 1180, Height = 780 };
        try
        {
            window.Show();
            window.UpdateLayout();

            var diagnostics = window.GetVisualDescendants().OfType<DiagnosticsPage>().Single();
            diagnostics.FindControl<LiveNumbersView>("ShareNumbers")!.DataContext.ShouldBeSameAs(shell.Screen);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A display list that counts how often it is read.</summary>
    private sealed class CountingDisplays(IReadOnlyList<DisplayInfo> displays) : IDisplayCatalog
    {
        public IReadOnlyList<DisplayInfo> Displays { get; set; } = displays;

        public int Reads { get; private set; }

        public IReadOnlyList<DisplayInfo> List()
        {
            Reads++;
            return Displays;
        }
    }

    private (ScreenPage Page, Window Window) Render(params DisplayInfo[] displays)
    {
        var screen = SnapshotFixtures.Screen(SnapshotFixtures.ViewModel(), settings, displays);
        var page = new ScreenPage { DataContext = screen };
        var window = new Window { Width = 1180, Height = 900, Content = page };
        window.Show();
        window.UpdateLayout();
        return (page, window);
    }

    /// <summary>Whether a control is on screen: in the window, and not inside anything hidden.</summary>
    /// <remarks>A control inside a hidden section is never templated, so it is not in the window at all.</remarks>
    private static bool Shown(Control control) => TopLevel.GetTopLevel(control) is not null && control.IsEffectivelyVisible;

    private static List<string> Texts(Control root) =>
    [
        .. root.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible)
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!),
    ];
}
