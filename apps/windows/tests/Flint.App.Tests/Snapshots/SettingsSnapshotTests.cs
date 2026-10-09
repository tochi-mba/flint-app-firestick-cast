using Avalonia;
using Avalonia.Headless.XUnit;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.App.ViewModels.Settings;
using Flint.App.Views;
using Flint.Core;
using Flint.Core.Settings;
using Flint.Core.Shortcuts;
using Shouldly;

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
    public void Settings_GeneralWithStartingAndClosing()
    {
        var model = Page();
        var general = model.Section<GeneralSettingsViewModel>();
        general.UseSignIn(new Flint.Platform.Windows.RunAtSignIn(
            new Flint.Platform.Windows.InMemoryRunKey(),
            @"C:\Users\you\AppData\Local\Flint\Flint.exe",
            _ => true));
        general.StartWithWindows = true;
        model.SelectedSection = general;

        Snapshot.Matches("settings-section-general", new SettingsPage { DataContext = model }, new Avalonia.PixelSize(1280, 1900));
    }

    [AvaloniaFact]
    public void Settings_Shortcuts()
    {
        var model = Page();
        model.SelectedSection = model.Section<ShortcutsSettingsViewModel>();

        Snapshot.Matches("settings-section-shortcuts", new SettingsPage { DataContext = model }, new Avalonia.PixelSize(1280, 1300));
    }

    [AvaloniaFact]
    public void Settings_ShortcutsCapturing()
    {
        var model = Page();
        var shortcuts = model.Section<ShortcutsSettingsViewModel>();
        shortcuts.Rows[1].ChangeCommand.Execute(null);
        model.SelectedSection = shortcuts;

        Snapshot.Matches("settings-section-shortcuts-capturing", new SettingsPage { DataContext = model }, new Avalonia.PixelSize(1280, 1300));
    }

    [AvaloniaFact]
    public void Settings_ShortcutsInConflict()
    {
        var settings = NewSettings();
        var model = Page(settings, null);
        var shortcuts = model.Section<ShortcutsSettingsViewModel>();
        var service = new HotKeyService(new TakenRegistrar("Ctrl+Alt+Shift+P"), settings, _ => true);
        shortcuts.UseService(service);
        shortcuts.Rows[7].ChangeCommand.Execute(null);
        HotKeyGesture.TryParse("Ctrl+Alt+Shift+S", out var taken, out _).ShouldBeTrue();
        shortcuts.Rows[7].Capture(taken);
        model.SelectedSection = shortcuts;

        Snapshot.Matches("settings-section-shortcuts-conflict", new SettingsPage { DataContext = model }, new Avalonia.PixelSize(1280, 1300));
        service.Dispose();
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

    private static SettingsPageViewModel Page(Flint.Core.IKnownTvStore? tvs, params RecentAddress[] remembered) =>
        Page(NewSettings(), tvs, remembered);

    /// <summary>Settings as Flint ships them, kept in memory and never saved.</summary>
    private static SettingsService NewSettings() =>
        new(new InMemoryAppSettingsStore(), (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));

    private static SettingsPageViewModel Page(SettingsService settings, Flint.Core.IKnownTvStore? tvs, params RecentAddress[] remembered)
    {
        var shell = SnapshotFixtures.Shell();
        foreach (var address in remembered)
        {
            shell.Cast.RecentAddresses.Add(address);
        }

        if (tvs is not null)
        {
            shell.Cast.UseReconnect(tvs, settings);
        }
        return new SettingsPageViewModel(
        [
            new GeneralSettingsViewModel(settings),
            new MediaSettingsViewModel(settings),
            new ScreenSettingsViewModel(settings),
            new ShortcutsSettingsViewModel(settings),
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

    /// <summary>Windows, with some combinations already taken by other programs.</summary>
    private sealed class TakenRegistrar(params string[] taken) : IHotKeyRegistrar
    {
        public event Action<int>? Pressed
        {
            add { }
            remove { }
        }

        public bool Register(int id, HotKeyModifiers modifiers, int virtualKey) =>
            !taken.Contains(new HotKeyGesture(modifiers, virtualKey).ToString());

        public void Unregister(int id)
        {
        }
    }

    private sealed class NoFolders : IFolderOpener
    {
        public bool Open(string path) => true;
    }
}
