namespace Flint.App.Services;

/// <summary>
/// Maps a touch-surface point into the 0…65,535 fixed-point coordinates the browser wire uses.
/// </summary>
public static class BrowserTouchCoordinates
{
    /// <summary>Right/bottom edge on the wire.</summary>
    public const int Max = 65_535;

    /// <summary>
    /// Converts a point inside a pad of the given size into wire coordinates.
    /// </summary>
    public static (int X, int Y) FromPadPoint(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return (0, 0);
        }

        var nx = Math.Clamp(x / width, 0, 1);
        var ny = Math.Clamp(y / height, 0, 1);
        return (
            (int)Math.Round(nx * Max),
            (int)Math.Round(ny * Max));
    }
}
