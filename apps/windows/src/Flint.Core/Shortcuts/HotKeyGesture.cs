using System.Collections.Frozen;

namespace Flint.Core.Shortcuts;

/// <summary>The modifier keys of a shortcut.</summary>
/// <remarks>Values match <c>MOD_ALT</c>, <c>MOD_CONTROL</c>, <c>MOD_SHIFT</c> and <c>MOD_WIN</c>, as <c>RegisterHotKey</c> takes them.</remarks>
[Flags]
public enum HotKeyModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Alt.</summary>
    Alt = 1,

    /// <summary>Ctrl.</summary>
    Control = 2,

    /// <summary>Shift.</summary>
    Shift = 4,

    /// <summary>The Windows key.</summary>
    Windows = 8,
}

/// <summary>A key combination that works from anywhere in Windows, such as Ctrl+Alt+Shift+S.</summary>
/// <param name="Modifiers">The modifier keys held.</param>
/// <param name="VirtualKey">The key pressed with them, as a Windows virtual-key code.</param>
public readonly record struct HotKeyGesture(HotKeyModifiers Modifiers, int VirtualKey)
{
    private static readonly IReadOnlyList<KeyValuePair<string, int>> KeyNames = ListKeys();
    private static readonly FrozenDictionary<string, int> KeysByName =
        KeyNames.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The name each key is shown with: the first it is listed under.</summary>
    private static readonly FrozenDictionary<int, string> NamesByKey =
        KeyNames.DistinctBy(pair => pair.Value).ToFrozenDictionary(pair => pair.Value, pair => pair.Key);

    /// <summary>Combinations Windows keeps for itself, which no program may take.</summary>
    private static readonly HashSet<HotKeyGesture> Reserved =
    [
        new(HotKeyModifiers.Alt, 0x73),
        new(HotKeyModifiers.Alt, 0x09),
        new(HotKeyModifiers.Alt | HotKeyModifiers.Shift, 0x09),
        new(HotKeyModifiers.Alt, 0x1B),
        new(HotKeyModifiers.Control, 0x1B),
        new(HotKeyModifiers.Control | HotKeyModifiers.Shift, 0x1B),
        new(HotKeyModifiers.Control | HotKeyModifiers.Alt, 0x2E),
        new(HotKeyModifiers.Alt, 0x20),
    ];

    /// <summary>Reads a shortcut as Flint writes it, such as "Ctrl+Alt+Shift+S".</summary>
    /// <param name="text">The shortcut. Case and the order of the modifiers do not matter.</param>
    /// <param name="gesture">The shortcut, when it can be used.</param>
    /// <param name="problem">Why it cannot be used, in words for the person, when it cannot.</param>
    /// <returns>Whether it can be used.</returns>
    public static bool TryParse(string? text, out HotKeyGesture gesture, out string? problem)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            problem = "Press a key combination.";
            return false;
        }

        var modifiers = HotKeyModifiers.None;
        int? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (ModifierNamed(part) is { } modifier && (modifiers & modifier) == 0)
            {
                modifiers |= modifier;
            }
            else if (key is null && KeysByName.TryGetValue(part, out var found))
            {
                key = found;
            }
            else
            {
                problem = $"\"{text.Trim()}\" is not a key combination Flint knows.";
                return false;
            }
        }

        if (key is not { } pressed)
        {
            problem = "Add a key to go with the modifiers, such as a letter.";
            return false;
        }

        return Check(new HotKeyGesture(modifiers, pressed), out gesture, out problem);
    }

    /// <summary>Checks a combination someone pressed.</summary>
    /// <returns>Whether it can be used; <paramref name="problem"/> says why not.</returns>
    public static bool Check(HotKeyGesture candidate, out HotKeyGesture gesture, out string? problem)
    {
        gesture = default;
        problem = candidate switch
        {
            _ when !NamesByKey.ContainsKey(candidate.VirtualKey) => "That key cannot be part of a shortcut.",
            { Modifiers: var held } when (held & HotKeyModifiers.Windows) != 0 =>
                "Windows keeps shortcuts with the Windows key for itself.",
            { Modifiers: var held } when (held & (HotKeyModifiers.Control | HotKeyModifiers.Alt)) == 0 =>
                "Hold Ctrl or Alt too, so the key still types as usual everywhere else.",
            _ when Reserved.Contains(candidate) => "Windows keeps that combination for itself.",
            _ => null,
        };
        if (problem is not null)
        {
            return false;
        }

        gesture = candidate;
        return true;
    }

    /// <summary>The shortcut as Flint writes and shows it, such as "Ctrl+Alt+Shift+S".</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if ((Modifiers & HotKeyModifiers.Control) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((Modifiers & HotKeyModifiers.Alt) != 0)
        {
            parts.Add("Alt");
        }

        if ((Modifiers & HotKeyModifiers.Shift) != 0)
        {
            parts.Add("Shift");
        }

        if ((Modifiers & HotKeyModifiers.Windows) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(NamesByKey.GetValueOrDefault(VirtualKey, $"Key {VirtualKey}"));
        return string.Join('+', parts);
    }

    private static HotKeyModifiers? ModifierNamed(string part) => part.ToUpperInvariant() switch
    {
        "CTRL" or "CONTROL" => HotKeyModifiers.Control,
        "ALT" => HotKeyModifiers.Alt,
        "SHIFT" => HotKeyModifiers.Shift,
        "WIN" or "WINDOWS" => HotKeyModifiers.Windows,
        _ => null,
    };

    /// <summary>Every key a shortcut may use, by the names it is written with; the first name is how it is shown.</summary>
    private static List<KeyValuePair<string, int>> ListKeys()
    {
        var keys = new List<KeyValuePair<string, int>>();
        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            keys.Add(new(letter.ToString(), letter));
        }

        for (var digit = 0; digit <= 9; digit++)
        {
            keys.Add(new(digit.ToString(System.Globalization.CultureInfo.InvariantCulture), 0x30 + digit));
        }

        for (var function = 1; function <= 24; function++)
        {
            keys.Add(new($"F{function}", 0x6F + function));
        }

        keys.AddRange(
        [
            new("Space", 0x20),
            new("PageUp", 0x21),
            new("PageDown", 0x22),
            new("End", 0x23),
            new("Home", 0x24),
            new("Left", 0x25),
            new("Up", 0x26),
            new("Right", 0x27),
            new("Down", 0x28),
            new("Insert", 0x2D),
            new("Delete", 0x2E),
            new("Tab", 0x09),
            new("Escape", 0x1B),
            new("Esc", 0x1B),
            new("Del", 0x2E),
        ]);
        return keys;
    }
}
