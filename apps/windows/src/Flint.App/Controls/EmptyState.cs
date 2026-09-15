using Avalonia;
using Avalonia.Controls.Primitives;

namespace Flint.App.Controls;

/// <summary>
/// The centred placeholder shown when a page has nothing to display yet.
/// </summary>
/// <remarks>
/// Ported from <c>EmptyState</c> in REX Cast, including its text glyph. REX uses short letterforms
/// rather than an icon font, which keeps the system dependency-free and makes an empty state read
/// as part of the same instrument as everything around it.
/// </remarks>
public sealed class EmptyState : TemplatedControl
{
    /// <summary>Identifies the <see cref="Glyph"/> property.</summary>
    public static readonly StyledProperty<string> GlyphProperty =
        AvaloniaProperty.Register<EmptyState, string>(nameof(Glyph), string.Empty);

    /// <summary>Identifies the <see cref="Title"/> property.</summary>
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<EmptyState, string>(nameof(Title), string.Empty);

    /// <summary>Identifies the <see cref="Body"/> property.</summary>
    public static readonly StyledProperty<string> BodyProperty =
        AvaloniaProperty.Register<EmptyState, string>(nameof(Body), string.Empty);

    /// <summary>A one- or two-character mark, such as "TV" or "+".</summary>
    public string Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>What is missing, in a few words.</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Why it is missing and what happens next.</summary>
    public string Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }
}
