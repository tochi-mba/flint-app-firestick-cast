using Avalonia;
using Avalonia.Controls.Primitives;

namespace Flint.App.Controls;

/// <summary>
/// A small outlined capsule carrying one upper-case word of status.
/// </summary>
/// <remarks>
/// Ported from <c>Pill</c> in REX Cast. The template upper-cases the text through
/// <see cref="Converters.UpperCaseConverter"/>, so a lower-case string can never reach the screen
/// and break the type system's rhythm.
/// </remarks>
public sealed class Pill : TemplatedControl
{
    /// <summary>Identifies the <see cref="Text"/> property.</summary>
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<Pill, string>(nameof(Text), string.Empty);

    /// <summary>Identifies the <see cref="Tone"/> property.</summary>
    public static readonly StyledProperty<Tone> ToneProperty =
        AvaloniaProperty.Register<Pill, Tone>(nameof(Tone), Tone.Neutral);

    /// <summary>The label. Rendered upper-case regardless of how it is supplied.</summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The status colour for the outline and the text.</summary>
    public Tone Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }
}
