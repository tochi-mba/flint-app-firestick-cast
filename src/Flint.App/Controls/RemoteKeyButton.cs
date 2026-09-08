using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Flint.Protocol;

namespace Flint.App.Controls;

/// <summary>
/// One key on the on-screen remote, carrying the semantic key it sends.
/// </summary>
/// <remarks>
/// <para>
/// The remote is a dozen buttons that differ only in a glyph and which key they send. Written as
/// plain buttons, each one repeats a size, a margin, a command binding and an accessible name, and
/// the repetition is where they drift apart: one ends up a different height, one loses its
/// automation name, one keeps a stale binding after a rename.
/// </para>
/// <para>
/// It also fixes a real defect. A <c>CommandParameter="Up"</c> in markup is a string, and the
/// command takes a <see cref="BrowserSemanticKey"/> — so the binding throws the moment the button is
/// attached, which is a crash on a page rather than a compile error. Carrying the key as a typed
/// property means a wrong name cannot be written in the first place.
/// </para>
/// </remarks>
public sealed class RemoteKeyButton : TemplatedControl
{
    /// <summary>Identifies the <see cref="Key"/> property.</summary>
    public static readonly StyledProperty<BrowserSemanticKey> KeyProperty =
        AvaloniaProperty.Register<RemoteKeyButton, BrowserSemanticKey>(nameof(Key));

    /// <summary>Identifies the <see cref="Glyph"/> property.</summary>
    public static readonly StyledProperty<string> GlyphProperty =
        AvaloniaProperty.Register<RemoteKeyButton, string>(nameof(Glyph), string.Empty);

    /// <summary>Identifies the <see cref="Emphasis"/> property.</summary>
    public static readonly StyledProperty<bool> EmphasisProperty =
        AvaloniaProperty.Register<RemoteKeyButton, bool>(nameof(Emphasis));

    /// <summary>Raised when the key is pressed.</summary>
    public static readonly RoutedEvent<RemoteKeyEventArgs> PressedEvent =
        RoutedEvent.Register<RemoteKeyButton, RemoteKeyEventArgs>(
            nameof(Pressed),
            RoutingStrategies.Bubble);

    /// <summary>The key this button sends to the television.</summary>
    public BrowserSemanticKey Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    /// <summary>What the button shows: an arrow, or a short word.</summary>
    public string Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>Whether this is the primary key of its cluster, drawn in the Signal accent.</summary>
    public bool Emphasis
    {
        get => GetValue(EmphasisProperty);
        set => SetValue(EmphasisProperty, value);
    }

    /// <summary>Raised when the key is pressed.</summary>
    public event EventHandler<RemoteKeyEventArgs>? Pressed
    {
        add => AddHandler(PressedEvent, value);
        remove => RemoveHandler(PressedEvent, value);
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (e.NameScope.Find<Button>("PART_Button") is { } button)
        {
            button.Click += (_, _) => RaiseEvent(new RemoteKeyEventArgs(PressedEvent, Key));
        }
    }
}

/// <summary>Carries which remote key was pressed.</summary>
/// <param name="routedEvent">The event being raised.</param>
/// <param name="key">The key the button sends.</param>
public sealed class RemoteKeyEventArgs(RoutedEvent routedEvent, BrowserSemanticKey key)
    : RoutedEventArgs(routedEvent)
{
    /// <summary>The key the pressed button sends.</summary>
    public BrowserSemanticKey Key { get; } = key;
}
