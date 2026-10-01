using System.Reflection;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Protocol;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// Tab chips must reclaim the TV when Screen mirroring owns the glass. Starting Mirror runs
/// PrepareFor and Closes the browser; clicking a tab without PrepareFor left the stick on the
/// mirror while Windows still showed the strip (2026-09-08).
/// </summary>
public sealed class BrowserPageViewModelTabSurfaceTests
{
    [Fact]
    public async Task ClosedSurface_Move_DoesNotTakeTheTvOrSendADestroyedTabId()
    {
        var host = new ClosedTabHost(takeResult: false);
        var restorer = new BrowserTabSessionRestorer(host);

        var shouldSend = await restorer.BeforeTabCommandAsync(
            new BrowserTabRequest(BrowserTabOperation.Move, TabId: 7, Position: 1),
            TestContext.Current.CancellationToken);

        shouldSend.ShouldBeFalse();
        host.TakeCalls.ShouldBe(0);
        host.Opened.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(99, 1)]
    public async Task ClosedSurface_Close_RemovesOnlyTheHeldTabWithoutTakingTheTv(
        long closing,
        long expectedActive)
    {
        var host = new ClosedTabHost(takeResult: true);
        host.Tabs.Apply(new BrowserTabsSnapshot(
            Epoch: 1,
            Revision: 1,
            ActiveTabId: 1,
            Tabs:
            [
                new BrowserTabSnapshotItem(1, "One", "https://example.test/one", 100, false, false, false, false),
                new BrowserTabSnapshotItem(2, "Two", "https://example.test/two", 100, false, false, false, false),
            ]));
        var restorer = new BrowserTabSessionRestorer(host);
        restorer.Capture();

        var shouldSend = await restorer.BeforeTabCommandAsync(
            new BrowserTabRequest(BrowserTabOperation.Close, TabId: closing),
            TestContext.Current.CancellationToken);

        shouldSend.ShouldBeFalse();
        host.TakeCalls.ShouldBe(0);
        host.Tabs.ActiveId.ShouldBe(expectedActive);
        host.Tabs.Items.ShouldNotContain(tab => tab.Id == closing && closing != 99);
        host.Tabs.Items.Count.ShouldBe(closing == 99 ? 2 : 1);
    }

    [Fact]
    public async Task ClosedSurface_CloseOfTheOnlyTab_LeavesAnEmptyHeldStrip()
    {
        var host = new ClosedTabHost(takeResult: true);
        host.Tabs.Apply(new BrowserTabsSnapshot(
            Epoch: 1,
            Revision: 1,
            ActiveTabId: 1,
            Tabs:
            [
                new BrowserTabSnapshotItem(1, "One", "https://example.test/one", 100, false, false, false, false),
            ]));
        var restorer = new BrowserTabSessionRestorer(host);
        restorer.Capture();

        await restorer.BeforeTabCommandAsync(
            new BrowserTabRequest(BrowserTabOperation.Close, TabId: 1),
            TestContext.Current.CancellationToken);

        host.Tabs.Items.ShouldBeEmpty();
        host.Tabs.ActiveId.ShouldBe(0);
    }

    [Fact]
    public async Task SelectTab_WhileMirroring_StopsMirrorAndReopensTabUrl()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        var cast = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice())).Cast;
        var coordinator = new ModeSessionCoordinator(cast, viewModel);

        viewModel.Address = "https://example.test/search?q=bunda";
        await viewModel.NavigateCommand.ExecuteAsync(null);
        viewModel.HasOpenBrowserSurface.ShouldBeTrue();

        remote.Cockpit!.PublishTabs(new BrowserTabsSnapshot(
            Epoch: viewModel.BrowserEpochForTests,
            Revision: 1,
            ActiveTabId: 2,
            Tabs:
            [
                new BrowserTabSnapshotItem(
                    1, "One", "https://example.test/one", 100, false, false, false, false),
                new BrowserTabSnapshotItem(
                    2, "Search", "https://example.test/search?q=bunda", 100, false, false, false, false),
            ]));
        viewModel.Tabs.Items.Count.ShouldBe(2);

        await coordinator.PrepareForAsync(TvSurfaceKind.Mirror, TestContext.Current.CancellationToken);
        viewModel.HasOpenBrowserSurface.ShouldBeFalse();
        SetIsMirroring(cast, true);

        var opensBefore = remote.Commands.Count(c => c.Action == BrowserCommandAction.Open);
        await viewModel.Tabs.SelectTabCommand.ExecuteAsync(1);

        cast.IsMirroring.ShouldBeFalse(
            "SelectTab must PrepareFor(Browser), which stops Screen mirroring.");
        viewModel.HasOpenBrowserSurface.ShouldBeTrue();
        remote.Commands.Count(c => c.Action == BrowserCommandAction.Open).ShouldBe(opensBefore + 1);
        remote.Commands.ShouldContain(c =>
            c.Action == BrowserCommandAction.Open && c.Url == "https://example.test/one");
        remote.Cockpit.TabRequests.ShouldNotContain(r =>
            r.Operation == BrowserTabOperation.Select && r.TabId == 1);
        // Sibling URLs from the app-held profile session must come back too - not only the click.
        remote.Cockpit.TabRequests.ShouldContain(r =>
            r.Operation == BrowserTabOperation.New
            && r.Url == "https://example.test/search?q=bunda");
    }

    [Fact]
    public async Task SelectTab_WhileMirroring_RestoresAllSiblingTabsFromAppSession()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        var cast = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice())).Cast;
        var coordinator = new ModeSessionCoordinator(cast, viewModel);

        viewModel.Address = "https://example.test/a";
        await viewModel.NavigateCommand.ExecuteAsync(null);
        remote.Cockpit!.PublishTabs(new BrowserTabsSnapshot(
            Epoch: viewModel.BrowserEpochForTests,
            Revision: 1,
            ActiveTabId: 2,
            Tabs:
            [
                new BrowserTabSnapshotItem(
                    1, "A", "https://example.test/a", 100, false, false, false, false),
                new BrowserTabSnapshotItem(
                    2, "B", "https://example.test/b", 100, false, false, false, false),
                new BrowserTabSnapshotItem(
                    3, "C", "https://example.test/c", 100, false, false, false, false),
            ]));

        await coordinator.PrepareForAsync(TvSurfaceKind.Mirror, TestContext.Current.CancellationToken);
        SetIsMirroring(cast, true);

        await viewModel.Tabs.SelectTabCommand.ExecuteAsync(2);

        remote.Commands.ShouldContain(c =>
            c.Action == BrowserCommandAction.Open && c.Url == "https://example.test/b");
        remote.Cockpit.TabRequests.Count(r =>
            r.Operation == BrowserTabOperation.New).ShouldBe(2);
        remote.Cockpit.TabRequests.ShouldContain(r =>
            r.Operation == BrowserTabOperation.New && r.Url == "https://example.test/a");
        remote.Cockpit.TabRequests.ShouldContain(r =>
            r.Operation == BrowserTabOperation.New && r.Url == "https://example.test/c");
    }

    [Fact]
    public async Task SelectTab_WhileMirroring_RestoresDistinctGoogleSearchQueries()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        var cast = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice())).Cast;
        var coordinator = new ModeSessionCoordinator(cast, viewModel);

        viewModel.Address = "https://www.google.com/search?q=bunda";
        await viewModel.NavigateCommand.ExecuteAsync(null);
        remote.Cockpit!.PublishTabs(new BrowserTabsSnapshot(
            Epoch: viewModel.BrowserEpochForTests,
            Revision: 1,
            ActiveTabId: 1,
            Tabs:
            [
                new BrowserTabSnapshotItem(
                    1, "bunda", "https://www.google.com/search?q=bunda", 100, false, false, false, false),
                new BrowserTabSnapshotItem(
                    2, "cats", "https://www.google.com/search?q=cats", 100, false, false, false, false),
            ]));

        await coordinator.PrepareForAsync(TvSurfaceKind.Mirror, TestContext.Current.CancellationToken);
        SetIsMirroring(cast, true);

        await viewModel.Tabs.SelectTabCommand.ExecuteAsync(1);

        remote.Commands.ShouldContain(c =>
            c.Action == BrowserCommandAction.Open
            && c.Url == "https://www.google.com/search?q=bunda");
        remote.Cockpit.TabRequests.ShouldContain(r =>
            r.Operation == BrowserTabOperation.New
            && r.Url == "https://www.google.com/search?q=cats");
        remote.Cockpit.TabRequests.ShouldNotContain(r =>
            r.Operation == BrowserTabOperation.New
            && r.Url == "https://www.google.com/search?q=bunda");
    }

    [Fact]
    public async Task SelectTab_WhileBrowserSurfaceStillOpen_SendsSelectWithoutSecondOpen()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        var cast = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice())).Cast;
        _ = new ModeSessionCoordinator(cast, viewModel);

        viewModel.Address = "https://example.test/a";
        await viewModel.NavigateCommand.ExecuteAsync(null);
        remote.Cockpit!.PublishTabs(new BrowserTabsSnapshot(
            Epoch: viewModel.BrowserEpochForTests,
            Revision: 1,
            ActiveTabId: 1,
            Tabs:
            [
                new BrowserTabSnapshotItem(
                    1, "A", "https://example.test/a", 100, false, false, false, false),
                new BrowserTabSnapshotItem(
                    2, "B", "https://example.test/b", 100, false, false, false, false),
            ]));

        var opensBefore = remote.Commands.Count(c => c.Action == BrowserCommandAction.Open);
        await viewModel.Tabs.SelectTabCommand.ExecuteAsync(2);

        remote.Cockpit.TabRequests.ShouldContain(r =>
            r.Operation == BrowserTabOperation.Select && r.TabId == 2);
        remote.Commands.Count(c => c.Action == BrowserCommandAction.Open).ShouldBe(opensBefore);
    }

    [Fact]
    public async Task NewTab_WhileMirroring_StopsMirrorAndOpensStartPage()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        var cast = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice())).Cast;
        var coordinator = new ModeSessionCoordinator(cast, viewModel);

        viewModel.Address = "https://example.test/a";
        await viewModel.NavigateCommand.ExecuteAsync(null);
        remote.Cockpit!.PublishTabs(new BrowserTabsSnapshot(
            Epoch: viewModel.BrowserEpochForTests,
            Revision: 1,
            ActiveTabId: 1,
            Tabs:
            [
                new BrowserTabSnapshotItem(
                    1, "A", "https://example.test/a", 100, false, false, false, false),
            ]));

        await coordinator.PrepareForAsync(TvSurfaceKind.Mirror, TestContext.Current.CancellationToken);
        SetIsMirroring(cast, true);

        await viewModel.Tabs.NewTabCommand.ExecuteAsync(null);

        cast.IsMirroring.ShouldBeFalse();
        viewModel.HasOpenBrowserSurface.ShouldBeTrue();
        remote.Commands.ShouldContain(c =>
            c.Action == BrowserCommandAction.Open && c.Url == "https://www.google.com/");
        remote.Cockpit.TabRequests.ShouldNotContain(r => r.Operation == BrowserTabOperation.New);
    }

    [Fact]
    public async Task Reload_WhileMirroringAfterClose_LeavesTheMirrorAloneAndSendsNothing()
    {
        // Reload used to take the glass first, which stopped the mirror, and only then find there
        // was no page to reload: the TV lost the mirror for a command that did nothing.
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        var cast = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice())).Cast;
        var coordinator = new ModeSessionCoordinator(cast, viewModel);

        viewModel.Address = "https://example.test/a";
        await viewModel.NavigateCommand.ExecuteAsync(null);
        await coordinator.PrepareForAsync(TvSurfaceKind.Mirror, TestContext.Current.CancellationToken);
        SetIsMirroring(cast, true);

        var before = remote.Commands.Count;
        await viewModel.ReloadCommand.ExecuteAsync(null);

        cast.IsMirroring.ShouldBeTrue();
        remote.Commands.Skip(before).ShouldBeEmpty();
    }

    private static void SetIsMirroring(CastPageViewModel cast, bool value)
    {
        var field = typeof(CastPageViewModel).GetField(
            "_isMirroring",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.ShouldNotBeNull();
        field!.SetValue(cast, value);
    }

    private sealed class ClosedTabHost(bool takeResult) : IBrowserTabSessionHost
    {
        public bool SurfaceOpen => false;

        public BrowserTabsViewModel Tabs { get; } = new();

        public int TakeCalls { get; private set; }

        public List<string> Opened { get; } = [];

        public Task<bool> TakeBrowserGlassAsync(CancellationToken cancellationToken)
        {
            TakeCalls++;
            return Task.FromResult(takeResult);
        }

        public Task OpenAddressAsync(string url, CancellationToken cancellationToken)
        {
            Opened.Add(url);
            return Task.CompletedTask;
        }
    }
}
