using Avalonia;
using Avalonia.Controls.Primitives;

namespace Flint.App.Controls;

/// <summary>
/// The nine-pixel status dot that precedes a state line.
/// </summary>
/// <remarks>
/// Ported from the inline dot in REX Cast's <c>NetworkCard</c>. Nine pixels is deliberate: large
/// enough to read colour at a glance, small enough that it never competes with the text beside it.
/// </remarks>
public sealed class StatusDot : TemplatedControl
{
    /// <summary>Identifies the <see cref="Tone"/> property.</summary>
    public static readonly StyledProperty<Tone> ToneProperty =
        AvaloniaProperty.Register<StatusDot, Tone>(nameof(Tone), Tone.Neutral);

    /// <summary>The state this dot reports.</summary>
    public Tone Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }
}
