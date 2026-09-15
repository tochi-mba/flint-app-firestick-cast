namespace Flint.App.Services;

/// <summary>
/// Pure policy for mapping host pointer/scroll onto a letterboxed TV preview image.
/// </summary>
/// <remarks>
/// Kept out of the Avalonia code-behind so letterbox rejection, gating, and frame-reference choice
/// can be asserted without a window.
/// </remarks>
public static class BrowserPreviewInteraction
{
    /// <summary>Result of asking whether a control-space point should become a TV pointer sample.</summary>
    public readonly record struct MappedPoint(int X, int Y, long NavigationId, long FrameId);

    /// <summary>
    /// Maps a point on the preview surface when interaction is allowed and the point hits the image.
    /// </summary>
    public static bool TryMapPointer(
        double x,
        double y,
        double controlWidth,
        double controlHeight,
        int imageWidth,
        int imageHeight,
        bool canInteract,
        bool hasLivePreview,
        long previewNavigationId,
        long previewFrameId,
        long fallbackNavigationId,
        out MappedPoint mapped)
    {
        mapped = default;
        if (!canInteract)
        {
            return false;
        }

        if (!BrowserPreviewGeometry.TryMap(
                x,
                y,
                controlWidth,
                controlHeight,
                imageWidth,
                imageHeight,
                out var wire))
        {
            return false;
        }

        var (navigationId, frameId) = ResolveRefs(
            hasLivePreview,
            previewNavigationId,
            previewFrameId,
            fallbackNavigationId);
        if (navigationId <= 0 || frameId <= 0)
        {
            return false;
        }

        mapped = new MappedPoint(wire.X, wire.Y, navigationId, frameId);
        return true;
    }

    /// <summary>
    /// Chooses wire navigation/frame identifiers: live preview ids when available, else the page nav id.
    /// </summary>
    public static (long NavigationId, long FrameId) ResolveRefs(
        bool hasLivePreview,
        long previewNavigationId,
        long previewFrameId,
        long fallbackNavigationId)
    {
        if (hasLivePreview && previewNavigationId > 0 && previewFrameId > 0)
        {
            return (previewNavigationId, previewFrameId);
        }

        if (fallbackNavigationId > 0)
        {
            return (fallbackNavigationId, fallbackNavigationId);
        }

        return (0, 0);
    }
}
