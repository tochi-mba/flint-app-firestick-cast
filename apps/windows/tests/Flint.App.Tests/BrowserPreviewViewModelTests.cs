using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Flint.App.ViewModels;
using Flint.Protocol;
using Shouldly;

namespace Flint.App.Tests;

public sealed class BrowserPreviewViewModelTests
{
    [Fact]
    public void Configure_WhenUnsupported_DisablesToggleAndClearsFrame()
    {
        using var preview = new BrowserPreviewViewModel();
        preview.Configure(supported: true);
        preview.SetRequested(true);

        preview.Configure(supported: false);

        preview.IsSupported.ShouldBeFalse();
        preview.CanToggle.ShouldBeFalse();
        preview.IsRequested.ShouldBeFalse();
        preview.HasFrame.ShouldBeFalse();
        preview.StatusLabel.ShouldBe("PREVIEW UNAVAILABLE");
    }

    [Fact]
    public void Configure_WhenSupported_RequestsPreviewByDefault()
    {
        using var preview = new BrowserPreviewViewModel();

        preview.Configure(supported: true);

        preview.IsSupported.ShouldBeTrue();
        preview.CanToggle.ShouldBeTrue();
        preview.IsRequested.ShouldBeTrue();
        preview.ToggleLabel.ShouldBe("PREVIEW OFF");
        preview.StatusLabel.ShouldBe("STARTING PREVIEW");
    }

    [Fact]
    public void Configure_WhenSupported_CanStayOffWhenDefaultDisabled()
    {
        using var preview = new BrowserPreviewViewModel();

        preview.Configure(supported: true, enableByDefault: false);

        preview.IsRequested.ShouldBeFalse();
        preview.StatusLabel.ShouldBe("OFF · TV IS CANONICAL");
    }

    [AvaloniaFact]
    public void Accept_WhenRequested_PublishesLiveFrame()
    {
        using var preview = new BrowserPreviewViewModel();
        preview.Configure(supported: true);
        preview.SetRequested(true);
        var bitmap = CreateBitmap(32, 18);

        preview.Accept(
            new BrowserPreviewMessage(
                Epoch: 1,
                NavigationId: 2,
                FrameId: 3,
                Width: 32,
                Height: 18,
                Jpeg: BinaryData.From([1, 2, 3])),
            bitmap);

        preview.HasFrame.ShouldBeTrue();
        preview.IsLive.ShouldBeTrue();
        preview.FrameWidth.ShouldBe(32);
        preview.FrameHeight.ShouldBe(18);
        preview.FrameId.ShouldBe(3);
        preview.StatusLabel.ShouldBe("LIVE · TV IS CANONICAL");
    }

    [AvaloniaFact]
    public void Accept_WhenNotRequested_DisposesBitmapWithoutPublishing()
    {
        using var preview = new BrowserPreviewViewModel();
        preview.Configure(supported: true, enableByDefault: false);
        var bitmap = CreateBitmap(16, 9);

        preview.Accept(
            new BrowserPreviewMessage(
                Epoch: 1,
                NavigationId: 1,
                FrameId: 1,
                Width: 16,
                Height: 9,
                Jpeg: BinaryData.From([9])),
            bitmap);

        preview.HasFrame.ShouldBeFalse();
        preview.IsLive.ShouldBeFalse();
    }

    private static WriteableBitmap CreateBitmap(int width, int height) =>
        new(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
}
