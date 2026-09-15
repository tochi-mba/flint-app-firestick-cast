using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Protocol;
using Flint.Session.Browser;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// After reconnect the TV can publish orphan tabs before Windows has OPEN'd. Selecting a tab with
/// epoch 0 threw WireFormatException and terminated Flint.App (Event 1026, 2026-09-08).
/// </summary>
public sealed class BrowserPageViewModelTabEpochTests
{
    [Fact]
    public async Task TabsSnapshot_AdoptsReceiverEpochAndMarksSurfaceOpen()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        using (viewModel)
        {
            viewModel.BrowserEpochForTests.ShouldBe(0);
            viewModel.BrowserSurfaceOpenForTests.ShouldBeFalse();

            remote.Cockpit!.PublishTabs(new BrowserTabsSnapshot(
                Epoch: 1_788_820_757_130,
                Revision: 3,
                ActiveTabId: 4,
                Tabs:
                [
                    new BrowserTabSnapshotItem(
                        3, "One", "https://example.test/1", 100, false, false, false, false),
                    new BrowserTabSnapshotItem(
                        4, "Two", "https://example.test/2", 100, false, true, false, false),
                ]));

            viewModel.BrowserEpochForTests.ShouldBe(1_788_820_757_130);
            viewModel.BrowserSurfaceOpenForTests.ShouldBeTrue();
            viewModel.PageStatusLabel.ShouldBe("https://example.test/2");
            viewModel.CanGoBack.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task SelectTab_AfterOrphanTabsSnapshot_UsesAdoptedEpoch()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        using (viewModel)
        {
            const long tvEpoch = 99;
            remote.Cockpit!.PublishTabs(new BrowserTabsSnapshot(
                Epoch: tvEpoch,
                Revision: 1,
                ActiveTabId: 1,
                Tabs:
                [
                    new BrowserTabSnapshotItem(
                        1, "A", "https://a.test/", 100, false, false, false, false),
                    new BrowserTabSnapshotItem(
                        2, "B", "https://b.test/", 100, false, false, false, false),
                ]));

            await viewModel.Tabs.SelectTabCommand.ExecuteAsync(2L);

            var sent = remote.Cockpit.TabRequests.ShouldHaveSingleItem();
            sent.Operation.ShouldBe(BrowserTabOperation.Select);
            sent.TabId.ShouldBe(2);
            remote.Cockpit.LastTabEpoch.ShouldBe(tvEpoch);
        }
    }

    [Fact]
    public async Task SelectTab_WhenEpochStillZero_DoesNotThrow()
    {
        // Defense in depth: even if adoption is skipped, Select must not crash the process.
        var tabs = new BrowserTabsViewModel();
        var cockpit = new RecordingCockpitRemote(() => 0, () => 1);
        cockpit.Announce(BrowserCockpitFeatures.Tabs);
        tabs.Bind(cockpit);
        tabs.Apply(new BrowserTabsSnapshot(
            Epoch: 0,
            Revision: 1,
            ActiveTabId: 1,
            Tabs: [new BrowserTabSnapshotItem(1, "A", "https://a.test/", 0, false, false, false, false)]));

        await tabs.SelectTabCommand.ExecuteAsync(1L);

        cockpit.TabRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task BrowserState_AdoptsCommandWatermarkPastLastAccepted()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        using (viewModel)
        {
            viewModel.NextCommandIdForTests.ShouldBe(1);

            remote.PublishState(new BrowserStateMessage(
                Epoch: 42,
                Revision: 1,
                NavigationId: 7,
                LastAcceptedCommandId: 7,
                LastAcceptedInputSequence: 0,
                LoadState: BrowserLoadState.Loaded,
                Url: "https://example.test/",
                Title: "Example",
                Progress: 100,
                CanGoBack: false,
                CanGoForward: false,
                ViewportWidth: 1920,
                ViewportHeight: 1080,
                PreviewState: BrowserPreviewState.Disabled));

            viewModel.BrowserEpochForTests.ShouldBe(42);
            // Past the receiver's watermark is the invariant — anything at or below it is
            // discarded as STALE_COMMAND. The exact number also moves with the default-on preview
            // request that an adopted surface sends, which is not what this test is about.
            viewModel.NextCommandIdForTests.ShouldBeGreaterThan(7);
            viewModel.BrowserSurfaceOpenForTests.ShouldBeTrue();
        }
    }
}
