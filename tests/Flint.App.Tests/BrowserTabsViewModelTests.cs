using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Session.Browser;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// The + tab control is bound to <see cref="BrowserTabsViewModel.NewTabCommand"/>, not to
/// <see cref="BrowserTabsViewModel.IsAvailable"/>. If CanExecute is not refreshed when the first
/// tabs snapshot arrives, the button stays disabled while the strip looks live.
/// </summary>
public sealed class BrowserTabsViewModelTests
{
    [Fact]
    public void NewTabCommand_DisabledBeforeTabsSnapshot()
    {
        var tabs = new BrowserTabsViewModel();
        var cockpit = new RecordingCockpitRemote();
        tabs.Bind(cockpit);

        tabs.IsAvailable.ShouldBeFalse();
        tabs.NewTabCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Apply_FirstTabsSnapshot_EnablesNewTabCommand()
    {
        // Defect: IsAvailable flipped true and the strip rendered, but NewTabCommand stayed at the
        // CanExecute=false evaluated while unavailable — the + button looked dead forever.
        var tabs = new BrowserTabsViewModel();
        var cockpit = new RecordingCockpitRemote();
        cockpit.Announce(BrowserCockpitFeatures.Tabs);
        tabs.Bind(cockpit);

        tabs.NewTabCommand.CanExecute(null).ShouldBeFalse();

        tabs.Apply(Snapshot(1));

        tabs.IsAvailable.ShouldBeTrue();
        tabs.CanCreate.ShouldBeTrue();
        tabs.Items.Count.ShouldBe(1);
        tabs.NewTabCommand.CanExecute(null).ShouldBeTrue();
        tabs.SelectTabCommand.CanExecute(1L).ShouldBeTrue();
        tabs.CloseTabCommand.CanExecute(1L).ShouldBeTrue();
        tabs.DuplicateTabCommand.CanExecute(1L).ShouldBeTrue();
    }

    [Fact]
    public async Task NewTabCommand_SendsNewTabRequest()
    {
        var tabs = new BrowserTabsViewModel();
        var cockpit = new RecordingCockpitRemote();
        cockpit.Announce(BrowserCockpitFeatures.Tabs);
        tabs.Bind(cockpit);
        tabs.Apply(Snapshot(1));

        await tabs.NewTabCommand.ExecuteAsync(null);

        cockpit.TabRequests.ShouldHaveSingleItem().Operation.ShouldBe(BrowserTabOperation.New);
    }

    [Fact]
    public void NewTabCommand_DisabledAtEightTabs()
    {
        var tabs = new BrowserTabsViewModel();
        tabs.Bind(new RecordingCockpitRemote());
        var items = Enumerable.Range(1, 8)
            .Select(id => new BrowserTabSnapshotItem(
                id, $"T{id}", $"https://example.test/{id}", 0, false, false, false, false))
            .ToList();
        tabs.Apply(new BrowserTabsSnapshot(1, 1, 1, items));

        tabs.CanCreate.ShouldBeFalse();
        tabs.NewTabCommand.CanExecute(null).ShouldBeFalse();
        tabs.SelectTabCommand.CanExecute(1L).ShouldBeTrue();
    }

    [Fact]
    public void Apply_MarksActiveTabForUi()
    {
        var tabs = new BrowserTabsViewModel();
        tabs.Bind(new RecordingCockpitRemote());
        tabs.Apply(new BrowserTabsSnapshot(
            Epoch: 1,
            Revision: 1,
            ActiveTabId: 2,
            Tabs:
            [
                new BrowserTabSnapshotItem(1, "One", "https://example.test/one", 100, false, false, false, false),
                new BrowserTabSnapshotItem(2, "Two", "https://example.test/two", 100, false, false, false, false),
            ]));

        tabs.Items[0].IsActive.ShouldBeFalse();
        tabs.Items[0].StateLabel.ShouldBe("Ready");
        tabs.Items[1].IsActive.ShouldBeTrue();
        tabs.Items[1].StateLabel.ShouldBe("Active tab");
        tabs.ActiveId.ShouldBe(2);
    }

    [Fact]
    public void Reset_DisablesNewTabCommandAgain()
    {
        var tabs = new BrowserTabsViewModel();
        tabs.Bind(new RecordingCockpitRemote());
        tabs.Apply(Snapshot(1));
        tabs.NewTabCommand.CanExecute(null).ShouldBeTrue();

        tabs.Reset(false);

        tabs.IsAvailable.ShouldBeFalse();
        tabs.NewTabCommand.CanExecute(null).ShouldBeFalse();
        tabs.Items.ShouldBeEmpty();
    }

    [Fact]
    public void CockpitBinding_TabsSnapshot_EnablesNewTabOnPage()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        using var page = new BrowserPageViewModel(
            shell.Cast,
            new RecordingBrowserSessionConnector(new RecordingBrowserRemote()),
            new InMemoryBrowserTrustStore(),
            new ImmediateDispatcher(),
            new InMemoryBrowserProfileLibraryStore(),
            new BrowserHelpViewModel(false));
        var cockpit = new RecordingCockpitRemote();
        page.AttachCockpit(cockpit);

        page.Tabs.NewTabCommand.CanExecute(null).ShouldBeFalse();

        cockpit.PublishTabs(Snapshot(1));

        page.Tabs.IsAvailable.ShouldBeTrue();
        page.Tabs.NewTabCommand.CanExecute(null).ShouldBeTrue();
    }

    private static BrowserTabsSnapshot Snapshot(long id) =>
        new(
            Epoch: 1,
            Revision: 1,
            ActiveTabId: id,
            Tabs:
            [
                new BrowserTabSnapshotItem(
                    id, "Example", "https://example.test/", 100, false, false, false, false),
            ]);

    private sealed class ImmediateDispatcher : IBrowserUiDispatcher
    {
        public void Dispatch(Action action) => action();
    }
}
