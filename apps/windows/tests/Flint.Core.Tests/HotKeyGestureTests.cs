using Flint.Core.Shortcuts;
using Shouldly;

namespace Flint.Core.Tests;

/// <summary>Shortcuts as Flint reads, writes and checks them.</summary>
public sealed class HotKeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Shift+S", HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift, 0x53)]
    [InlineData("Ctrl+Alt+Shift+Space", HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift, 0x20)]
    [InlineData("Ctrl+Alt+Shift+Right", HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift, 0x27)]
    [InlineData("Ctrl+F5", HotKeyModifiers.Control, 0x74)]
    [InlineData("Alt+F24", HotKeyModifiers.Alt, 0x87)]
    [InlineData("Ctrl+Shift+7", HotKeyModifiers.Control | HotKeyModifiers.Shift, 0x37)]
    [InlineData("Alt+Shift+Left", HotKeyModifiers.Alt | HotKeyModifiers.Shift, 0x25)]
    [InlineData("Ctrl+Alt+Up", HotKeyModifiers.Control | HotKeyModifiers.Alt, 0x26)]
    [InlineData("Ctrl+Down", HotKeyModifiers.Control, 0x28)]
    [InlineData("Ctrl+Home", HotKeyModifiers.Control, 0x24)]
    [InlineData("Ctrl+End", HotKeyModifiers.Control, 0x23)]
    [InlineData("Ctrl+PageUp", HotKeyModifiers.Control, 0x21)]
    [InlineData("Ctrl+PageDown", HotKeyModifiers.Control, 0x22)]
    [InlineData("Ctrl+Insert", HotKeyModifiers.Control, 0x2D)]
    [InlineData("Ctrl+Tab", HotKeyModifiers.Control, 0x09)]
    [InlineData("Ctrl+Alt+Escape", HotKeyModifiers.Control | HotKeyModifiers.Alt, 0x1B)]
    public void AShortcut_ReadsAndWritesBackTheSame(string text, HotKeyModifiers modifiers, int key)
    {
        HotKeyGesture.TryParse(text, out var gesture, out var problem).ShouldBeTrue(problem);

        gesture.ShouldBe(new HotKeyGesture(modifiers, key));
        gesture.ToString().ShouldBe(text);
        problem.ShouldBeNull();
    }

    [Theory]
    [InlineData("ctrl+alt+shift+s")]
    [InlineData("Control+Alt+Shift+S")]
    [InlineData("Shift+Alt+Ctrl+S")]
    [InlineData(" Ctrl + Alt + Shift + S ")]
    [InlineData("S+Ctrl+Shift+Alt")]
    public void CaseSpacingAndOrder_DoNotMatter(string text)
    {
        HotKeyGesture.TryParse(text, out var gesture, out _).ShouldBeTrue();

        gesture.ToString().ShouldBe("Ctrl+Alt+Shift+S");
    }

    [Theory]
    [InlineData("Ctrl+Esc+Alt", "Ctrl+Alt+Escape")]
    [InlineData("ctrl+shift+del", "Ctrl+Shift+Delete")]
    public void ShortNames_AreShownInFull(string text, string shown)
    {
        HotKeyGesture.TryParse(text, out var gesture, out _).ShouldBeTrue();

        gesture.ToString().ShouldBe(shown);
    }

    [Theory]
    [InlineData(null, "Press a key combination.")]
    [InlineData("", "Press a key combination.")]
    [InlineData("   ", "Press a key combination.")]
    [InlineData("S", "Hold Ctrl or Alt too, so the key still types as usual everywhere else.")]
    [InlineData("Shift+S", "Hold Ctrl or Alt too, so the key still types as usual everywhere else.")]
    [InlineData("Ctrl+Alt", "Add a key to go with the modifiers, such as a letter.")]
    [InlineData("Win+S", "Windows keeps shortcuts with the Windows key for itself.")]
    [InlineData("Ctrl+Windows+S", "Windows keeps shortcuts with the Windows key for itself.")]
    [InlineData("Alt+F4", "Windows keeps that combination for itself.")]
    [InlineData("Alt+Tab", "Windows keeps that combination for itself.")]
    [InlineData("Alt+Shift+Tab", "Windows keeps that combination for itself.")]
    [InlineData("Alt+Esc", "Windows keeps that combination for itself.")]
    [InlineData("Ctrl+Esc", "Windows keeps that combination for itself.")]
    [InlineData("Ctrl+Shift+Esc", "Windows keeps that combination for itself.")]
    [InlineData("Alt+Space", "Windows keeps that combination for itself.")]
    [InlineData("Ctrl+Alt+Del", "Windows keeps that combination for itself.")]
    [InlineData("Ctrl+Hyper+S", "\"Ctrl+Hyper+S\" is not a key combination Flint knows.")]
    [InlineData("Ctrl+S+T", "\"Ctrl+S+T\" is not a key combination Flint knows.")]
    [InlineData("Ctrl+Ctrl+S", "\"Ctrl+Ctrl+S\" is not a key combination Flint knows.")]
    [InlineData("Ctrl++S", "\"Ctrl++S\" is not a key combination Flint knows.")]
    [InlineData("Ctrl+F25", "\"Ctrl+F25\" is not a key combination Flint knows.")]
    public void WhatCannotBeUsed_IsRefused_WithTheReason(string? text, string reason)
    {
        HotKeyGesture.TryParse(text, out var gesture, out var problem).ShouldBeFalse();

        problem.ShouldBe(reason);
        gesture.ShouldBe(default);
    }

    [Fact]
    public void APressedCombination_IsCheckedTheSameWay()
    {
        HotKeyGesture.Check(new HotKeyGesture(HotKeyModifiers.Control, 0x41), out var ok, out var none).ShouldBeTrue();
        ok.ToString().ShouldBe("Ctrl+A");
        none.ShouldBeNull();

        HotKeyGesture.Check(new HotKeyGesture(HotKeyModifiers.Control, 0xA0), out _, out var problem).ShouldBeFalse();
        problem.ShouldBe("That key cannot be part of a shortcut.");
        new HotKeyGesture(HotKeyModifiers.Windows, 0xA0).ToString().ShouldBe("Win+Key 160");
    }

    [Fact]
    public void Equality_IgnoresHowTheShortcutWasWritten()
    {
        HotKeyGesture.TryParse("Shift+Ctrl+Up", out var first, out _).ShouldBeTrue();
        HotKeyGesture.TryParse("ctrl+shift+up", out var second, out _).ShouldBeTrue();

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void EveryShortcutFlintShipsWith_CanBeUsed()
    {
        var defaults = new Settings.ShortcutSettings();
        string?[] shipped =
        [
            defaults.StartStopSharing, defaults.PauseResumeSharing, defaults.PlayPauseTv, defaults.NextItem,
            defaults.PreviousItem, defaults.VolumeUp, defaults.VolumeDown, defaults.ShowWindow,
        ];

        foreach (var text in shipped)
        {
            HotKeyGesture.TryParse(text, out var gesture, out var problem).ShouldBeTrue($"{text}: {problem}");
            gesture.ToString().ShouldBe(text);
        }
    }
}
