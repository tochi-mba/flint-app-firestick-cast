using Flint.App.ViewModels;
using Flint.Protocol;
using Shouldly;

namespace Flint.App.Tests;

public sealed class BrowserPageViewModelNavigateEpochTests
{
    [Fact]
    public async Task Navigate_FirstOpen_UsesAdvancingEpoch()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        viewModel.Address = "https://example.test/a";

        await viewModel.NavigateCommand.ExecuteAsync(null);

        // Default-on preview rides along with the first Open, so this asserts the navigation
        // command rather than the size of the batch it travelled in.
        var open = remote.Commands.Single(command => command.Action == BrowserCommandAction.Open);
        open.Epoch.ShouldBeGreaterThan(0);
        open.Url.ShouldBe("https://example.test/a");
    }

    [Fact]
    public async Task Navigate_AfterClose_OpensWithNewerEpoch()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        viewModel.Address = "https://example.test/first";
        await viewModel.NavigateCommand.ExecuteAsync(null);
        var firstEpoch = remote.Commands.Last().Epoch;

        await viewModel.CloseBrowserCommand.ExecuteAsync(null);
        viewModel.Address = "https://example.test/second";
        await viewModel.NavigateCommand.ExecuteAsync(null);

        var secondOpen = remote.Commands.Last(command => command.Action == BrowserCommandAction.Open);
        secondOpen.Epoch.ShouldBeGreaterThan(firstEpoch);
        secondOpen.Url.ShouldBe("https://example.test/second");
    }

    [Fact]
    public async Task Navigate_SecondGo_UsesNavigateNotOpen()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        viewModel.Address = "https://example.test/first";
        await viewModel.NavigateCommand.ExecuteAsync(null);

        viewModel.Address = "https://example.test/second";
        await viewModel.NavigateCommand.ExecuteAsync(null);

        // The second address must not mint a new epoch: the surface is already open, so it is a
        // Navigate within the same session, not a fresh Open.
        var open = remote.Commands.Single(command => command.Action == BrowserCommandAction.Open);
        var navigate = remote.Commands.Single(command => command.Action == BrowserCommandAction.Navigate);
        navigate.Epoch.ShouldBe(open.Epoch);
        navigate.Url.ShouldBe("https://example.test/second");
    }

    [Fact]
    public async Task Navigate_SearchText_ResolvesToGoogleHostOpen()
    {
        // Matches the 2026-09-07 session: typing a search becomes Open(www.google.com). If the TV
        // refuses that Open, nothing appears on screen — covered on the receiver reclaim tests.
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        viewModel.Address = "weather today";

        await viewModel.NavigateCommand.ExecuteAsync(null);

        var open = remote.Commands.Single(command => command.Action == BrowserCommandAction.Open);
        open.Url.ShouldNotBeNull().ShouldContain("google.", Case.Insensitive);
    }
}
