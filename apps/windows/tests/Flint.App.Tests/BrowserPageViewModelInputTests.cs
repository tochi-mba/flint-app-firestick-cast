using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Protocol;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// Sending remote input from the Windows shell to the television's browser.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of the feature the product exists for. The receiver renders the page and the
/// desktop is the keyboard and pointer driving it; a session that can navigate but not type is a
/// browser you can point at one URL and never use.
/// </para>
/// <para>
/// Every one of these asserts something the reducers on the receiver cannot: the receiver rejects a
/// stale sequence or a wrong epoch, and it is this side's job never to send one.
/// </para>
/// </remarks>
public sealed class BrowserPageViewModelInputTests
{
    [Fact]
    public async Task SendKey_WithNoSession_SendsNothingRatherThanThrowing()
    {
        // Keyboard focus can sit on the page before a session exists. Throwing here would surface a
        // dialog for pressing an arrow key on an idle screen.
        var viewModel = BrowserFixtures.ViewModel();

        await viewModel.SendKeyCommand.ExecuteAsync(BrowserSemanticKey.Down);

        viewModel.SessionPhase.ShouldBe(BrowserUiPhase.Idle);
    }

    [Fact]
    public async Task SendKey_OnAReadySession_SendsOneInputMessage()
    {
        // Arrange
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);

        // Act
        await viewModel.SendKeyCommand.ExecuteAsync(BrowserSemanticKey.Select);

        // Assert
        remote.Inputs.Count.ShouldBe(1);
        var sent = remote.Inputs.Single();
        sent.Event.ShouldBeOfType<BrowserSemanticKeyInput>().Key.ShouldBe(BrowserSemanticKey.Select);
    }

    [Fact]
    public async Task SendKey_NumbersEachInputWithAStrictlyIncreasingSequence()
    {
        // The receiver drops any input whose sequence does not advance, so a repeated or reused
        // number is a key that silently does nothing — and looks like a dropped packet.
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);

        await viewModel.SendKeyCommand.ExecuteAsync(BrowserSemanticKey.Down);
        await viewModel.SendKeyCommand.ExecuteAsync(BrowserSemanticKey.Down);
        await viewModel.SendKeyCommand.ExecuteAsync(BrowserSemanticKey.Up);

        var sequences = remote.Inputs.Select(input => input.Sequence).ToArray();
        sequences.ShouldBe(sequences.OrderBy(value => value).ToArray());
        sequences.Distinct().Count().ShouldBe(sequences.Length);
    }

    [Fact]
    public async Task SendKey_CarriesTheEpochTheSessionWasOpenedWith()
    {
        // An input tagged with the wrong epoch belongs to a page that is gone; the receiver rejects
        // it, correctly, and the user sees a dead keyboard.
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        await viewModel.NavigateCommand.ExecuteAsync(null);

        await viewModel.SendKeyCommand.ExecuteAsync(BrowserSemanticKey.Down);

        var expected = remote.Commands.Last().Epoch;
        remote.Inputs.Single().Epoch.ShouldBe(expected);
    }

    [Fact]
    public async Task SendText_SendsTheTextAsOneMessage()
    {
        // One message rather than one per character: a search phrase typed a key at a time is a
        // round trip per letter, which is visible as lag on a remote page.
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);

        await viewModel.SendTextCommand.ExecuteAsync("hello world");

        remote.Inputs.Count.ShouldBe(1);
        remote.Inputs.Single().Event.ShouldBeOfType<BrowserTextInput>().Text.ShouldBe("hello world");
    }

    [Fact]
    public async Task SendText_WithEmptyText_SendsNothing()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);

        await viewModel.SendTextCommand.ExecuteAsync(string.Empty);

        remote.Inputs.ShouldBeEmpty();
    }

    [Fact]
    public async Task SendKey_AfterTheSessionDrops_StopsSendingRatherThanFaulting()
    {
        // A television that goes to sleep mid-session drops the socket. Every later keystroke must
        // become a no-op, not an unhandled exception on the UI thread.
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        remote.IsConnected = false;

        await viewModel.SendKeyCommand.ExecuteAsync(BrowserSemanticKey.Down);

        remote.Inputs.ShouldBeEmpty();
    }

    [Fact]
    public async Task SendKey_WhenTheRemoteFails_ReportsItWithoutTearingDownTheApp()
    {
        // A send can fail for ordinary reasons. The page should say so and stay usable.
        var remote = new RecordingBrowserRemote { ThrowOnSend = true };
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);

        await viewModel.SendKeyCommand.ExecuteAsync(BrowserSemanticKey.Down);

        viewModel.LastError.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task HistoryCommands_SendTheirOwnActions()
    {
        // Back and forward are history moves, not navigations to a URL: sending Navigate with the
        // previous address would push a new entry and make Back walk forwards forever.
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);

        remote.PublishState(new BrowserStateMessage(
            Epoch: 1, Revision: 1, NavigationId: 1, LastAcceptedCommandId: 1,
            LastAcceptedInputSequence: 0, LoadState: BrowserLoadState.Loading,
            Url: "https://example.test/", Title: "Example", Progress: 42,
            CanGoBack: true, CanGoForward: true, ViewportWidth: 1920, ViewportHeight: 1080,
            PreviewState: BrowserPreviewState.Disabled));

        remote.Commands.Clear(); // Exclude the initial preview preference synchronization.
        await viewModel.GoBackCommand.ExecuteAsync(null);
        await viewModel.GoForwardCommand.ExecuteAsync(null);
        await viewModel.ReloadCommand.ExecuteAsync(null);
        await viewModel.StopLoadingCommand.ExecuteAsync(null);

        remote.Commands.Select(command => command.Action).ShouldBe(
        [
            BrowserCommandAction.Back,
            BrowserCommandAction.Forward,
            BrowserCommandAction.Reload,
            BrowserCommandAction.Stop,
        ]);
    }

    [Fact]
    public async Task HistoryCommands_NumberEachCommandDistinctly()
    {
        // The receiver de-duplicates by command id, so a reused number is a button that works once.
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);

        await viewModel.GoBackCommand.ExecuteAsync(null);
        await viewModel.ReloadCommand.ExecuteAsync(null);

        var ids = remote.Commands.Select(command => command.CommandId).ToArray();
        ids.Distinct().Count().ShouldBe(ids.Length);
    }

    [Fact]
    public async Task HistoryCommands_WithNoSession_DoNothing()
    {
        var viewModel = BrowserFixtures.ViewModel();

        await viewModel.GoBackCommand.ExecuteAsync(null);

        viewModel.SessionPhase.ShouldBe(BrowserUiPhase.Idle);
    }

    [Fact]
    public async Task ReceiverState_UpdatesTheTitleAndProgress()
    {
        // The desktop shows what the television reports rather than guessing from what it sent, so
        // a page that redirects or fails is described accurately.
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);

        remote.PublishState(new BrowserStateMessage(
            Epoch: 1,
            Revision: 1,
            NavigationId: 1,
            LastAcceptedCommandId: 1,
            LastAcceptedInputSequence: 0,
            LoadState: BrowserLoadState.Loading,
            Url: "https://example.test/",
            Title: "Example Domain",
            Progress: 42,
            CanGoBack: true,
            CanGoForward: false,
            ViewportWidth: 1920,
            ViewportHeight: 1080,
            PreviewState: BrowserPreviewState.Disabled));

        viewModel.PageTitle.ShouldBe("Example Domain");
        viewModel.PageProgress.ShouldBe(42);
        viewModel.IsLoading.ShouldBeTrue();
        viewModel.PageStatusLabel.ShouldBe("Loading… 42%");
        viewModel.CanGoBack.ShouldBeTrue();
        viewModel.CanGoForward.ShouldBeFalse();
    }
    [Fact]
    public void ViewControls_WhenTheTvReportsAFocusedField_ArmsKeyboardForwardingAutomatically()
    {
        // Typing on a television is the slowest thing this feature asks of anyone. The moment the
        // TV says a field has focus, the desktop keyboard should already be pointed at it — nobody
        // should have to find and click a capture pad first.
        var page = BrowserFixtures.ViewModel();
        page.KeyboardCaptureEnabled.ShouldBeFalse();

        page.ViewControls.Apply(EditingSnapshot(isEditing: true));

        page.KeyboardCaptureEnabled.ShouldBeTrue();
        page.KeyboardCaptureLabel.ShouldContain("TV");
    }

    [Fact]
    public void ViewControls_WhenTheFieldLosesFocus_StopsForwardingAgain()
    {
        // Leaving it armed would keep sending keystrokes at a page with nothing to receive them.
        var page = BrowserFixtures.ViewModel();
        page.ViewControls.Apply(EditingSnapshot(isEditing: true));

        page.ViewControls.Apply(EditingSnapshot(isEditing: false));

        page.KeyboardCaptureEnabled.ShouldBeFalse();
    }

    [Fact]
    public void ViewControls_ManualCapture_IsNotUndoneByAnUnrelatedViewUpdate()
    {
        // Someone who armed it deliberately keeps it armed; a zoom change must not switch it off.
        var page = BrowserFixtures.ViewModel();
        page.ToggleKeyboardCaptureCommand.Execute(null);

        page.ViewControls.Apply(EditingSnapshot(isEditing: false));

        page.KeyboardCaptureEnabled.ShouldBeTrue();
    }

    private static BrowserViewSnapshot EditingSnapshot(bool isEditing) => new(
        Epoch: 1,
        Revision: 1,
        ZoomPercent: 125,
        UserAgent: Flint.App.Services.BrowserUserAgentMode.Tv,
        DarkMode: Flint.App.Services.BrowserDarkMode.Off,
        InputMode: BrowserInputMode.Cursor,
        IsFullscreen: false,
        IsEditing: isEditing,
        IsFindActive: false,
        FindCurrent: 0,
        FindTotal: 0);
}
