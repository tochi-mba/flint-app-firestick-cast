using System.Reflection;
using Flint.App.ViewModels;
using Flint.Core.Settings;
using Flint.Platform.Windows;
using Shouldly;

namespace Flint.App.Tests;

public sealed class SettingsIntegrationTests
{
    [Fact]
    public async Task NeverAskSwitchesWithoutOpeningTheQuestion()
    {
        var store = new InMemoryAppSettingsStore(new AppSettings
        {
            General = new GeneralSettings { AskBeforeSwitching = false },
        });
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()), settingsStore: store);
        await shell.Cast.ProbeCommand.ExecuteAsync(null);
        SetMirroring(shell.Cast, true);

        (await shell.Coordinator.TakeAsync(TvSurfaceKind.Browser, TestContext.Current.CancellationToken)).ShouldBeTrue();

        shell.SwitchPrompt.IsOpen.ShouldBeFalse();
        shell.Cast.IsMirroring.ShouldBeFalse();
    }

    [Fact]
    public void NeverAskAlsoTurnsOffArrivalOffers_ButTheTvNoticeRemains()
    {
        var store = new InMemoryAppSettingsStore(new AppSettings
        {
            General = new GeneralSettings { AskBeforeSwitching = false },
        });
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()), settingsStore: store);
        SetMirroring(shell.Cast, true);

        shell.Coordinator.AsksBeforeSwitching.ShouldBeFalse();
        shell.Coordinator.NoticeFor(TvSurfaceKind.Browser).ShouldNotBeNull().ShouldContain("showing your screen");
    }

    [Fact]
    public void KeepAwakeFollowsMirrorMediaSettingAndDispose()
    {
        var granted = new List<KeepAwakeLevel>();
        using var shell = MainWindowViewModel.CreateWith(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()),
            keepAwake: level => { granted.Add(level); return true; });

        SetMediaPlaying(shell.Cast, true);
        shell.KeepAwakeLevel.ShouldBe(KeepAwakeLevel.System);
        SetMirroring(shell.Cast, true);
        SetMediaPlaying(shell.Cast, false);
        shell.KeepAwakeLevel.ShouldBe(KeepAwakeLevel.SystemAndDisplay);
        shell.SettingsService.Update(value => value with { General = value.General with { KeepAwake = false } });
        shell.KeepAwakeLevel.ShouldBe(KeepAwakeLevel.None);

        granted.ShouldBe([KeepAwakeLevel.System, KeepAwakeLevel.SystemAndDisplay, KeepAwakeLevel.None]);
    }

    [Fact]
    public void InterfaceScaleChangesAtOnceAndMovesTheMinimumSizeWithIt()
    {
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var changed = new List<string?>();
        shell.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        shell.SettingsService.Update(value => value with
        {
            General = value.General with { InterfaceScalePercent = 130 },
        });

        shell.InterfaceScale.ShouldBe(1.3);
        shell.MinimumWidth.ShouldBe(1170);
        shell.MinimumHeight.ShouldBe(806);
        changed.ShouldContain(nameof(MainWindowViewModel.InterfaceScale));
    }

    private static void SetMirroring(CastPageViewModel cast, bool value) =>
        typeof(CastPageViewModel).GetField("_isMirroring", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(cast, value);

    private static void SetMediaPlaying(CastPageViewModel cast, bool value) =>
        typeof(CastPageViewModel).GetProperty(nameof(CastPageViewModel.IsMediaPlaying))!.SetValue(cast, value);
}
