using Avalonia.Input;
using Flint.Core.Shortcuts;

namespace Flint.App.Services;

/// <summary>Turns a key pressed in Flint's window into the combination a shortcut is made of.</summary>
public static class ShortcutKeys
{
    /// <summary>
    /// The combination pressed, or null for a key a shortcut cannot use, such as a modifier on its
    /// own while the person is still choosing the rest.
    /// </summary>
    public static HotKeyGesture? Of(Key key, KeyModifiers modifiers)
    {
        if (VirtualKeyOf(key) is not { } virtualKey)
        {
            return null;
        }

        var held = HotKeyModifiers.None;
        held |= (modifiers & KeyModifiers.Control) != 0 ? HotKeyModifiers.Control : HotKeyModifiers.None;
        held |= (modifiers & KeyModifiers.Alt) != 0 ? HotKeyModifiers.Alt : HotKeyModifiers.None;
        held |= (modifiers & KeyModifiers.Shift) != 0 ? HotKeyModifiers.Shift : HotKeyModifiers.None;
        held |= (modifiers & KeyModifiers.Meta) != 0 ? HotKeyModifiers.Windows : HotKeyModifiers.None;
        return new HotKeyGesture(held, virtualKey);
    }

    /// <summary>The Windows virtual-key code of a key a shortcut may use, or null.</summary>
    internal static int? VirtualKeyOf(Key key) => key switch
    {
        >= Key.A and <= Key.Z => 'A' + (key - Key.A),
        >= Key.D0 and <= Key.D9 => '0' + (key - Key.D0),
        >= Key.F1 and <= Key.F24 => 0x70 + (key - Key.F1),
        Key.Space => 0x20,
        Key.PageUp => 0x21,
        Key.PageDown => 0x22,
        Key.End => 0x23,
        Key.Home => 0x24,
        Key.Left => 0x25,
        Key.Up => 0x26,
        Key.Right => 0x27,
        Key.Down => 0x28,
        Key.Insert => 0x2D,
        Key.Delete => 0x2E,
        Key.Tab => 0x09,
        _ => null,
    };
}
