using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Flint.App.Controls;

namespace Flint.App.Converters;

/// <summary>
/// Maps a <see cref="Tone"/> onto its palette brush.
/// </summary>
/// <remarks>
/// The single place where a semantic status becomes a colour. Keeping this mapping in one converter
/// is what stops an off-palette colour reaching a control, and makes a palette change a one-file
/// edit rather than a hunt.
/// </remarks>
public sealed class ToneToBrushConverter : IValueConverter
{
    /// <summary>The shared instance used by control templates.</summary>
    public static readonly ToneToBrushConverter Instance = new();

    private static readonly SolidColorBrush Neutral = new(Color.Parse("#858D83"));
    private static readonly SolidColorBrush Signal = new(Color.Parse("#D7FF3F"));
    private static readonly SolidColorBrush Live = new(Color.Parse("#FF774D"));
    private static readonly SolidColorBrush Line = new(Color.Parse("#29302A"));

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            Tone.Signal => Signal,
            Tone.Live => Live,
            Tone.Line => Line,
            _ => Neutral,
        };

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Tone mapping is one-way presentation.");
}
