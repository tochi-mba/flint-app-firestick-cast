using Flint.Core.Shortcuts;
using Shouldly;

namespace Flint.Platform.Windows.Tests;

/// <summary>The invisible window shortcut presses are delivered to.</summary>
public sealed class MessageWindowTests
{
    [Fact]
    public void WhenWindowsWillNotRegisterTheClassOrMakeTheWindow_ThereIsNone()
    {
        var asked = false;

        MessageWindow.TryCreate(registered: false, () =>
        {
            asked = true;
            return 1;
        }).ShouldBeNull();
        asked.ShouldBeFalse("no window is made from a class that was never registered");
        MessageWindow.TryCreate(registered: true, () => 0).ShouldBeNull();
    }

    [Fact]
    public void Messages_ReachWhoeverListens_AndTheRestGoToWindows()
    {
        using var window = MessageWindow.TryCreate().ShouldNotBeNull();
        var heard = new List<(uint, nint)>();
        window.Received += (message, wParam, _) =>
        {
            heard.Add((message, wParam));
            return message == GlobalHotKeys.HotKeyMessage;
        };

        window.Send(GlobalHotKeys.HotKeyMessage, 7, 0).ShouldBe(0);
        window.Send(0x0400, 8, 0);

        heard.ShouldBe([(GlobalHotKeys.HotKeyMessage, (nint)7), (0x0400u, (nint)8)]);
    }

    [Fact]
    public void AShortcutPressed_ReachesTheShortcuts_ThroughTheWindow()
    {
        using var window = MessageWindow.TryCreate().ShouldNotBeNull();
        using var keys = new GlobalHotKeys(window.Handle);
        window.Received += (message, wParam, _) => keys.OnMessage(message, wParam);
        var pressed = new List<int>();
        keys.Pressed += pressed.Add;

        window.Send(GlobalHotKeys.HotKeyMessage, 3, 0);

        pressed.ShouldBe([3]);
        keys.Register(0x7A10, HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift, 0x86);
    }

    [Fact]
    public void AClosedWindow_HearsNothing_AndClosingTwiceIsHarmless()
    {
        var window = MessageWindow.TryCreate().ShouldNotBeNull();
        var handle = window.Handle;
        var heard = 0;
        window.Received += (_, _, _) =>
        {
            heard++;
            return true;
        };

        window.Dispose();
        window.Dispose();

        window.Handle.ShouldBe(0);
        handle.ShouldNotBe(0);
        heard.ShouldBe(0);
    }

    [Fact]
    public void AWindowWithNobodyListening_LeavesEverythingToWindows()
    {
        using var window = MessageWindow.TryCreate().ShouldNotBeNull();

        Should.NotThrow(() => window.Send(GlobalHotKeys.HotKeyMessage, 1, 0));
    }
}
