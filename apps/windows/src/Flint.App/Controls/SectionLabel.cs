using Avalonia;
using Avalonia.Controls.Primitives;

namespace Flint.App.Controls;

/// <summary>
/// A tracked upper-case label that separates groups of cards.
/// </summary>
/// <remarks>Ported from <c>SectionLabel</c> in REX Cast.</remarks>
public sealed class SectionLabel : TemplatedControl
{
    /// <summary>Identifies the <see cref="Text"/> property.</summary>
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<SectionLabel, string>(nameof(Text), string.Empty);

    /// <summary>The section name.</summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
