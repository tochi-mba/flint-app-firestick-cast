namespace Flint.App.ViewModels;

/// <summary>
/// What the switch prompt says: what the TV is showing now, what switching would do to it, and the
/// two answers.
/// </summary>
/// <param name="Title">The question, naming what the TV would switch to.</param>
/// <param name="Body">What the TV is showing now and what switching stops.</param>
/// <param name="ConfirmLabel">The button that switches.</param>
/// <param name="KeepLabel">The button that leaves the TV as it is.</param>
public sealed record SurfaceSwitchCopy(string Title, string Body, string ConfirmLabel, string KeepLabel)
{
    /// <summary>The question for switching <paramref name="tvName"/> from <paramref name="current"/> to <paramref name="next"/>.</summary>
    /// <remarks>
    /// Only asked between two different surfaces. Nothing on the TV, or the same surface asked for
    /// again, is not a switch, and a caller that asks anyway has a bug worth hearing about.
    /// </remarks>
    public static SurfaceSwitchCopy For(TvSurfaceKind current, TvSurfaceKind next, string? tvName)
    {
        if (current is TvSurfaceKind.None || next is TvSurfaceKind.None || current == next)
        {
            throw new ArgumentOutOfRangeException(
                nameof(next),
                $"{current} to {next}",
                "A switch goes from one surface on the TV to a different one.");
        }

        var tv = string.IsNullOrWhiteSpace(tvName) ? "The TV" : tvName.Trim();
        var (showing, stopping, keep) = current switch
        {
            TvSurfaceKind.Mirror => ("is showing your screen", "stops the mirror", "KEEP MIRRORING"),
            TvSurfaceKind.Media => ("is playing a file from this PC", "stops it", "KEEP PLAYING"),
            _ => ("is showing the browser", "closes it; your tabs are kept for when you come back", "KEEP THE BROWSER"),
        };
        var (title, doing, confirm) = next switch
        {
            TvSurfaceKind.Browser => ("Switch the TV to the browser?", "Opening the browser", "SWITCH TO BROWSER"),
            TvSurfaceKind.Mirror => ("Mirror your screen instead?", "Mirroring", "START MIRRORING"),
            _ => ("Play this on the TV instead?", "Playing this", "PLAY ON TV"),
        };

        return new SurfaceSwitchCopy(title, $"{tv} {showing}. {doing} {stopping}.", confirm, keep);
    }
}
