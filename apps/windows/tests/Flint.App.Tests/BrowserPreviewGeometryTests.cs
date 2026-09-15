using Flint.App.Services;
using Shouldly;

namespace Flint.App.Tests;

public sealed class BrowserPreviewGeometryTests
{
    [Theory]
    [InlineData(480, 270, 960, 540)] // same aspect
    [InlineData(400, 400, 960, 540)] // letterbox top/bottom
    [InlineData(1000, 200, 960, 540)] // pillarbox left/right
    public void TryMap_Center_MapsToMidWire(double controlW, double controlH, int imageW, int imageH)
    {
        BrowserPreviewGeometry.TryMap(
            controlW / 2,
            controlH / 2,
            controlW,
            controlH,
            imageW,
            imageH,
            out var mapped).ShouldBeTrue();

        mapped.X.ShouldBeInRange((BrowserTouchCoordinates.Max / 2) - 2, (BrowserTouchCoordinates.Max / 2) + 2);
        mapped.Y.ShouldBeInRange((BrowserTouchCoordinates.Max / 2) - 2, (BrowserTouchCoordinates.Max / 2) + 2);
    }

    [Fact]
    public void TryMap_ImageCorners_MapToWireExtents()
    {
        const double controlW = 400;
        const double controlH = 400;
        const int imageW = 960;
        const int imageH = 540;
        var (left, top, width, height) = BrowserPreviewGeometry.DrawnRect(controlW, controlH, imageW, imageH);

        BrowserPreviewGeometry.TryMap(left, top, controlW, controlH, imageW, imageH, out var tl)
            .ShouldBeTrue();
        tl.ShouldBe((0, 0));

        BrowserPreviewGeometry.TryMap(left + width, top + height, controlW, controlH, imageW, imageH, out var br)
            .ShouldBeTrue();
        br.ShouldBe((BrowserTouchCoordinates.Max, BrowserTouchCoordinates.Max));
    }

    [Fact]
    public void TryMap_LetterboxGutters_AreRejected()
    {
        // Tall control → bars above/below a 16:9 image.
        const double controlW = 400;
        const double controlH = 400;
        const int imageW = 960;
        const int imageH = 540;
        var (_, top, _, height) = BrowserPreviewGeometry.DrawnRect(controlW, controlH, imageW, imageH);

        BrowserPreviewGeometry.TryMap(controlW / 2, top / 2, controlW, controlH, imageW, imageH, out _)
            .ShouldBeFalse();
        BrowserPreviewGeometry.TryMap(controlW / 2, top + height + (controlH - top - height) / 2, controlW, controlH, imageW, imageH, out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void TryMap_PillarboxGutters_AreRejected()
    {
        // Wide control → bars left/right of a 16:9 image.
        const double controlW = 1000;
        const double controlH = 200;
        const int imageW = 960;
        const int imageH = 540;
        var (left, _, width, _) = BrowserPreviewGeometry.DrawnRect(controlW, controlH, imageW, imageH);

        left.ShouldBeGreaterThan(1);
        BrowserPreviewGeometry.TryMap(left / 2, controlH / 2, controlW, controlH, imageW, imageH, out _)
            .ShouldBeFalse();
        BrowserPreviewGeometry.TryMap(left + width + 1, controlH / 2, controlW, controlH, imageW, imageH, out _)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData(0, 100, 960, 540)]
    [InlineData(100, 0, 960, 540)]
    [InlineData(100, 100, 0, 540)]
    [InlineData(100, 100, 960, 0)]
    [InlineData(-1, 100, 960, 540)]
    public void TryMap_DegenerateSizes_ReturnFalse(double controlW, double controlH, int imageW, int imageH)
    {
        BrowserPreviewGeometry.TryMap(10, 10, controlW, controlH, imageW, imageH, out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void TryMap_NonFiniteCoordinates_ReturnFalse()
    {
        BrowserPreviewGeometry.TryMap(double.NaN, 10, 100, 100, 960, 540, out _).ShouldBeFalse();
        BrowserPreviewGeometry.TryMap(10, double.PositiveInfinity, 100, 100, 960, 540, out _).ShouldBeFalse();
    }

    [Fact]
    public void DrawnRect_CentersUniformScale()
    {
        var (x, y, width, height) = BrowserPreviewGeometry.DrawnRect(400, 400, 960, 540);
        width.ShouldBeGreaterThan(0);
        height.ShouldBeGreaterThan(0);
        (width / height).ShouldBe(960.0 / 540.0, tolerance: 0.01);
        x.ShouldBeGreaterThanOrEqualTo(0);
        y.ShouldBeGreaterThanOrEqualTo(0);
        (x + width).ShouldBeLessThanOrEqualTo(400.01);
        (y + height).ShouldBeLessThanOrEqualTo(400.01);
    }

    [Fact]
    public void DrawnRect_InvalidSizes_ReturnDefault()
    {
        BrowserPreviewGeometry.DrawnRect(0, 100, 960, 540).ShouldBe(default);
        BrowserPreviewGeometry.DrawnRect(100, 100, 0, 540).ShouldBe(default);
    }
}

public sealed class BrowserPreviewInteractionTests
{
    [Fact]
    public void TryMapPointer_WhenCannotInteract_ReturnsFalse()
    {
        BrowserPreviewInteraction.TryMapPointer(
            50, 50, 100, 100, 960, 540,
            canInteract: false,
            hasLivePreview: true,
            previewNavigationId: 3,
            previewFrameId: 9,
            fallbackNavigationId: 3,
            out _).ShouldBeFalse();
    }

    [Fact]
    public void TryMapPointer_Gutter_ReturnsFalse()
    {
        BrowserPreviewInteraction.TryMapPointer(
            50, 5, 400, 400, 960, 540,
            canInteract: true,
            hasLivePreview: true,
            previewNavigationId: 3,
            previewFrameId: 9,
            fallbackNavigationId: 3,
            out _).ShouldBeFalse();
    }

    [Fact]
    public void TryMapPointer_LivePreview_UsesPreviewRefs()
    {
        BrowserPreviewInteraction.TryMapPointer(
            200, 200, 400, 400, 960, 540,
            canInteract: true,
            hasLivePreview: true,
            previewNavigationId: 11,
            previewFrameId: 42,
            fallbackNavigationId: 7,
            out var mapped).ShouldBeTrue();

        mapped.NavigationId.ShouldBe(11);
        mapped.FrameId.ShouldBe(42);
    }

    [Fact]
    public void TryMapPointer_WithoutLivePreview_FallsBackToNavigationId()
    {
        BrowserPreviewInteraction.TryMapPointer(
            200, 200, 400, 400, 960, 540,
            canInteract: true,
            hasLivePreview: false,
            previewNavigationId: 0,
            previewFrameId: 0,
            fallbackNavigationId: 7,
            out var mapped).ShouldBeTrue();

        mapped.NavigationId.ShouldBe(7);
        mapped.FrameId.ShouldBe(7);
    }

    [Fact]
    public void ResolveRefs_LivePreferPreviewIds()
    {
        BrowserPreviewInteraction.ResolveRefs(true, 5, 9, 3).ShouldBe((5L, 9L));
    }

    [Fact]
    public void ResolveRefs_FallbackWhenNotLive()
    {
        BrowserPreviewInteraction.ResolveRefs(false, 5, 9, 3).ShouldBe((3L, 3L));
        BrowserPreviewInteraction.ResolveRefs(true, 0, 9, 3).ShouldBe((3L, 3L));
        BrowserPreviewInteraction.ResolveRefs(false, 0, 0, 0).ShouldBe((0L, 0L));
    }
}
