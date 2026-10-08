using Avalonia.Data.Converters;
using Flint.Core;
using Flint.Core.Settings;

namespace Flint.App.ViewModels;

/// <summary>Text the Screen page builds from a display.</summary>
public static class ScreenPageText
{
    /// <summary>One line naming a display, such as "1 · DELL U2720Q · 2560 × 1440 · main".</summary>
    public static FuncValueConverter<DisplayInfo?, string?> DescribeDisplay { get; } =
        new(display => display?.Describe());

    /// <summary>The custom data rate slider's lowest value, as the slider takes it.</summary>
    public static double MinimumMegabits => ScreenSettings.MinimumMegabitsPerSecond;

    /// <summary>The custom data rate slider's highest value.</summary>
    public static double MaximumMegabits => ScreenSettings.MaximumMegabitsPerSecond;
}
