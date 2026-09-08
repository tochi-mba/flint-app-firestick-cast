using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Session.Browser;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// GO / Navigate must track a *live* TLS session — SecureReady with a dead remote leaves the HUD
/// up and the button disabled forever (seen after receiver reinstall + re-pair).
/// </summary>
public sealed class BrowserPageViewModelGoEnabledTests
{
    [Fact]
    public async Task NavigateCommand_DisabledWhenSecureSessionDiesAndCannotReconnect()
    {
        var remote = new RecordingBrowserRemote();
        var (viewModel, cast, _) = await ReadyAsync(remote);
        using (viewModel)
        {
            viewModel.CanNavigate.ShouldBeTrue();
            viewModel.NavigateCommand.CanExecute(null).ShouldBeTrue();

            // Without a pairing code, auto-reconnect cannot run — GO must stay off, not stuck
            // SecureReady with a dead socket.
            cast.PairingCode = string.Empty;
            remote.EndSession();
            await PumpAsync();

            viewModel.SessionPhase.ShouldBe(BrowserUiPhase.Idle);
            viewModel.CanNavigate.ShouldBeFalse();
            viewModel.NavigateCommand.CanExecute(null).ShouldBeFalse();
            viewModel.IsLoading.ShouldBeFalse();
            viewModel.ShowNavigation.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task SessionDeath_WithRememberedTrust_AutoReconnectsAndReenablesGo()
    {
        var remote = new RecordingBrowserRemote();
        var (viewModel, _, connector) = await ReadyWithRememberedTrustAsync(remote);
        using (viewModel)
        {
            connector.ConnectCount.ShouldBe(1);
            viewModel.CanNavigate.ShouldBeTrue();

            remote.EndSession();
            await PumpAsync();

            connector.ConnectCount.ShouldBeGreaterThanOrEqualTo(2);
            viewModel.SessionPhase.ShouldBe(BrowserUiPhase.SecureReady);
            viewModel.CanNavigate.ShouldBeTrue();
            viewModel.NavigateCommand.CanExecute(null).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task ReconnectPass_WhenSecureReadyButDisconnected_ReenablesGo()
    {
        // Cast re-pair updates the browser port while the UI is still SecureReady against a dead
        // socket — the reconnect pass must notice !IsConnected before requiring Idle.
        var remote = new RecordingBrowserRemote();
        var (viewModel, _, connector) = await ReadyWithRememberedTrustAsync(remote);
        using (viewModel)
        {
            connector.ConnectCount.ShouldBe(1);

            remote.IsConnected = false;
            await viewModel.RefreshRememberedTrustAndMaybeReconnectForTestsAsync();
            await PumpAsync();

            connector.ConnectCount.ShouldBe(2);
            viewModel.SessionPhase.ShouldBe(BrowserUiPhase.SecureReady);
            viewModel.CanNavigate.ShouldBeTrue();
            viewModel.NavigateCommand.CanExecute(null).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task ActivateAsync_AfterForcedIdle_Reconnects()
    {
        var remote = new RecordingBrowserRemote();
        var (viewModel, cast, connector) = await ReadyWithRememberedTrustAsync(remote);
        using (viewModel)
        {
            cast.PairingCode = string.Empty;
            remote.EndSession();
            await PumpAsync();
            viewModel.SessionPhase.ShouldBe(BrowserUiPhase.Idle);
            viewModel.CanNavigate.ShouldBeFalse();

            cast.PairingCode = "123456";
            await viewModel.ActivateAsync();
            await PumpAsync();

            connector.ConnectCount.ShouldBeGreaterThanOrEqualTo(2);
            viewModel.CanNavigate.ShouldBeTrue();
            viewModel.NavigateCommand.CanExecute(null).ShouldBeTrue();
        }
    }

    private static async Task<(BrowserPageViewModel ViewModel, CastPageViewModel Cast, RecordingBrowserSessionConnector Connector)> ReadyAsync(
        RecordingBrowserRemote remote)
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        await shell.Cast.ProbeCommand.ExecuteAsync(null);
        shell.Cast.PairingCode = "123456";

        var connector = new RecordingBrowserSessionConnector(remote);
        var viewModel = new BrowserPageViewModel(
            shell.Cast,
            connector,
            new InMemoryBrowserTrustStore(),
            new ImmediateDispatcher(),
            new InMemoryBrowserProfileLibraryStore(),
            new BrowserHelpViewModel(false));
        await viewModel.VerifySecureReceiverCommand.ExecuteAsync(null);
        return (viewModel, shell.Cast, connector);
    }

    private static async Task<(BrowserPageViewModel ViewModel, CastPageViewModel Cast, RecordingBrowserSessionConnector Connector)> ReadyWithRememberedTrustAsync(
        RecordingBrowserRemote remote)
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        await shell.Cast.ProbeCommand.ExecuteAsync(null);
        shell.Cast.PairingCode = "123456";

        var trust = new InMemoryBrowserTrustStore();
        var endpoint = BrowserEndpointResolver.TryResolve(shell.Cast.Report!.Device!, null)!;
        var identity = BrowserFixtures.Identity(endpoint);
        await trust.SaveAsync(
            new BrowserTrustedReceiver(endpoint.ReceiverIdentity, identity.Fingerprint, DateTimeOffset.UtcNow));

        var connector = new RecordingBrowserSessionConnector(remote);
        var viewModel = new BrowserPageViewModel(
            shell.Cast,
            connector,
            trust,
            new ImmediateDispatcher(),
            new InMemoryBrowserProfileLibraryStore(),
            new BrowserHelpViewModel(false));
        await viewModel.RefreshRememberedTrustAndMaybeReconnectForTestsAsync();
        return (viewModel, shell.Cast, connector);
    }

    private static async Task PumpAsync()
    {
        await Task.Yield();
        await Task.Delay(50);
        await Task.Yield();
    }

    private sealed class ImmediateDispatcher : IBrowserUiDispatcher
    {
        public void Dispatch(Action action) => action();
    }
}
