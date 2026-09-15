using Flint.App.Services;
using Flint.Protocol;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// WEB HUD load presentation must track the TV. Optimistic local IsLoading after a wire send, or
/// Loaded with a mid-load progress percent, left the desktop on "Loading… 21%" while the Stick was
/// already showing the finished page (2026-09-07).
/// </summary>
public sealed class BrowserPageViewModelLoadingTests
{
    [Fact]
    public async Task LoadedState_WithMidLoadProgress_ClearsLoadingAndShowsUrl()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        using (viewModel)
        {
            remote.PublishState(State(
                load: BrowserLoadState.Loading,
                progress: 21,
                url: "https://www.google.com/search?q=test",
                title: "search"));

            viewModel.IsLoading.ShouldBeTrue();
            viewModel.PageStatusLabel.ShouldBe("Loading… 21%");

            remote.PublishState(State(
                load: BrowserLoadState.Loaded,
                progress: 21,
                url: "https://www.google.com/search?q=test",
                title: "search"));

            viewModel.IsLoading.ShouldBeFalse();
            viewModel.PageProgress.ShouldBe(100);
            viewModel.PageStatusLabel.ShouldBe("https://www.google.com/search?q=test");
            viewModel.PageTitle.ShouldBe("search");
        }
    }

    [Fact]
    public async Task NavigateCommand_DoesNotOptimisticallySetLoading()
    {
        // Wire send ≠ TV ack. A rejected Navigate left IsLoading true forever before this guard.
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        using (viewModel)
        {
            viewModel.Address = "https://example.test/";
            await viewModel.NavigateCommand.ExecuteAsync(null);

            viewModel.IsLoading.ShouldBeFalse();
            viewModel.PageStatusLabel.ShouldBe("https://example.test/");
        }
    }

    [Fact]
    public async Task NavigateThenLoaded_ShowsReadyNotStuckProgress()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        using (viewModel)
        {
            viewModel.Address = "https://example.test/a";
            await viewModel.NavigateCommand.ExecuteAsync(null);
            remote.PublishState(State(BrowserLoadState.Loading, 40, "https://example.test/a", "A"));
            remote.PublishState(State(BrowserLoadState.Loaded, 40, "https://example.test/a", "A"));

            viewModel.IsLoading.ShouldBeFalse();
            viewModel.PageProgress.ShouldBe(100);
            viewModel.PageStatusLabel.ShouldBe("https://example.test/a");
        }
    }

    [Fact]
    public async Task ActiveTabSnapshot_NotLoading_ClearsHudEvenWithoutFreshPageState()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        using (viewModel)
        {
            remote.PublishState(State(
                BrowserLoadState.Loading,
                21,
                "https://www.google.com/",
                "Google"));
            viewModel.IsLoading.ShouldBeTrue();

            var cockpit = new RecordingCockpitRemote();
            viewModel.AttachCockpit(cockpit);
            cockpit.PublishTabs(new BrowserTabsSnapshot(
                Epoch: 1,
                Revision: 2,
                ActiveTabId: 3,
                Tabs:
                [
                    new BrowserTabSnapshotItem(
                        3,
                        "BOOTY BUTT CHEEKS - GOOGLE SEARCH",
                        "https://www.google.com/search?q=bunda",
                        Progress: 21,
                        IsLoading: false,
                        CanGoBack: true,
                        CanGoForward: false,
                        IsFrozen: false),
                ]));

            viewModel.IsLoading.ShouldBeFalse();
            viewModel.PageProgress.ShouldBe(100);
            viewModel.PageStatusLabel.ShouldBe("https://www.google.com/search?q=bunda");
            viewModel.PageTitle.ShouldBe("BOOTY BUTT CHEEKS - GOOGLE SEARCH");
            viewModel.CanGoBack.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task LoadingProgressUpdates_RefreshPageStatusLabel()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        using (viewModel)
        {
            remote.PublishState(State(BrowserLoadState.Loading, 0, "https://example.test/", null));
            viewModel.PageStatusLabel.ShouldBe("Loading… 0%");

            remote.PublishState(State(BrowserLoadState.Loading, 67, "https://example.test/", null));
            viewModel.PageStatusLabel.ShouldBe("Loading… 67%");
        }
    }

    private static BrowserStateMessage State(
        BrowserLoadState load,
        int progress,
        string url,
        string? title) =>
        new(
            Epoch: 1,
            Revision: 1,
            NavigationId: 1,
            LastAcceptedCommandId: 1,
            LastAcceptedInputSequence: 0,
            LoadState: load,
            Url: url,
            Title: title ?? string.Empty,
            Progress: progress,
            CanGoBack: false,
            CanGoForward: false,
            ViewportWidth: 1920,
            ViewportHeight: 1080,
            PreviewState: BrowserPreviewState.Disabled);
}
