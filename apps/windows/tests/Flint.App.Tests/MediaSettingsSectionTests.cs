using Flint.App.ViewModels;
using Flint.App.ViewModels.Settings;
using Flint.Core.Media;
using Flint.Core.Settings;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>The Media section of the Settings page, and that the shell lists it.</summary>
public sealed class MediaSettingsSectionTests
{
    [Fact]
    public void ChangesEachSettingThroughTheService()
    {
        using var service = Service();
        var media = new MediaSettingsViewModel(service);

        media.SkipBack = media.SkipBackChoices.Single(choice => choice.Seconds == 5);
        media.SkipForward = media.SkipForwardChoices.Single(choice => choice.Seconds == 60);
        media.VolumeStep = 8;

        service.Current.Media.SkipBackSeconds.ShouldBe(5);
        service.Current.Media.SkipForwardSeconds.ShouldBe(60);
        service.Current.Media.VolumeStepPercent.ShouldBe(8);
        media.SkipBack!.Label.ShouldBe("5 seconds");
        media.SkipForward!.ToString().ShouldBe("60 seconds");
        media.VolumeStep.ShouldBe(8);
        media.VolumeStepLabel.ShouldBe("8%");
    }

    [Fact]
    public void OffersOnlyTheDistancesTheSettingsAllow()
    {
        var media = new MediaSettingsViewModel(Service());

        media.SkipBackChoices.Select(choice => choice.Seconds).ShouldBe(MediaSettings.SkipBackChoices);
        media.SkipForwardChoices.Select(choice => choice.Seconds).ShouldBe(MediaSettings.SkipForwardChoices);
    }

    [Fact]
    public void AVolumeStepOutsideTheRange_IsKeptWithinIt()
    {
        using var service = Service();
        var media = new MediaSettingsViewModel(service);

        media.VolumeStep = 40;
        service.Current.Media.VolumeStepPercent.ShouldBe(10);

        media.VolumeStep = 0.2;
        service.Current.Media.VolumeStepPercent.ShouldBe(1);
    }

    [Fact]
    public void ClearingAChoice_ChangesNothing()
    {
        using var service = Service();
        var media = new MediaSettingsViewModel(service);

        media.SkipBack = null;
        media.SkipForward = null;

        service.Current.Media.ShouldBe(new MediaSettings());
    }

    [Fact]
    public void RefreshesOnlyWhenMediaChanged()
    {
        using var service = Service();
        var media = new MediaSettingsViewModel(service);
        var refreshed = 0;
        media.PropertyChanged += (_, _) => refreshed++;

        service.Update(value => value with { General = value.General with { KeepAwake = false } });
        refreshed.ShouldBe(0);

        service.Update(value => value with { Media = value.Media with { SkipBackSeconds = 15 } });
        refreshed.ShouldBe(1);
    }

    [Fact]
    public void Reset_PutsTheSectionBack_AfterAsking()
    {
        using var service = Service();
        var media = new MediaSettingsViewModel(service);
        media.VolumeStep = 9;
        service.Update(value => value with { General = value.General with { KeepAwake = false } });

        media.Reset.AskCommand.Execute(null);
        media.Reset.ConfirmCommand.Execute(null);

        service.Current.Media.ShouldBe(new MediaSettings());
        service.Current.General.KeepAwake.ShouldBeFalse("only this section is reset");
    }

    [Fact]
    public void ListsEverySettingItShows()
    {
        var media = new MediaSettingsViewModel(Service());

        media.Settings.ShouldBe(
        [
            media.SkipBackText, media.SkipForwardText, media.VolumeStepText, media.AutoPlayNextText,
            media.RepeatText, media.ShuffleText, media.PlayedBeforeText, media.RememberText,
            media.SubfoldersText, media.DropOrderText, media.PictureText, media.QueueEndText,
        ]);
        media.Title.ShouldBe("Media");
        Should.Throw<ArgumentNullException>(() => new MediaSettingsViewModel(null!));
    }

    [Fact]
    public void TheShell_ListsMedia_AfterGeneral_AndTheCardReadsTheSameSettings()
    {
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));

        shell.Settings.Sections.Select(section => section.Title).Take(2).ShouldBe(["General", "Media"]);
        shell.Settings.Section<MediaSettingsViewModel>().SkipBack = new SecondsChoice(5);
        shell.Media.NowPlaying.SkipBackLabel.ShouldBe("−5 S");
        shell.Media.Cast.ShouldBeSameAs(shell.Cast);
    }

    [Fact]
    public void TheMediaPage_NeedsItsParts()
    {
        using var service = Service();
        var cast = Snapshots.SnapshotFixtures.ViewModel();

        var history = new InMemoryMediaHistoryStore();
        var files = new Flint.App.Services.LocalMediaFileSystem();

        Should.Throw<ArgumentNullException>(() => new MediaPageViewModel(null!, service, history, files));
        Should.Throw<ArgumentNullException>(() => new MediaPageViewModel(cast, null!, history, files));
        Should.Throw<ArgumentNullException>(() => new MediaPageViewModel(cast, service, null!, files));
        Should.Throw<ArgumentNullException>(() => new MediaPageViewModel(cast, service, history, null!));
    }

    private static SettingsService Service() => new(new InMemoryAppSettingsStore());
}
