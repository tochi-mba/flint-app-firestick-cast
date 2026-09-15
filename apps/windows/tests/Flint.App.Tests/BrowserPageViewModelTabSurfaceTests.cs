using System.Reflection;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Protocol;
using Flint.Session.Browser;
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

        await coordinator.PrepareForAsync(TvSurfaceKind.Mirror);
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
        // Sibling URLs from the app-held profile session must come back too — not only the click.
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

        await coordinator.PrepareForAsync(TvSurfaceKind.Mirror);
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

        await coordinator.PrepareForAsync(TvSurfaceKind.Mirror);
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

        await coordinator.PrepareForAsync(TvSurfaceKind.Mirror);
        SetIsMirroring(cast, true);

        await viewModel.Tabs.NewTabCommand.ExecuteAsync(null);

        cast.IsMirroring.ShouldBeFalse();
        viewModel.HasOpenBrowserSurface.ShouldBeTrue();
        remote.Commands.ShouldContain(c =>
            c.Action == BrowserCommandAction.Open && c.Url == "https://www.google.com/");
        remote.Cockpit.TabRequests.ShouldNotContain(r => r.Operation == BrowserTabOperation.New);
    }

    [Fact]
    public async Task Reload_WhileMirroringAfterClose_DoesNotSendIntoClosedSurface()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        var cast = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice())).Cast;
        var coordinator = new ModeSessionCoordinator(cast, viewModel);

        viewModel.Address = "https://example.test/a";
        await viewModel.NavigateCommand.ExecuteAsync(null);
        await coordinator.PrepareForAsync(TvSurfaceKind.Mirror);
        SetIsMirroring(cast, true);

        var before = remote.Commands.Count;
        await viewModel.ReloadCommand.ExecuteAsync(null);

        cast.IsMirroring.ShouldBeFalse();
        remote.Commands.Skip(before).ShouldNotContain(c => c.Action == BrowserCommandAction.Reload);
    }

    private static void SetIsMirroring(CastPageViewModel cast, bool value)
    {
        var field = typeof(CastPageViewModel).GetField(
            "_isMirroring",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.ShouldNotBeNull();
        field!.SetValue(cast, value);
    }
}
