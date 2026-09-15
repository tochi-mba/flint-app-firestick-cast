namespace Flint.App.Services;

/// <summary>Maps pointer input against the image that is actually drawn inside a preview control.</summary>
public static class BrowserPreviewGeometry
{
    /// <summary>
    /// Maps a control-space point into browser fixed-point coordinates, rejecting letterbox bars.
    /// </summary>
    public static bool TryMap(
        double x,
        double y,
        double controlWidth,
        double controlHeight,
        int imageWidth,
        int imageHeight,
        out (int X, int Y) mapped)
    {
        mapped = default;
        if (!double.IsFinite(x)
            || !double.IsFinite(y)
            || !double.IsFinite(controlWidth)
            || !double.IsFinite(controlHeight)
            || controlWidth <= 0
            || controlHeight <= 0
            || imageWidth <= 0
            || imageHeight <= 0)
        {
            return false;
        }

        var scale = Math.Min(controlWidth / imageWidth, controlHeight / imageHeight);
        var drawnWidth = imageWidth * scale;
        var drawnHeight = imageHeight * scale;
        var left = (controlWidth - drawnWidth) / 2;
        var top = (controlHeight - drawnHeight) / 2;

        if (x < left || y < top || x > left + drawnWidth || y > top + drawnHeight)
        {
            return false;
        }

        var localX = Math.Clamp((x - left) / drawnWidth, 0, 1);
        var localY = Math.Clamp((y - top) / drawnHeight, 0, 1);
        mapped = (
            (int)Math.Round(localX * BrowserTouchCoordinates.Max),
            (int)Math.Round(localY * BrowserTouchCoordinates.Max));
        return true;
    }

    /// <summary>The uniformly scaled image rectangle, useful for drawing an exact input affordance.</summary>
    public static (double X, double Y, double Width, double Height) DrawnRect(
        double controlWidth,
        double controlHeight,
        int imageWidth,
        int imageHeight)
    {
        if (controlWidth <= 0 || controlHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
        {
            return default;
        }

        var scale = Math.Min(controlWidth / imageWidth, controlHeight / imageHeight);
        var width = imageWidth * scale;
        var height = imageHeight * scale;
        return ((controlWidth - width) / 2, (controlHeight - height) / 2, width, height);
    }
}
