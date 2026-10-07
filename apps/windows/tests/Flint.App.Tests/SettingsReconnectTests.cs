using Flint.App.ViewModels;
using Flint.App.ViewModels.Settings;
using Flint.Core;
using Flint.Core.Settings;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>The reconnect settings in General, and the remembered TVs in TVs.</summary>
public sealed class SettingsReconnectTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void General_ReconnectSwitches_ReadAndWriteTheSettings()
    {
        using var service = new SettingsService(new InMemoryAppSettingsStore());
        var general = new GeneralSettingsViewModel(service);

        general.ReconnectOnStart.ShouldBeTrue();
        general.ReconnectAfterDrop.ShouldBeTrue();
        general.OpenReceiverForNewCode.ShouldBeTrue();
        general.ReconnectOnStart = false;
        general.ReconnectAfterDrop = false;
        general.OpenReceiverForNewCode = false;

        service.Current.General.ReconnectOnStart.ShouldBeFalse();
        service.Current.General.ReconnectAfterDrop.ShouldBeFalse();
        service.Current.General.OpenReceiverForNewCode.ShouldBeFalse();
    }

    [Fact]
    public void General_HowLongAndAfterReconnecting_AreChoices()
    {
        using var service = new SettingsService(new InMemoryAppSettingsStore());
        var general = new GeneralSettingsViewModel(service);

        general.ReconnectTime!.Label.ShouldBe("2 minutes");
        general.ReconnectTime = general.ReconnectTimeChoices.Single(choice => choice.Value == 600);
        service.Current.General.ReconnectSeconds.ShouldBe(600);
        general.ReconnectTime = null;
        service.Current.General.ReconnectSeconds.ShouldBe(600, "clearing the box changes nothing");

        service.Update(current => current with { General = current.General with { ReconnectSeconds = 100 } });
        general.ReconnectTime!.Label.ShouldBe("2 minutes", "a time set by hand shows as the nearest offered");

        general.AfterReconnect!.Value.ShouldBe(ReconnectOutcome.Ask);
        general.AfterReconnect = general.AfterReconnectChoices.Single(choice => choice.Value == ReconnectOutcome.CarryOn);
        service.Current.General.AfterReconnect.ShouldBe(ReconnectOutcome.CarryOn);
        general.AfterReconnect = null;
        service.Current.General.AfterReconnect.ShouldBe(ReconnectOutcome.CarryOn);
        general.Settings.ShouldContain(general.ReconnectOnStartText);
        general.Settings.ShouldContain(general.OpenReceiverText);
    }

    [Fact]
    public void Tvs_ListsEachTv_WithWhatItCanDo_AndForgetsOne()
    {
        var store = new InMemoryKnownTvStore();
        store.Save(new KnownTv("Living Room", "10.0.0.5", 47855, new string('k', 43), Now));
        store.Save(new KnownTv("Bedroom", "10.0.0.6", 47855, null, Now.AddDays(-1)));
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()), knownTvs: store);
        var tvs = shell.Settings.Section<TvSettingsViewModel>();

        tvs.HasTvs.ShouldBeTrue();
        tvs.HasAnything.ShouldBeTrue();
        tvs.Tvs.Select(row => row.Name).ShouldBe(["Living Room", "Bedroom"]);
        tvs.Tvs[0].Detail.ShouldStartWith("10.0.0.5");
        tvs.Tvs[0].Detail.ShouldEndWith("connects without a code");
        tvs.Tvs[1].Detail.ShouldEndWith("needs its code next time");
        tvs.Tvs[0].ForgetName.ShouldBe("Forget Living Room");

        tvs.Tvs[0].ForgetCommand.Execute(null);

        tvs.Tvs.ShouldHaveSingleItem().Name.ShouldBe("Bedroom");
        store.Load().ShouldHaveSingleItem().Name.ShouldBe("Bedroom");
        Should.Throw<ArgumentNullException>(() => new KnownTvRow(null!, () => { }));
        Should.Throw<ArgumentNullException>(() => new KnownTvRow(store.Load()[0], null!));
    }

    [Fact]
    public void Tvs_ForgetAll_ForgetsEveryTvAndAddress()
    {
        var store = new InMemoryKnownTvStore();
        store.Save(new KnownTv("Living Room", "10.0.0.5", 47855, null, Now));
        using var shell = MainWindowViewModel.CreateWith(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()),
            addressStore: new SettingsSectionsTests.ListAddressStore(new RecentAddress("192.168.1.42", 8009)),
            knownTvs: store);
        var tvs = shell.Settings.Section<TvSettingsViewModel>();

        tvs.ForgetAll.AskCommand.Execute(null);
        tvs.ForgetAll.ConfirmCommand.Execute(null);

        tvs.HasTvs.ShouldBeFalse();
        tvs.HasRecentAddresses.ShouldBeFalse();
        tvs.HasAnything.ShouldBeFalse();
        store.Load().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheShell_StartsByReconnecting_AndProbesWhenThereIsNothingToReconnectTo()
    {
        using var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        shell.Cast.ManualAddress = "192.168.1.42";

        await shell.StartAsync();

        // This test's prober takes no address, so its refusal is the proof the typed address was probed.
        shell.Cast.Failure.ShouldBe("The configured device probe does not accept an address.");
        shell.Cast.IsSessionConnected.ShouldBeFalse();
    }
}
