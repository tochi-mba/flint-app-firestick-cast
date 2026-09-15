using Flint.App.ViewModels;
using Shouldly;

namespace Flint.App.Tests;

public sealed class ModeSessionCoordinatorTests
{
    [Fact]
    public async Task PrepareForBrowser_StopsMediaPlaybackFlag()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        shell.Cast.GetType().GetProperty(nameof(CastPageViewModel.IsMediaPlaying))!
            .SetValue(shell.Cast, true);

        var coordinator = new ModeSessionCoordinator(shell.Cast, shell.Browser);
        await coordinator.PrepareForAsync(TvSurfaceKind.Browser);

        shell.Cast.IsMediaPlaying.ShouldBeFalse();
        shell.Cast.MediaStatus.ShouldContain("stopped", Case.Insensitive);
    }

    [Fact]
    public async Task PrepareForMedia_ClosesOpenBrowserSurface()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        var cast = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice())).Cast;
        var coordinator = new ModeSessionCoordinator(cast, viewModel);

        viewModel.Address = "https://example.test/";
        await viewModel.NavigateCommand.ExecuteAsync(null);
        viewModel.HasOpenBrowserSurface.ShouldBeTrue();

        await coordinator.PrepareForAsync(TvSurfaceKind.Media);

        viewModel.HasOpenBrowserSurface.ShouldBeFalse();
        remote.Commands.ShouldContain(command => command.Action == Flint.Protocol.BrowserCommandAction.Close);
    }

    [Fact]
    public async Task StopMediaCommand_ClearsPlayingFlag()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        shell.Cast.GetType().GetProperty(nameof(CastPageViewModel.IsMediaPlaying))!
            .SetValue(shell.Cast, true);

        await shell.Cast.StopMediaCommand.ExecuteAsync(null);

        shell.Cast.IsMediaPlaying.ShouldBeFalse();
    }

    [Fact]
    public async Task PrepareForMirror_ClosesOpenBrowserSurface()
    {
        var remote = new RecordingBrowserRemote();
        var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        var cast = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice())).Cast;
        var coordinator = new ModeSessionCoordinator(cast, viewModel);

        viewModel.Address = "https://example.test/";
        await viewModel.NavigateCommand.ExecuteAsync(null);
        viewModel.HasOpenBrowserSurface.ShouldBeTrue();

        await coordinator.PrepareForAsync(TvSurfaceKind.Mirror);

        viewModel.HasOpenBrowserSurface.ShouldBeFalse();
        remote.Commands.ShouldContain(command => command.Action == Flint.Protocol.BrowserCommandAction.Close);
    }

    [Fact]
    public async Task PrepareForBrowser_ClearsStuckMirroringFlagWithoutLiveLoop()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        typeof(CastPageViewModel).GetField(
                "_isMirroring",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(shell.Cast, true);

        var coordinator = new ModeSessionCoordinator(shell.Cast, shell.Browser);
        await coordinator.PrepareForAsync(TvSurfaceKind.Browser);

        shell.Cast.IsMirroring.ShouldBeFalse();
    }
}
