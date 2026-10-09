using Flint.Core;
using Flint.Core.Settings;

namespace Flint.Cli;

/// <summary>What a share started from the command line sends, as its options asked.</summary>
/// <remarks>
/// Worked out the way the app works out its own, from the same picture modes and the same main
/// display, so a problem seen in the app can be reproduced here with the same numbers.
/// </remarks>
/// <param name="Display">The display's number as <c>--list-displays</c> shows it, or null for the main one.</param>
/// <param name="Mode">The picture mode the share starts from.</param>
/// <param name="FrameRate">Frames a second instead of the mode's, or null.</param>
/// <param name="MegabitsPerSecond">The data rate instead of the mode's, or null.</param>
/// <param name="MaxWidth">The widest picture instead of the mode's, or null.</param>
/// <param name="Sound">Whether this PC's sound plays on the TV as well.</param>
/// <param name="Pointer">Whether the mouse pointer is drawn into the picture.</param>
internal sealed record MirrorChoices(
    int? Display = null,
    PictureMode Mode = PictureMode.Balanced,
    int? FrameRate = null,
    int? MegabitsPerSecond = null,
    uint? MaxWidth = null,
    bool Sound = true,
    bool Pointer = true)
{
    /// <summary>The names <c>--mode</c> takes, in the order the Screen page offers the modes.</summary>
    internal static IReadOnlyList<(string Name, PictureMode Mode)> ModeNames { get; } =
    [
        ("balanced", PictureMode.Balanced),
        ("movie", PictureMode.Movie),
        ("game", PictureMode.Game),
        ("text", PictureMode.TextAndSlides),
        ("data-saver", PictureMode.DataSaver),
    ];

    /// <summary>Whether any of the mode's numbers was replaced, which the app calls a custom picture.</summary>
    internal bool IsCustom => FrameRate is not null || MegabitsPerSecond is not null || MaxWidth is not null;

    /// <summary>The mode a <c>--mode</c> name stands for, ignoring case, or null.</summary>
    internal static PictureMode? FindMode(string? name)
    {
        foreach (var (known, mode) in ModeNames)
        {
            if (known.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return mode;
            }
        }

        return null;
    }

    /// <summary>Finds the display to share among those connected.</summary>
    /// <param name="displays">The displays now connected, numbered as <c>--list-displays</c> shows them.</param>
    /// <param name="display">The display, or null to leave the choice to capture when none could be listed.</param>
    /// <param name="error">Why the display asked for cannot be shared, or null.</param>
    internal bool TryChooseDisplay(IReadOnlyList<DisplayInfo> displays, out DisplayInfo? display, out string? error)
    {
        ArgumentNullException.ThrowIfNull(displays);
        error = null;

        if (Display is not { } number)
        {
            // The app's own default: the main display, not whichever one capture happens to list first.
            display = DisplayCatalog.Choose(displays, ShareDisplayChoice.Main, null).Display;
            return true;
        }

        display = displays.FirstOrDefault(candidate => candidate.Number == number);
        if (display is not null)
        {
            return true;
        }

        error = displays.Count == 0
            ? "--display has nothing to choose from: no displays could be listed on this PC."
            : $"There is no display {number}. This PC has {displays.Count}, and flint --list-displays names them.";
        return false;
    }

    /// <summary>What the share sends: the mode's numbers, with any the options replaced.</summary>
    /// <param name="display">The display to share, or null for the first one capture finds.</param>
    /// <param name="tvWidth">The TV's own width, when it said.</param>
    internal MirrorSessionOptions Resolve(DisplayInfo? display, int? tvWidth)
    {
        var fromMode = ScreenQualityPreset.ToOptions(
            new ScreenSettings { PictureMode = Mode },
            display?.Index ?? 0,
            tvWidth,
            display?.Width);
        return fromMode with
        {
            FrameRate = FrameRate is { } frameRate ? (uint)frameRate : fromMode.FrameRate,
            BitrateBitsPerSecond = MegabitsPerSecond is { } megabits ? (uint)megabits * 1_000_000 : fromMode.BitrateBitsPerSecond,
            MaxWidth = MaxWidth ?? fromMode.MaxWidth,
        };
    }

    /// <summary>One sentence saying what the share sends, worded as the Screen page words it.</summary>
    internal string Describe(MirrorSessionOptions options) =>
        ScreenQualityPreset.Describe(IsCustom ? PictureMode.Custom : Mode, options);
}
