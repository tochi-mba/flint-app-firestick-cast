using Flint.App.Controls;
using Flint.App.ViewModels;
using Flint.App.ViewModels.Settings;
using Flint.Core;
using Flint.Core.Settings;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// Each Settings section on its own: what it shows, what it changes, and what it refuses.
/// </summary>
public sealed class SettingsSectionsTests
{
    [Fact]
    public void General_ChangesEachSettingThroughTheService()
    {
        using var service = Service();
        var general = new GeneralSettingsViewModel(service);

        general.KeepAwake = false;
        general.AskBeforeSwitching = false;
        general.InterfaceScale = general.InterfaceScales.Single(choice => choice.Percent == 115);

        service.Current.General.KeepAwake.ShouldBeFalse();
        service.Current.General.AskBeforeSwitching.ShouldBeFalse();
        service.Current.General.InterfaceScalePercent.ShouldBe(115);
        general.InterfaceScale!.Label.ShouldBe("115%");
        general.InterfaceScale.ToString().ShouldBe("115%");
    }

    [Fact]
    public void General_ClearingTheSizeBoxChangesNothing()
    {
        // A ComboBox writes null while its items are being replaced. That is not a choice.
        using var service = Service();
        var general = new GeneralSettingsViewModel(service);

        general.InterfaceScale = null;

        service.Current.General.InterfaceScalePercent.ShouldBe(100);
    }

    [Fact]
    public void General_RefreshesOnlyWhenGeneralChanged()
    {
        using var service = Service();
        var general = new GeneralSettingsViewModel(service);
        var refreshed = 0;
        general.PropertyChanged += (_, _) => refreshed++;

        service.Update(value => value with { Media = value.Media with { Shuffle = true } });
        refreshed.ShouldBe(0);

        service.Update(value => value with { General = value.General with { KeepAwake = false } });
        refreshed.ShouldBe(1);
    }

    [Fact]
    public void General_ListsEverySettingItShows()
    {
        var general = new GeneralSettingsViewModel(Service());

        general.Settings.ShouldBe([general.AskBeforeSwitchingText, general.KeepAwakeText, general.InterfaceSizeText]);
        general.Title.ShouldBe("General");
        Should.Throw<ArgumentNullException>(() => new GeneralSettingsViewModel(null!));
    }

    [Fact]
    public void Confirm_DoesNothingUntilAsked_AndCancelPutsTheButtonBack()
    {
        var runs = 0;
        var action = new ConfirmableAction("RESET", "Reset it?", "YES", () => runs++);

        action.ConfirmCommand.Execute(null);
        runs.ShouldBe(0, "a confirm that was never asked for is not an answer");

        action.AskCommand.Execute(null);
        action.IsAsking.ShouldBeTrue();
        action.CancelCommand.Execute(null);
        action.IsAsking.ShouldBeFalse();
        runs.ShouldBe(0);

        action.AskCommand.Execute(null);
        action.ConfirmCommand.Execute(null);
        action.ConfirmCommand.Execute(null);
        runs.ShouldBe(1, "one question, one change");
        action.IsAsking.ShouldBeFalse();
    }

    [Fact]
    public void Confirm_RefusesMissingWords()
    {
        Should.Throw<ArgumentException>(() => new ConfirmableAction(" ", "q", "c", () => { }));
        Should.Throw<ArgumentException>(() => new ConfirmableAction("l", " ", "c", () => { }));
        Should.Throw<ArgumentException>(() => new ConfirmableAction("l", "q", " ", () => { }));
        Should.Throw<ArgumentNullException>(() => new ConfirmableAction("l", "q", "c", null!));
    }

    [Fact]
    public void Tvs_ListsRememberedAddresses_AndForgettingAllClearsThem()
    {
        using var shell = MainWindowViewModel.CreateWith(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()),
            addressStore: new ListAddressStore(new RecentAddress("192.168.1.42", 8009)));
        var tvs = shell.Settings.Section<TvSettingsViewModel>();
        tvs.HasRecentAddresses.ShouldBeTrue();
        var raised = new List<string?>();
        tvs.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        tvs.ForgetAll.AskCommand.Execute(null);
        tvs.ForgetAll.ConfirmCommand.Execute(null);

        tvs.RecentAddresses.ShouldBeEmpty();
        tvs.HasRecentAddresses.ShouldBeFalse();
        raised.ShouldContain(nameof(TvSettingsViewModel.HasRecentAddresses));
        tvs.Settings.ShouldBe([tvs.RememberedText]);
        Should.Throw<ArgumentNullException>(() => new TvSettingsViewModel(null!));
    }

    [Fact]
    public void Updates_WrapsTheUpdateClient()
    {
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var updates = shell.Settings.Section<UpdatesSectionViewModel>();

        updates.Updates.ShouldBeSameAs(shell.Updates);
        updates.Settings.ShouldBe([updates.CheckText]);
        Should.Throw<ArgumentNullException>(() => new UpdatesSectionViewModel(null!));
    }

    [Fact]
    public async Task About_ReportNamesTheVersionsAndTheTvModel_ButNothingThatIdentifiesTheHome()
    {
        var device = BrowserFixtures.EligibleDevice() with { Model = "AFTKA" };
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(device));
        await shell.Cast.ProbeCommand.ExecuteAsync(null);
        var about = new AboutSettingsViewModel(shell.Cast, "v1.2.3", "0.1.0", "Windows 11");

        about.ReportDetails.ShouldBe("Flint v1.2.3\nEngine 0.1.0\nWindows Windows 11\nTV AFTKA, Fire OS 8".ReplaceLineEndings());
        about.ReportDetails.ShouldNotContain("192.168");
        about.ReportDetails.ShouldNotContain("Living Room");
    }

    [Fact]
    public async Task About_SaysWhenTheTvDidNotReportItsModel_AndWhenNoTvIsKnown()
    {
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var about = new AboutSettingsViewModel(shell.Cast, "v1", null, "Windows 11");
        about.EngineVersion.ShouldBe("Not loaded");
        about.ReportDetails.ShouldNotContain("TV ");

        await shell.Cast.ProbeCommand.ExecuteAsync(null);

        about.ReportDetails.ShouldEndWith("TV model not reported, Fire OS 8");
    }

    [Fact]
    public async Task About_CopyReportsEachOutcome()
    {
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var about = shell.Settings.Section<AboutSettingsViewModel>();
        var requested = 0;
        about.CopyRequested += (_, _) => requested++;
        about.CopyReportCommand.Execute(null);
        requested.ShouldBe(1);

        string? copied = null;
        await about.CopyReportWithAsync(text =>
        {
            copied = text;
            return Task.CompletedTask;
        });
        copied.ShouldBe(about.ReportDetails);
        about.Status.ShouldBe("Copied. Paste it into your report.");

        await about.CopyReportWithAsync(_ => throw new InvalidOperationException("held by another program"));
        about.Status.ShouldNotBeNull().ShouldStartWith("Windows would not let Flint use the clipboard");

        about.Status = null;
        await about.CopyReportWithAsync(null);
        about.Status.ShouldNotBeNull().ShouldStartWith("Windows would not let Flint use the clipboard");
    }

    [Fact]
    public void About_ListsItsSettings_AndRefusesMissingFacts()
    {
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var about = shell.Settings.Section<AboutSettingsViewModel>();

        about.Settings.ShouldBe([about.VersionText, about.WhatsNewText, about.ReportText, about.LicenceText]);
        Should.Throw<ArgumentNullException>(() => new AboutSettingsViewModel(null!, "v1", null, "w"));
        Should.Throw<ArgumentException>(() => new AboutSettingsViewModel(shell.Cast, " ", null, "w"));
        Should.Throw<ArgumentException>(() => new AboutSettingsViewModel(shell.Cast, "v1", null, " "));
    }

    [Fact]
    public void Search_NeedsAWord_AndResultsNameTheirSection()
    {
        var text = new SettingText("Interface size", "Makes everything larger or smaller.");
        text.Matches("   ").ShouldBeFalse();
        text.Matches("LARGER size").ShouldBeTrue();
        text.Matches("larger tv").ShouldBeFalse();
        Should.Throw<ArgumentNullException>(() => text.Matches(null!));

        var general = new GeneralSettingsViewModel(Service());
        new SettingSearchResult(general, text).SectionTitle.ShouldBe("General");
    }

    [Fact]
    public void Page_NeedsASection_AndIgnoresAnEmptyResult()
    {
        Should.Throw<ArgumentNullException>(() => new SettingsPageViewModel(null!));
        Should.Throw<ArgumentException>(() => new SettingsPageViewModel([]));

        var general = new GeneralSettingsViewModel(Service());
        var page = new SettingsPageViewModel([general]);
        page.SearchText = "size";
        page.OpenResultCommand.Execute(null);
        page.SearchText.ShouldBe("size");

        page.NoResultsText.ShouldBe("No settings match “size”.");
        page.ClearSearchCommand.Execute(null);
        page.IsSearching.ShouldBeFalse();
        page.SearchResults.ShouldBeEmpty();
        page.Section<GeneralSettingsViewModel>().ShouldBeSameAs(general);
    }

    [Fact]
    public void SettingRow_KeepsItsWords()
    {
        var row = new SettingRow { Title = "Interface size", Description = "Larger or smaller." };

        row.Title.ShouldBe("Interface size");
        row.Description.ShouldBe("Larger or smaller.");
    }

    [Fact]
    public void TheShellsFolderOpener_OpensNothingAndSucceeds()
    {
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var privacy = shell.Settings.Section<PrivacySettingsViewModel>();

        privacy.OpenDataFolderCommand.Execute(null);

        privacy.Status.ShouldBeNull();
    }

    private static SettingsService Service(AppSettings? initial = null) =>
        new(new InMemoryAppSettingsStore(initial), (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));

    private sealed class ListAddressStore(params RecentAddress[] addresses) : IRecentAddressStore
    {
        private readonly List<RecentAddress> saved = [.. addresses];

        public IReadOnlyList<RecentAddress> Load() => saved;

        public void Remember(RecentAddress address) => saved.Insert(0, address);

        public void Clear() => saved.Clear();
    }
}
