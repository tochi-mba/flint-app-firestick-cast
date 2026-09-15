using Avalonia;
using Avalonia.Controls.Primitives;

namespace Flint.App.Controls;

/// <summary>
/// One label-and-value line inside a diagnostics card, with a hairline beneath it.
/// </summary>
/// <remarks>
/// Ported from <c>DiagnosticRow</c> in REX Cast, including its 42/58 weighting: the value column is
/// wider because addresses, URLs and error strings are what actually overflow.
/// </remarks>
public sealed class DiagnosticRow : TemplatedControl
{
    /// <summary>Identifies the <see cref="Label"/> property.</summary>
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<DiagnosticRow, string>(nameof(Label), string.Empty);

    /// <summary>Identifies the <see cref="Value"/> property.</summary>
    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<DiagnosticRow, string>(nameof(Value), string.Empty);

    /// <summary>Identifies the <see cref="IsLast"/> property.</summary>
    public static readonly StyledProperty<bool> IsLastProperty =
        AvaloniaProperty.Register<DiagnosticRow, bool>(nameof(IsLast));

    /// <summary>Identifies the <see cref="ValueTone"/> property.</summary>
    public static readonly StyledProperty<Tone> ValueToneProperty =
        AvaloniaProperty.Register<DiagnosticRow, Tone>(nameof(ValueTone), Tone.Neutral);

    /// <summary>The field name.</summary>
    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>The measured or reported value.</summary>
    public string Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Whether to omit the hairline, because this row ends the card.</summary>
    public bool IsLast
    {
        get => GetValue(IsLastProperty);
        set => SetValue(IsLastProperty, value);
    }

    /// <summary>
    /// Colour for the value. <see cref="Tone.Neutral"/> renders as primary text; the accents are
    /// reserved for values that carry a state, such as a failure string.
    /// </summary>
    public Tone ValueTone
    {
        get => GetValue(ValueToneProperty);
        set => SetValue(ValueToneProperty, value);
    }
}
