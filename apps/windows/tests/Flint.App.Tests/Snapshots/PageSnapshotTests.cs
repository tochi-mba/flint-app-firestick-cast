using Avalonia;
using Avalonia.Headless.XUnit;
using Flint.App.Views;

namespace Flint.App.Tests.Snapshots;

/// <summary>
/// Every page Flint shows, held to an approved image.
/// </summary>
/// <remarks>
/// <para>
/// These are the tests that catch what the assertion suites structurally cannot: a control that
/// renders behind another, a rail that no longer lines up with its content, a heading that has
/// drifted three pixels down, a page that fits at one size and clips at another. All of those pass
/// every property assertion in this project.
/// </para>
/// <para>
/// Each page is rendered at the smallest window Flint supports, because that is where layout breaks
/// first. Pages with a populated state get two images — empty and populated — since a page can be
/// correct with no data and wrong the moment it has some.
/// </para>
/// </remarks>
public sealed class PageSnapshotTests
{
    [AvaloniaFact]
    public void MainWindow_Shell()
    {
        // The whole shell: brand lockup, left rail, and the page it opens on. This is the one image
        // that would catch the rail and the content disagreeing about width.
        var window = new MainWindow { DataContext = SnapshotFixtures.Shell() };

        Snapshot.MatchesWindow("main-window-shell", window);
    }

    [AvaloniaFact]
    public void CastPage_BeforeProbing()
    {
        var page = new CastPage { DataContext = SnapshotFixtures.ViewModel() };

        Snapshot.Matches("cast-page-idle", page);
    }

    [AvaloniaFact]
    public async Task CastPage_WithACapableDevice()
    {
        var page = new CastPage { DataContext = await SnapshotFixtures.ProbedViewModel() };

        Snapshot.Matches("cast-page-ready", page);
    }

    [AvaloniaFact]
    public async Task CastPage_WithAVegaDeviceThatCannotBeCastTo()
    {
        // The honest-refusal state. It has to read as a clear explanation rather than an error, and
        // it is the state most likely to rot, because nobody looks at it during ordinary work.
        var viewModel = await SnapshotFixtures.ProbedViewModel(
            SnapshotFixtures.VegaDevice(),
            SnapshotFixtures.CapableHost(),
            SnapshotFixtures.GoodPath());
        var page = new CastPage { DataContext = viewModel };

        Snapshot.Matches("cast-page-vega-device", page);
    }

    [AvaloniaFact]
    public void ScreenPage_BeforeProbing()
    {
        var page = new ScreenPage { DataContext = SnapshotFixtures.ViewModel() };

        Snapshot.Matches("screen-page-idle", page);
    }

    [AvaloniaFact]
    public async Task ScreenPage_WhenMirroringIsAvailable()
    {
        var page = new ScreenPage { DataContext = await SnapshotFixtures.ProbedViewModel() };

        Snapshot.Matches("screen-page-ready", page);
    }

    [AvaloniaFact]
    public async Task ScreenPage_WhenTheHostHasNoEncoder()
    {
        // The disabled state, which a property test proves is disabled and only an image proves
        // actually reads as unavailable rather than merely looking pressable.
        var viewModel = await SnapshotFixtures.ProbedViewModel(
            SnapshotFixtures.CapableDevice(),
            SnapshotFixtures.HostThatCannotMirror(),
            SnapshotFixtures.GoodPath());
        var page = new ScreenPage { DataContext = viewModel };

        Snapshot.Matches("screen-page-blocked", page);
    }

    [AvaloniaFact]
    public void MediaPage_BeforeProbing()
    {
        var page = new MediaPage { DataContext = SnapshotFixtures.ViewModel() };

        Snapshot.Matches("media-page-idle", page);
    }

    [AvaloniaFact]
    public async Task MediaPage_WithACapableDevice()
    {
        var page = new MediaPage { DataContext = await SnapshotFixtures.ProbedViewModel() };

        Snapshot.Matches("media-page-ready", page);
    }

    [AvaloniaFact]
    public void DiagnosticsPage_BeforeProbing()
    {
        var page = new DiagnosticsPage { DataContext = SnapshotFixtures.ViewModel() };

        Snapshot.Matches("diagnostics-page-idle", page);
    }

    [AvaloniaFact]
    public async Task DiagnosticsPage_WithAHealthyPath()
    {
        var page = new DiagnosticsPage { DataContext = await SnapshotFixtures.ProbedViewModel() };

        Snapshot.Matches("diagnostics-page-healthy", page);
    }

    [AvaloniaFact]
    public async Task DiagnosticsPage_WithACongestedPath()
    {
        // The diagnostic rows carry the numbers that justify every latency claim the app makes, so
        // a layout that truncates one is a correctness problem rather than a cosmetic one.
        var viewModel = await SnapshotFixtures.ProbedViewModel(
            SnapshotFixtures.CapableDevice(),
            SnapshotFixtures.CapableHost(),
            SnapshotFixtures.PoorPath());
        var page = new DiagnosticsPage { DataContext = viewModel };

        Snapshot.Matches("diagnostics-page-congested", page);
    }

    [AvaloniaFact]
    public void BrowserPage_BeforeVerification()
    {
        // The state a person meets first: the feature explained, and one action offered.
        var page = new BrowserPage { DataContext = BrowserFixtures.ViewModel() };

        Snapshot.Matches("browser-page-idle", page);
    }

    [AvaloniaFact]
    public async Task BrowserPage_WithAVerifiedSession()
    {
        // The working state, with the address bar, the history controls and the remote. This is the
        // image that would catch the remote pad losing its layout, which no property test can see.
        var page = new BrowserPage
        {
            DataContext = await BrowserFixtures.ReadyViewModelAsync(new RecordingBrowserRemote()),
        };

        Snapshot.Matches("browser-page-ready", page, new PixelSize(1280, 1100));
    }

    [AvaloniaFact]
    public void SettingsPage()
    {
        var page = new SettingsPage { DataContext = SnapshotFixtures.Settings() };

        Snapshot.Matches("settings-page", page);
    }

    [AvaloniaFact]
    public void OnboardingView_FirstStep()
    {
        var page = new OnboardingView { DataContext = SnapshotFixtures.Onboarding() };

        Snapshot.Matches("onboarding-first-step", page);
    }

    [AvaloniaFact]
    public void CastPage_AtTheNarrowestSupportedWindow()
    {
        // Layout breaks at the edges, not in the middle. This is the width below which Flint does
        // not claim to work, so it is the width worth pinning.
        var page = new CastPage { DataContext = SnapshotFixtures.ViewModel() };

        Snapshot.Matches("cast-page-narrow", page, new PixelSize(1024, 640));
    }

    [AvaloniaFact]
    public void CastPage_AtAWideWindow()
    {
        // The other edge: content that centres correctly at 1280 can strand itself at 1920.
        var page = new CastPage { DataContext = SnapshotFixtures.ViewModel() };

        Snapshot.Matches("cast-page-wide", page, new PixelSize(1920, 1080));
    }
}
