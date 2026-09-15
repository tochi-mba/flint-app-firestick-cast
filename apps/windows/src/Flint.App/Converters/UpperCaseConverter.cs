using System.Globalization;
using Avalonia.Data.Converters;

namespace Flint.App.Converters;

/// <summary>
/// Upper-cases a string for the REX tracked-label style.
/// </summary>
/// <remarks>
/// Applied in templates rather than at call sites so every eyebrow, pill and section label is
/// upper-cased by the design system itself. Uses the invariant culture deliberately: these are
/// interface chrome words, not user content, and a Turkish-locale dotless capital I here would be
/// a rendering bug rather than a localisation feature.
/// </remarks>
public sealed class UpperCaseConverter : IValueConverter
{
    /// <summary>The shared instance used by control templates.</summary>
    public static readonly UpperCaseConverter Instance = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text ? text.ToUpperInvariant() : value;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Upper-casing is one-way presentation.");
}
