using Flint.Core.Shortcuts;
using Shouldly;

namespace Flint.Platform.Windows.Tests;

/// <summary>Claiming shortcuts from Windows, and reading their presses.</summary>
public sealed class GlobalHotKeysTests
{
    /// <summary>A combination nobody uses: no keyboard sold has an F24 key.</summary>
    private const HotKeyModifiers Unusual = HotKeyModifiers.Control | HotKeyModifiers.Alt | HotKeyModifiers.Shift;
    private const int F24 = 0x87;

    [Fact]
    public void AClaim_IsMadeWithoutRepeat_AndReleasedOnce()
    {
        var api = new RecordingApi();
        using var keys = new GlobalHotKeys(42, api);

        keys.Register(1, HotKeyModifiers.Control | HotKeyModifiers.Alt, 0x53).ShouldBeTrue();
        keys.Unregister(1);
        keys.Unregister(1);

        api.Calls.ShouldBe(["register 42 1 0x4003 0x53", "unregister 42 1"]);
    }

    [Fact]
    public void ClaimingAgainUnderTheSameId_LetsGoOfTheOldCombinationFirst()
    {
        var api = new RecordingApi();
        using var keys = new GlobalHotKeys(42, api);

        keys.Register(1, HotKeyModifiers.Control, 0x41);
        keys.Register(1, HotKeyModifiers.Control, 0x42);

        api.Calls.ShouldBe(["register 42 1 0x4002 0x41", "unregister 42 1", "register 42 1 0x4002 0x42"]);
    }

    [Fact]
    public void ACombinationWindowsRefuses_IsNotHeld()
    {
        var api = new RecordingApi { Refuses = true };
        var keys = new GlobalHotKeys(42, api);

        keys.Register(1, HotKeyModifiers.Control, 0x41).ShouldBeFalse();
        keys.Dispose();

        api.Calls.ShouldBe(["register 42 1 0x4002 0x41"], "nothing to let go of");
    }

    [Fact]
    public void Presses_AreReadFromTheWindowsMessages_AndOtherMessagesAreLeftAlone()
    {
        using var keys = new GlobalHotKeys(42, new RecordingApi());
        var pressed = new List<int>();
        keys.Pressed += pressed.Add;

        keys.OnMessage(GlobalHotKeys.HotKeyMessage, 7).ShouldBeTrue();
        keys.OnMessage(0x0100, 8).ShouldBeFalse();

        pressed.ShouldBe([7]);
    }

    [Fact]
    public void APressWithNobodyListening_IsStillReadAsAShortcut()
    {
        using var keys = new GlobalHotKeys(42, new RecordingApi());

        keys.OnMessage(GlobalHotKeys.HotKeyMessage, 7).ShouldBeTrue();
    }

    [Fact]
    public void Disposing_LetsGoOfEverythingHeld()
    {
        var api = new RecordingApi();
        var keys = new GlobalHotKeys(42, api);
        keys.Register(1, HotKeyModifiers.Alt, 0x41);
        keys.Register(2, HotKeyModifiers.Alt, 0x42);

        keys.Dispose();

        api.Calls.Where(call => call.StartsWith("unregister", StringComparison.Ordinal)).Order().ShouldBe(["unregister 42 1", "unregister 42 2"]);
    }

    /// <summary>
    /// The real API, for this thread rather than a window: a combination can be claimed once, and a
    /// second claim of it is refused as it would be for another program.
    /// </summary>
    [Fact]
    public void TheRealApi_ClaimsACombinationOnce()
    {
        using var first = new GlobalHotKeys(0);
        using var second = new GlobalHotKeys(0);

        var claimed = first.Register(0x7A01, Unusual, F24);
        if (!claimed)
        {
            // Something on this PC already holds it; nothing more to learn here.
            return;
        }

        second.Register(0x7A02, Unusual, F24).ShouldBeFalse("Windows gives a combination to one claimant");
        first.Unregister(0x7A01);
        second.Register(0x7A02, Unusual, F24).ShouldBeTrue("free again once released");
    }

    private sealed class RecordingApi : GlobalHotKeys.IHotKeyApi
    {
        public bool Refuses { get; init; }

        public List<string> Calls { get; } = [];

        public bool Register(nint window, int id, uint modifiers, uint virtualKey)
        {
            Calls.Add($"register {window} {id} 0x{modifiers:X} 0x{virtualKey:X}");
            return !Refuses;
        }

        public void Unregister(nint window, int id) => Calls.Add($"unregister {window} {id}");
    }
}
