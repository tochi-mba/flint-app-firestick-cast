namespace Flint.App.ViewModels;

/// <summary>
/// One entry in the left rail.
/// </summary>
/// <remarks>
/// REX uses short letterforms rather than an icon font, so the glyph is a string. It keeps the
/// design system dependency-free and matches the receiver, which has no icon font available.
/// </remarks>
/// <param name="Glyph">The one- or two-character mark.</param>
/// <param name="Label">The destination name.</param>
public sealed record NavigationDestination(string Glyph, string Label)
{
    /// <summary>Whether this destination has a connected page.</summary>
    public bool IsImplemented => Label is "Cast" or "Media" or "Screen" or "Web" or "Diagnostics" or "Settings";
}
