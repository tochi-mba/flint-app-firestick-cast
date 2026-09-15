using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Flint.App.Services;
using Flint.Protocol;
using Shouldly;

namespace Flint.App.Tests;

public sealed class BrowserTouchPadTests
{
    [Fact]
    public void FromPadPoint_MapsCornersToWireExtents()
    {
        BrowserTouchCoordinates.FromPadPoint(0, 0, 200, 100).ShouldBe((0, 0));
        BrowserTouchCoordinates.FromPadPoint(200, 100, 200, 100)
            .ShouldBe((BrowserTouchCoordinates.Max, BrowserTouchCoordinates.Max));
        var mid = BrowserTouchCoordinates.FromPadPoint(100, 50, 200, 100);
        mid.X.ShouldBe((int)Math.Round(0.5 * BrowserTouchCoordinates.Max));
        mid.Y.ShouldBe((int)Math.Round(0.5 * BrowserTouchCoordinates.Max));
    }

    [Fact]
    public async Task SendPointer_AfterOpen_WithoutLivePreview_UsesNavigationIdForBothRefs()
    {
        var remote = new RecordingBrowserRemote();
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        await viewModel.NavigateCommand.ExecuteAsync(null);
        var navigationId = viewModel.ActiveNavigationIdForInteraction;

        await viewModel.SendPointerAsync(BrowserPointerAction.Down, 100, 200, buttons: 1);
        await viewModel.SendPointerAsync(BrowserPointerAction.Up, 100, 200, buttons: 0);

        var down = remote.Inputs[0].Event.ShouldBeOfType<BrowserPointerInput>();
        down.Action.ShouldBe(BrowserPointerAction.Down);
        down.NavigationId.ShouldBe(navigationId);
        down.FrameId.ShouldBe(navigationId);
        down.Buttons.ShouldBe(1);

        var up = remote.Inputs[1].Event.ShouldBeOfType<BrowserPointerInput>();
        up.Action.ShouldBe(BrowserPointerAction.Up);
        up.Buttons.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task SendPointer_WhenLivePreview_UsesPreviewNavigationAndFrameIds()
    {
        var remote = new RecordingBrowserRemote();
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        await viewModel.NavigateCommand.ExecuteAsync(null);
        viewModel.ActiveNavigationIdForInteraction.ShouldBeGreaterThan(0);

        viewModel.Preview.Accept(
            new BrowserPreviewMessage(
                Epoch: 1,
                NavigationId: viewModel.ActiveNavigationIdForInteraction + 10,
                FrameId: 9_001,
                Width: 32,
                Height: 18,
                Jpeg: BinaryData.From([1, 2, 3])),
            CreateBitmap(32, 18));

        viewModel.Preview.IsLive.ShouldBeTrue();
        viewModel.CanInteractWithPreview.ShouldBeTrue();

        await viewModel.SendPointerAsync(BrowserPointerAction.Down, 100, 200, buttons: 1);

        var down = remote.Inputs.Single().Event.ShouldBeOfType<BrowserPointerInput>();
        down.NavigationId.ShouldBe(viewModel.ActiveNavigationIdForInteraction + 10);
        down.FrameId.ShouldBe(9_001);
    }

    [AvaloniaFact]
    public async Task SendScroll_WhenLivePreview_UsesPreviewFrameId()
    {
        var remote = new RecordingBrowserRemote();
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        await viewModel.NavigateCommand.ExecuteAsync(null);
        var navigationId = viewModel.ActiveNavigationIdForInteraction;

        viewModel.Preview.Accept(
            new BrowserPreviewMessage(
                Epoch: 1,
                NavigationId: navigationId,
                FrameId: 4_200,
                Width: 32,
                Height: 18,
                Jpeg: BinaryData.From([1])),
            CreateBitmap(32, 18));

        await viewModel.SendScrollAsync(10, 20, 0, -120);

        var scroll = remote.Inputs.Single().Event.ShouldBeOfType<BrowserScrollInput>();
        scroll.NavigationId.ShouldBe(navigationId);
        scroll.FrameId.ShouldBe(4_200);
        scroll.DeltaY.ShouldBe(-120);
    }

    [Fact]
    public async Task SendScroll_AfterOpen_SendsNonZeroDelta()
    {
        var remote = new RecordingBrowserRemote();
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        await viewModel.NavigateCommand.ExecuteAsync(null);

        await viewModel.SendScrollAsync(10, 20, 0, -120);

        var scroll = remote.Inputs.Single().Event.ShouldBeOfType<BrowserScrollInput>();
        scroll.DeltaY.ShouldBe(-120);
        scroll.X.ShouldBe(10);
        scroll.Y.ShouldBe(20);
    }

    private static WriteableBitmap CreateBitmap(int width, int height) =>
        new(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
}
