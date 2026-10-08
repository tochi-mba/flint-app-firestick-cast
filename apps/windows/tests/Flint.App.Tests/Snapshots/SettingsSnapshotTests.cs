using Avalonia;
using Avalonia.Headless.XUnit;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.App.ViewModels.Settings;
using Flint.App.Views;
using Flint.Core;
using Flint.Core.Settings;

namespace Flint.App.Tests.Snapshots;

/// <summary>
/// Every Settings section, the search, and the narrow layout, held to approved images.
/// </summary>
/// <remarks>
/// Each section is built with fixed versions and folder paths. The real ones differ by machine and
/// by release, and a snapshot that changes with the machine running it approves nothing.
/// </remarks>
public sealed class SettingsSnapshotTests
{
    [AvaloniaFact]
    public void Settings_AtTheNarrowestWidth_PutsTheSectionsAboveTheContent()
    {
        var page = new SettingsPage { DataContext = Page() };

        Snapshot.Matches("settings-page-narrow", page, new PixelSize(700, 640));
    }

    [AvaloniaFact]
    public void Settings_Search()
    {
        var model = Page();
        model.SearchText = "folder";

        Snapshot.Matches("settings-page-search", new SettingsPage { DataContext = model });
    }

    [AvaloniaFact]
    public void Settings_SearchWithNoMatch()
    {
        var model = Page();
        model.SearchText = "nothing resembles this";

        Snapshot.Matches("settings-page-search-empty", new SettingsPage { DataContext = model });
    }

    [AvaloniaFact]
    public void Settings_TvsWithRememberedAddresses()
    {
        // Midday UTC, so the date the row shows is the same in every ordinary time zone.
        var tvs = new Flint.Core.InMemoryKnownTvStore();
        tvs.Save(new Flint.Core.KnownTv("Living Room", "192.168.1.42", 47855, new string('k', 43), new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero)));
        tvs.Save(new Flint.Core.KnownTv("Bedroom", "192.168.1.77", 47855, null, new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero)));
        var model = Page(tvs, new RecentAddress("192.168.1.42", 8009), new RecentAddress("192.168.1.77", null));
        model.SelectedSection = model.Section<TvSettingsViewModel>();

        Snapshot.Matches("settings-section-tvs", new SettingsPage { DataContext = model });
    }

    [AvaloniaFact]
    public void Settings_TvsWithNone()
    {
        var model = Page();
        model.SelectedSection = model.Section<TvSettingsViewModel>();

        Snapshot.Matches("settings-section-tvs-empty", new SettingsPage { DataContext = model });
    }

    [AvaloniaFact]
    public void Settings_Privacy()
    {
        var model = Page();
        model.SelectedSection = model.Section<PrivacySettingsViewModel>();

        Snapshot.Matches("settings-section-privacy", new SettingsPage { DataContext = model }, new PixelSize(1024, 900));
    }

    [AvaloniaFact]
    public void Settings_PrivacyAskingBeforeResettingEverything()
    {
        var model = Page();
        var privacy = model.Section<PrivacySettingsViewModel>();
        model.SelectedSection = privacy;
        privacy.ResetAll.AskCommand.Execute(null);

        Snapshot.Matches("settings-section-privacy-confirming", new SettingsPage { DataContext = model }, new PixelSize(1024, 900));
    }

    [AvaloniaFact]
    public void Settings_Updates()
    {
        var model = Page();
        model.SelectedSection = model.Section<UpdatesSectionViewModel>();

        Snapshot.Matches("settings-section-updates", new SettingsPage { DataContext = model });
    }

    [AvaloniaFact]
    public void Settings_Media()
    {
        var model = Page();
        model.SelectedSection = model.Section<MediaSettingsViewModel>();

        Snapshot.Matches("settings-section-media", new SettingsPage { DataContext = model });
    }

    [AvaloniaFact]
    public void Settings_ScreenSharingInCustom()
    {
        var model = Page();
        var screen = model.Section<ScreenSettingsViewModel>();
        screen.Picture = screen.PictureChoices.Single(choice => choice.Value is Flint.Core.Settings.PictureMode.Custom);
        model.SelectedSection = screen;

        Snapshot.Matches("settings-section-screen", new SettingsPage { DataContext = model }, new Avalonia.PixelSize(1280, 1200));
    }

    [AvaloniaFact]
    public void Settings_About()
    {
        var model = Page();
        model.SelectedSection = model.Section<AboutSettingsViewModel>();

        Snapshot.Matches("settings-section-about", new SettingsPage { DataContext = model });
    }

    /// <summary>The Settings page over pinned sections, so nothing in the image depends on this machine.</summary>
    private static SettingsPageViewModel Page(params RecentAddress[] remembered) => Page(null, remembered);

    private static SettingsPageViewModel Page(Flint.Core.IKnownTvStore? tvs, params RecentAddress[] remembered)
    {
        var shell = SnapshotFixtures.Shell();
        foreach (var address in remembered)
        {
            shell.Cast.RecentAddresses.Add(address);
        }

        var settings = new SettingsService(
            new InMemoryAppSettingsStore(),
            (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
        if (tvs is not null)
        {
            shell.Cast.UseReconnect(tvs, settings);
        }
        return new SettingsPageViewModel(
        [
            new GeneralSettingsViewModel(settings),
            new MediaSettingsViewModel(settings),
            new ScreenSettingsViewModel(settings),
            new TvSettingsViewModel(shell.Cast),
            new PrivacySettingsViewModel(
                settings,
                new NoFolders(),
                @"C:\Users\you\AppData\Local\REX Technologies\Flint",
                @"C:\Users\you\AppData\Local\Flint\logs",
                new Flint.Core.Media.InMemoryMediaHistoryStore()),
            new UpdatesSectionViewModel(shell.Updates),
            new AboutSettingsViewModel(shell.Cast, "v1.0.0", "0.1.0", "Windows 11"),
        ]);
    }

    private sealed class NoFolders : IFolderOpener
    {
        public bool Open(string path) => true;
    }
}
