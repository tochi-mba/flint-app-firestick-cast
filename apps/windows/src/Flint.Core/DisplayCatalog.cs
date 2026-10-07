using Flint.Core.Settings;

namespace Flint.Core;

/// <summary>Which display a share should capture, and whether the usual one was missing.</summary>
/// <param name="Display">The display to share, or null when none could be listed.</param>
/// <param name="RememberedIsMissing">
/// Whether the person's usual display is not connected, so the main display stands in for it.
/// </param>
public sealed record DisplaySelection(DisplayInfo? Display, bool RememberedIsMissing);

/// <summary>Names and numbers the displays capture can open.</summary>
/// <param name="outputs">The displays, as capture counts them.</param>
/// <param name="names">The monitors' own names.</param>
public sealed class DisplayCatalog(IDisplayOutputSource outputs, IMonitorNames names) : IDisplayCatalog
{
    private readonly IDisplayOutputSource outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
    private readonly IMonitorNames names = names ?? throw new ArgumentNullException(nameof(names));

    /// <inheritdoc />
    public IReadOnlyList<DisplayInfo> List()
    {
        var found = outputs.ListOutputs();
        if (found.Count == 0)
        {
            return [];
        }

        var monitors = names.Read();
        var displays = new List<DisplayInfo>(found.Count);
        foreach (var output in found)
        {
            var number = displays.Count + 1;
            monitors.TryGetValue(output.DeviceName, out var monitor);
            var name = string.IsNullOrWhiteSpace(monitor?.FriendlyName) ? $"Display {number}" : monitor.FriendlyName.Trim();
            var identity = string.IsNullOrWhiteSpace(monitor?.DevicePath) ? output.DeviceName : monitor.DevicePath;
            displays.Add(new DisplayInfo(
                output.Index,
                number,
                name,
                identity,
                output.Left,
                output.Top,
                output.Width,
                output.Height,
                output.Rotation,
                output.IsMain));
        }

        return displays;
    }

    /// <summary>Picks the display a share captures, as the settings ask.</summary>
    /// <param name="displays">The displays now connected.</param>
    /// <param name="choice">Whether the main display or the remembered one is wanted.</param>
    /// <param name="remembered">The remembered display's identity.</param>
    public static DisplaySelection Choose(
        IReadOnlyList<DisplayInfo> displays,
        ShareDisplayChoice choice,
        string? remembered)
    {
        ArgumentNullException.ThrowIfNull(displays);

        // Nothing listed means the list could not be read, not that the usual display is gone.
        if (displays.Count == 0)
        {
            return new DisplaySelection(null, RememberedIsMissing: false);
        }

        var main = displays.FirstOrDefault(display => display.IsMain) ?? displays[0];
        if (choice is not ShareDisplayChoice.Remembered || string.IsNullOrWhiteSpace(remembered))
        {
            return new DisplaySelection(main, RememberedIsMissing: false);
        }

        var usual = displays.FirstOrDefault(display =>
            string.Equals(display.Identity, remembered, StringComparison.OrdinalIgnoreCase));
        return usual is not null
            ? new DisplaySelection(usual, RememberedIsMissing: false)
            : new DisplaySelection(main, RememberedIsMissing: true);
    }
}
