using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Flint.App.Controls;

/// <summary>
/// The REX card: a panel surface inside a one-pixel border whose colour carries the status.
/// </summary>
/// <remarks>
/// Ported from <c>InfoCard</c> in REX Cast. The border tone is the entire status language of the
/// system — a card outlined in Signal is ready, in Live is blocked, in Line is merely structural —
/// so cards never need a coloured fill or an icon to say the same thing twice.
/// </remarks>
public sealed class InfoCard : ContentControl
{
    /// <summary>Identifies the <see cref="BorderTone"/> property.</summary>
    public static readonly StyledProperty<Tone> BorderToneProperty =
        AvaloniaProperty.Register<InfoCard, Tone>(nameof(BorderTone), Tone.Line);

    /// <summary>The status this card's outline communicates.</summary>
    public Tone BorderTone
    {
        get => GetValue(BorderToneProperty);
        set => SetValue(BorderToneProperty, value);
    }
}
