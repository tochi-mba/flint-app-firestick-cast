using Flint.Protocol;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// Controls must either act or be plainly unavailable — never look live and do nothing.
/// </summary>
/// <remarks>
/// The receiver drops page commands when no browser surface is open. The host offered them anyway,
/// so Reload and Stop were clickable over nothing, and — the one that mattered — CLEAR TV BROWSER
/// DATA ran its destructive confirmation and then silently dropped the command. The receiver
/// exempts clear-data from both of its own gates precisely so that erasing data always works; the
/// host was the only thing standing in the way.
/// </remarks>
public sealed class BrowserPageViewModelSurfaceGuardTests
{
    [Fact]
    public async Task ClearData_WithNoPageOpen_StillReachesTheTelevision()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        viewModel.BrowserSurfaceOpenForTests.ShouldBeFalse();
        remote.Commands.Clear();

        await viewModel.ConfirmClearDataCommand.ExecuteAsync(null);

        remote.Commands.ShouldContain(command => command.Action == BrowserCommandAction.ClearData);
        viewModel.ClearDataConfirmationVisible.ShouldBeFalse();
    }

    [Fact]
    public async Task ClearData_DoesNotTakeTheTelevisionsGlass()
    {
        // Erasing browsing data is not a reason to interrupt whatever is on screen, and taking the
        // glass would close a mirror the person is watching.
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        remote.Commands.Clear();

        await viewModel.ConfirmClearDataCommand.ExecuteAsync(null);

        remote.Commands.ShouldNotContain(command => command.Action == BrowserCommandAction.Open);
    }

    [Fact]
    public async Task ReloadAndStop_AreUnavailableUntilAPageIsOpen()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);

        viewModel.CanNavigate.ShouldBeTrue();
        viewModel.ReloadCommand.CanExecute(null).ShouldBeFalse();
        viewModel.StopLoadingCommand.CanExecute(null).ShouldBeFalse();
        viewModel.CloseBrowserCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task ReloadAndStop_BecomeAvailableOnceAPageIsOpen()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        viewModel.Address = "https://example.test/a";

        await viewModel.NavigateCommand.ExecuteAsync(null);

        viewModel.ReloadCommand.CanExecute(null).ShouldBeTrue();
        viewModel.StopLoadingCommand.CanExecute(null).ShouldBeTrue();
        viewModel.CloseBrowserCommand.CanExecute(null).ShouldBeTrue();
    }
}
