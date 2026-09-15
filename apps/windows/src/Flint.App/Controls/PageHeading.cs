using Avalonia;
using Avalonia.Controls.Primitives;

namespace Flint.App.Controls;

/// <summary>
/// The heading block at the top of every page: a tracked eyebrow, a large title, and a status pill.
/// </summary>
/// <remarks>
/// Ported from <c>PageHeading</c> in REX Cast. The eyebrow names the domain and the pill states the
/// page's condition, so the user knows what they are looking at and whether it is working before
/// reading anything else.
/// </remarks>
public sealed class PageHeading : TemplatedControl
{
    /// <summary>Identifies the <see cref="Eyebrow"/> property.</summary>
    public static readonly StyledProperty<string> EyebrowProperty =
        AvaloniaProperty.Register<PageHeading, string>(nameof(Eyebrow), string.Empty);

    /// <summary>Identifies the <see cref="Title"/> property.</summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<PageHeading, string>(nameof(Title), string.Empty);

    /// <summary>Identifies the <see cref="Trailing"/> property.</summary>
    public static readonly StyledProperty<string> TrailingProperty =
        AvaloniaProperty.Register<PageHeading, string>(nameof(Trailing), string.Empty);

    /// <summary>Identifies the <see cref="TrailingTone"/> property.</summary>
    public static readonly StyledProperty<Tone> TrailingToneProperty =
        AvaloniaProperty.Register<PageHeading, Tone>(nameof(TrailingTone), Tone.Signal);

    /// <summary>The small tracked label above the title.</summary>
    public string Eyebrow
    {
        get => GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    /// <summary>The page name.</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The status word shown in the trailing pill.</summary>
    public string Trailing
    {
        get => GetValue(TrailingProperty);
        set => SetValue(TrailingProperty, value);
    }

    /// <summary>The trailing pill's colour.</summary>
    public Tone TrailingTone
    {
        get => GetValue(TrailingToneProperty);
        set => SetValue(TrailingToneProperty, value);
    }
}
