using Flint.App.ViewModels;
using Flint.Protocol;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// TV preview defaults on when supported; the toggle turns it off for the session.
/// </summary>
public sealed class BrowserPageViewModelPreviewTests
{
    [Fact]
    public async Task ReadySession_WithPreviewCapability_RequestsPreviewByDefault()
    {
        var remote = new RecordingBrowserRemote();
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);

        viewModel.Preview.IsSupported.ShouldBeTrue();
        viewModel.Preview.CanToggle.ShouldBeTrue();
        viewModel.TogglePreviewCommand.CanExecute(null).ShouldBeTrue();
        viewModel.Preview.IsRequested.ShouldBeTrue();
        viewModel.Preview.ToggleLabel.ShouldBe("PREVIEW OFF");
    }

    [Fact]
    public async Task ReadySession_WithoutPreviewCapability_KeepsToggleDisabled()
    {
        var remote = new RecordingBrowserRemote
        {
            Capability = new BrowserCapabilityMessage(
                BrowserCapabilityStatus.Available,
                SecureEndpointPort: 8443,
                ApiLevel: 30,
                WebViewVersion: "test",
                PreviewSupported: false,
                PreviewMaxWidth: 0,
                PreviewMaxHeight: 0,
                InteractivePreviewFramesPerSecond: 0,
                IdlePreviewFramesPerSecond: 0,
                PreviewMaxBytes: 0),
        };
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);

        viewModel.Preview.IsSupported.ShouldBeFalse();
        viewModel.Preview.CanToggle.ShouldBeFalse();
        viewModel.TogglePreviewCommand.CanExecute(null).ShouldBeFalse();
        viewModel.Preview.StatusLabel.ShouldBe("PREVIEW UNAVAILABLE");
    }

    [Fact]
    public async Task Navigate_SendsSetPreviewEnabledTrueByDefault()
    {
        var remote = new RecordingBrowserRemote();
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);

        await viewModel.NavigateCommand.ExecuteAsync(null);

        viewModel.Preview.IsRequested.ShouldBeTrue();
        remote.Commands.ShouldContain(command =>
            command.Action == BrowserCommandAction.Open);
        remote.Commands.ShouldContain(command =>
            command.Action == BrowserCommandAction.SetPreviewEnabled &&
            command.PreviewEnabled == true);
    }

    [Fact]
    public async Task TogglePreview_TurnsDefaultOffThenOnAgain()
    {
        var remote = new RecordingBrowserRemote();
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        await viewModel.NavigateCommand.ExecuteAsync(null);
        remote.Commands.Clear();

        await viewModel.TogglePreviewCommand.ExecuteAsync(null);

        viewModel.Preview.IsRequested.ShouldBeFalse();
        remote.Commands.ShouldContain(command =>
            command.Action == BrowserCommandAction.SetPreviewEnabled &&
            command.PreviewEnabled == false);

        await viewModel.TogglePreviewCommand.ExecuteAsync(null);

        viewModel.Preview.IsRequested.ShouldBeTrue();
        remote.Commands.ShouldContain(command =>
            command.Action == BrowserCommandAction.SetPreviewEnabled &&
            command.PreviewEnabled == true);
    }

    [Fact]
    public async Task WorkspaceMosaic_KeepsDefaultPreviewPreference()
    {
        var remote = new RecordingBrowserRemote();
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        remote.Cockpit!.PublishWorkspace(
            new BrowserWorkspaceSnapshot(
                Epoch: 1,
                Revision: 1,
                Capabilities: new BrowserWorkspaceCapabilities(
                    IsAvailable: true,
                    MaximumVisiblePanes: 4,
                    SupportedLayouts: BrowserWorkspaceLayoutSet.TwoColumns,
                    CanCreatePane: true,
                    CanClosePane: true,
                    CanRequestPaneFocus: true,
                    CanSendFocusedPaneInput: true,
                    CanRequestMediaControl: true,
                    CanRequestTheaterMode: false),
                Layout: BrowserWorkspaceLayout.TwoColumns,
                FocusedPaneId: "one",
                Panes:
                [
                    new BrowserWorkspacePaneSnapshot(
                        PaneId: "one",
                        Slot: 0,
                        Url: "https://one.example.test/",
                        Title: "One",
                        Progress: 100,
                        State: BrowserWorkspacePaneState.Live,
                        Media: BrowserWorkspaceMediaSnapshot.None),
                    new BrowserWorkspacePaneSnapshot(
                        PaneId: "two",
                        Slot: 1,
                        Url: "https://two.example.test/",
                        Title: "Two",
                        Progress: 100,
                        State: BrowserWorkspacePaneState.Live,
                        Media: BrowserWorkspaceMediaSnapshot.None),
                ]));

        viewModel.ShowWorkspaceMosaic.ShouldBeTrue();
        viewModel.Preview.IsRequested.ShouldBeTrue();
        viewModel.TogglePreviewCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task AdoptedOpenSurface_SendsSetPreviewEnabledWithoutNavigate()
    {
        var remote = new RecordingBrowserRemote();
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        remote.Commands.Clear();

        remote.PublishState(new BrowserStateMessage(
            Epoch: 1_788_829_770_152,
            Revision: 1,
            NavigationId: 4,
            LastAcceptedCommandId: 4,
            LastAcceptedInputSequence: 0,
            LoadState: BrowserLoadState.Loaded,
            Url: "https://www.google.com/",
            Title: "Google",
            Progress: 100,
            CanGoBack: false,
            CanGoForward: false,
            ViewportWidth: 1920,
            ViewportHeight: 1080,
            PreviewState: BrowserPreviewState.Disabled));

        await AwaitPreviewCommandAsync(remote);

        viewModel.Preview.IsRequested.ShouldBeTrue();
        viewModel.HasOpenBrowserSurface.ShouldBeTrue();
        remote.Commands.ShouldContain(command =>
            command.Action == BrowserCommandAction.SetPreviewEnabled &&
            command.PreviewEnabled == true &&
            command.Epoch == 1_788_829_770_152);
        remote.Commands.ShouldNotContain(command =>
            command.Action == BrowserCommandAction.Open ||
            command.Action == BrowserCommandAction.Navigate);
    }

    [Fact]
    public async Task AdoptedOpenSurface_DoesNotResendPreviewWhenAlreadyEnabled()
    {
        var remote = new RecordingBrowserRemote();
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        remote.Commands.Clear();

        remote.PublishState(AdoptedState(BrowserPreviewState.Enabled));
        await Task.Delay(80);

        remote.Commands.ShouldNotContain(command =>
            command.Action == BrowserCommandAction.SetPreviewEnabled);
    }

    [Fact]
    public async Task AdoptedOpenSurface_DoesNotForcePreviewWhenUserTurnedItOff()
    {
        var remote = new RecordingBrowserRemote();
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        await viewModel.NavigateCommand.ExecuteAsync(null);
        await viewModel.TogglePreviewCommand.ExecuteAsync(null);
        viewModel.Preview.IsRequested.ShouldBeFalse();
        remote.Commands.Clear();

        remote.PublishState(AdoptedState(BrowserPreviewState.Disabled));
        await Task.Delay(80);

        remote.Commands.ShouldNotContain(command =>
            command.Action == BrowserCommandAction.SetPreviewEnabled);
    }

    [Fact]
    public async Task AdoptedOpenSurface_SyncsPreviewOnlyOnceAcrossRepeatedDisabledStates()
    {
        var remote = new RecordingBrowserRemote();
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        remote.Commands.Clear();

        remote.PublishState(AdoptedState(BrowserPreviewState.Disabled));
        await AwaitPreviewCommandAsync(remote);
        remote.PublishState(AdoptedState(BrowserPreviewState.Disabled) with { Revision = 2 });
        await Task.Delay(80);

        remote.Commands.Count(command =>
                command.Action == BrowserCommandAction.SetPreviewEnabled &&
                command.PreviewEnabled == true)
            .ShouldBe(1);
    }

    private static BrowserStateMessage AdoptedState(BrowserPreviewState preview) =>
        new(
            Epoch: 99,
            Revision: 1,
            NavigationId: 4,
            LastAcceptedCommandId: 4,
            LastAcceptedInputSequence: 0,
            LoadState: BrowserLoadState.Loaded,
            Url: "https://www.google.com/",
            Title: "Google",
            Progress: 100,
            CanGoBack: false,
            CanGoForward: false,
            ViewportWidth: 1920,
            ViewportHeight: 1080,
            PreviewState: preview);

    private static async Task AwaitPreviewCommandAsync(RecordingBrowserRemote remote)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (remote.Commands.Any(command =>
                    command.Action == BrowserCommandAction.SetPreviewEnabled))
            {
                return;
            }

            await Task.Delay(25);
        }
    }
}
