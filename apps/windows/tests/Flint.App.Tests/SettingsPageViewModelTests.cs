using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.App.ViewModels.Settings;
using Flint.Core.Settings;
using Shouldly;

namespace Flint.App.Tests;

public sealed class SettingsPageViewModelTests
{
    [Fact]
    public void SearchMatchesEveryWordAcrossTitlesAndDescriptionsWithoutCase()
    {
        var service = new SettingsService(new InMemoryAppSettingsStore(), NoDelay);
        var general = new GeneralSettingsViewModel(service);
        var page = new SettingsPageViewModel([general]);

        page.SearchText = "TV switching";

        page.SearchResults.Count.ShouldBe(1);
        page.SearchResults[0].Setting.ShouldBe(general.AskBeforeSwitchingText);
        page.HasNoResults.ShouldBeFalse();
    }

    [Fact]
    public void SearchWithNoMatchHasAnHonestEmptyState_AndOpeningAResultReturnsToItsSection()
    {
        var service = new SettingsService(new InMemoryAppSettingsStore(), NoDelay);
        var general = new GeneralSettingsViewModel(service);
        var privacy = new PrivacySettingsViewModel(service, new RecordingFolderOpener(), "data", "logs");
        var page = new SettingsPageViewModel([general, privacy]);
        page.SearchText = "nothing resembles this";
        page.HasNoResults.ShouldBeTrue();

        page.SearchText = "logs folder";
        page.OpenResultCommand.Execute(page.SearchResults.Single());

        page.SelectedSection.ShouldBe(privacy);
        page.SearchText.ShouldBeEmpty();
    }

    [Fact]
    public void ResetGeneralAsks_ThenChangesOnlyGeneral()
    {
        var service = new SettingsService(new InMemoryAppSettingsStore(new AppSettings
        {
            General = new GeneralSettings { KeepAwake = false },
            Media = new MediaSettings { Shuffle = true },
        }), NoDelay);
        var section = new GeneralSettingsViewModel(service);

        section.Reset.AskCommand.Execute(null);
        service.Current.General.KeepAwake.ShouldBeFalse();
        section.Reset.ConfirmCommand.Execute(null);

        service.Current.General.ShouldBe(new GeneralSettings());
        service.Current.Media.Shuffle.ShouldBeTrue();
    }

    [Fact]
    public void ImportRejectsDamageAndValidImportAppliesNormalizedSettings()
    {
        var service = new SettingsService(new InMemoryAppSettingsStore(), NoDelay);
        var section = new PrivacySettingsViewModel(service, new RecordingFolderOpener(), "data", "logs");
        using var damaged = new MemoryStream("not settings"u8.ToArray());
        section.Import(damaged);
        section.Status.ShouldNotBeNull().ShouldContain("not Flint settings");
        service.Current.ShouldBe(AppSettings.Default);

        using var valid = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            "{\"schemaVersion\":99,\"general\":{\"interfaceScalePercent\":999}}"));
        section.Import(valid);

        service.Current.General.InterfaceScalePercent.ShouldBe(130);
        section.Status.ShouldBe("Settings imported.");
    }

    [Fact]
    public void ExportWritesExactlyTheCurrentNormalizedSettings()
    {
        var initial = new AppSettings { General = new GeneralSettings { KeepAwake = false } };
        var service = new SettingsService(new InMemoryAppSettingsStore(initial), NoDelay);
        var section = new PrivacySettingsViewModel(service, new RecordingFolderOpener(), "data", "logs");
        using var output = new MemoryStream();

        section.Export(output);

        AppSettingsJson.Parse(System.Text.Encoding.UTF8.GetString(output.ToArray())).ShouldBe(initial.Normalize());
    }

    private static Task NoDelay(TimeSpan delay, CancellationToken token) => Task.Delay(Timeout.InfiniteTimeSpan, token);

    private sealed class RecordingFolderOpener : IFolderOpener
    {
        public bool Open(string path) => true;
    }
}
