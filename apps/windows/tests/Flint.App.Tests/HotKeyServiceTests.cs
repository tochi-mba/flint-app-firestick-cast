using Flint.App.Services;
using Flint.Core.Settings;
using Flint.Core.Shortcuts;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>Claiming the shortcuts the settings name, following changes, and running their actions.</summary>
public sealed class HotKeyServiceTests : IDisposable
{
    private readonly SettingsService settings = new(new InMemoryAppSettingsStore());
    private readonly FakeRegistrar registrar = new();
    private readonly List<ShortcutAction> ran = [];

    public void Dispose() => settings.Dispose();

    [Fact]
    public void Starting_ClaimsEveryAssignedShortcut_AndNothingUnassigned()
    {
        using var service = Service();

        registrar.Held.Keys.Order().ShouldBe(Enumerable.Range(1, 8), "Disconnect has no shortcut until one is chosen");
        registrar.Held[(int)ShortcutAction.StartStopSharing].ShouldBe("Ctrl+Alt+Shift+S");
        service.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void ChangingOneShortcut_ReleasesOnlyTheOld_AndClaimsOnlyTheNew()
    {
        using var service = Service();
        registrar.Calls.Clear();

        settings.Update(current => current with { Shortcuts = current.Shortcuts with { ShowWindow = "Ctrl+Alt+F" } });

        registrar.Calls.ShouldBe(["unregister 8", "register 8 Ctrl+Alt+F"]);
    }

    [Fact]
    public void ACombinationAnotherProgramHolds_IsReported_AndClearedWhenItIsFree()
    {
        registrar.Taken.Add("Ctrl+Alt+Shift+P");
        using var service = Service();
        var told = 0;
        service.ProblemsChanged += (_, _) => told++;

        service.Problems[ShortcutAction.PauseResumeSharing].ShouldBe("Another program already uses Ctrl+Alt+Shift+P.");
        registrar.Held.ShouldNotContainKey((int)ShortcutAction.PauseResumeSharing);

        settings.Update(current => current with { Shortcuts = current.Shortcuts with { PauseResumeSharing = "Ctrl+Alt+P" } });

        service.Problems.ShouldBeEmpty();
        told.ShouldBe(1);
        settings.Update(current => current with { Shortcuts = current.Shortcuts with { VolumeUp = "Ctrl+Alt+U" } });
        told.ShouldBe(1, "nothing about the problems changed");
    }

    [Fact]
    public void Assigning_ChecksWithWindowsFirst_AndKeepsTheOldShortcutWhenRefused()
    {
        using var service = Service();
        registrar.Taken.Add("Ctrl+Alt+K");
        var taken = Gesture("Ctrl+Alt+K");
        var free = Gesture("Ctrl+Alt+J");

        service.TryAssign(ShortcutAction.ShowWindow, taken).ShouldBe("Another program already uses Ctrl+Alt+K.");
        settings.Current.Shortcuts.ShowWindow.ShouldBe("Ctrl+Alt+Shift+F");

        service.TryAssign(ShortcutAction.ShowWindow, free).ShouldBeNull();
        settings.Current.Shortcuts.ShowWindow.ShouldBe("Ctrl+Alt+J");
        registrar.Held[(int)ShortcutAction.ShowWindow].ShouldBe("Ctrl+Alt+J");
        registrar.Held.ShouldNotContainKey(99, "the trial claim is let go");

        var own = Gesture("Ctrl+Alt+Shift+S");
        service.TryAssign(ShortcutAction.StartStopSharing, own).ShouldBeNull("already Flint's, so not tried again");
    }

    [Fact]
    public void TheOwnerOfACombination_IsFound_ForMovingItHere()
    {
        using var service = Service();
        var sharing = Gesture("Ctrl+Alt+Shift+S");
        var nobody = Gesture("Ctrl+Alt+Q");

        service.OwnerOf(sharing, ShortcutAction.ShowWindow).ShouldBe(ShortcutAction.StartStopSharing);
        service.OwnerOf(sharing, ShortcutAction.StartStopSharing).ShouldBeNull("its own shortcut is not a clash");
        service.OwnerOf(nobody, ShortcutAction.ShowWindow).ShouldBeNull();
    }

    [Fact]
    public void WorkingOnlyInFlint_ReleasesEverything_AndRunsShortcutsPressedInTheWindow()
    {
        using var service = Service();
        var show = Gesture("Ctrl+Alt+Shift+F");
        var nothing = Gesture("Ctrl+Alt+Q");
        service.OnWindowKey(show).ShouldBeFalse("they work everywhere, so the window leaves them to Windows");

        settings.Update(current => current with { Shortcuts = current.Shortcuts with { WorkInBackground = false } });

        registrar.Held.ShouldBeEmpty();
        service.OnWindowKey(show).ShouldBeTrue();
        service.OnWindowKey(nothing).ShouldBeFalse();
        ran.ShouldBe([ShortcutAction.ShowWindow]);
        service.TryAssign(ShortcutAction.Disconnect, nothing).ShouldBeNull("nothing to claim from Windows");
        registrar.Calls.ShouldNotContain("register 99 Ctrl+Alt+Q");

        settings.Update(current => current with { Shortcuts = current.Shortcuts with { WorkInBackground = true } });
        registrar.Held.Count.ShouldBe(9);
    }

    [Fact]
    public void Presses_RunTheirActions_AndMediaKeysOnlyWhenTheSettingSays()
    {
        using var service = Service();
        registrar.Press((int)ShortcutAction.PlayPauseTv);
        registrar.Press(HotKeyService.MediaKeyBase);
        registrar.Press(55);
        ran.ShouldBe([ShortcutAction.PlayPauseTv]);
        registrar.Held.Keys.ShouldNotContain(HotKeyService.MediaKeyBase);

        settings.Update(current => current with { Shortcuts = current.Shortcuts with { UseMediaKeys = true } });

        registrar.Held[HotKeyService.MediaKeyBase].ShouldBe("Key 179");
        registrar.Press(HotKeyService.MediaKeyBase + 1);
        registrar.Press(HotKeyService.MediaKeyBase + 2);
        registrar.Press(HotKeyService.MediaKeyBase + 3);
        ran.ShouldBe([ShortcutAction.PlayPauseTv, ShortcutAction.NextItem, ShortcutAction.PreviousItem]);

        settings.Update(current => current with { Shortcuts = current.Shortcuts with { UseMediaKeys = false } });
        registrar.Held.Keys.ShouldNotContain(HotKeyService.MediaKeyBase);
    }

    [Fact]
    public void AnActionThatCannotRunNow_DoesNothing()
    {
        using var service = new HotKeyService(registrar, settings, action =>
        {
            ran.Add(action);
            return false;
        });

        registrar.Press((int)ShortcutAction.PauseResumeSharing);

        ran.ShouldBe([ShortcutAction.PauseResumeSharing]);
    }

    [Fact]
    public void Disposing_ReleasesEverything_AndStopsListening()
    {
        settings.Update(current => current with { Shortcuts = current.Shortcuts with { UseMediaKeys = true } });
        var service = Service();

        service.Dispose();
        registrar.Press((int)ShortcutAction.ShowWindow);
        settings.Update(current => current with { Shortcuts = current.Shortcuts with { ShowWindow = "Ctrl+Alt+F" } });

        registrar.Held.ShouldBeEmpty();
        ran.ShouldBeEmpty();
    }

    [Fact]
    public void AChangeToOtherSettings_LeavesTheShortcutsAlone()
    {
        using var service = Service();
        registrar.Calls.Clear();

        settings.Update(current => current with { General = current.General with { KeepAwake = !current.General.KeepAwake } });

        registrar.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void EveryAction_ReadsAndWritesItsOwnSetting()
    {
        var shortcuts = new ShortcutSettings();
        foreach (var action in Enum.GetValues<ShortcutAction>())
        {
            var changed = HotKeyService.With(shortcuts, action, "Ctrl+Alt+9");

            HotKeyService.TextFor(changed, action).ShouldBe("Ctrl+Alt+9");
            Enum.GetValues<ShortcutAction>().Where(other => other != action)
                .ShouldAllBe(other => HotKeyService.TextFor(changed, other) == HotKeyService.TextFor(shortcuts, other));
        }

        Should.Throw<ArgumentOutOfRangeException>(() => HotKeyService.TextFor(shortcuts, (ShortcutAction)42));
        Should.Throw<ArgumentOutOfRangeException>(() => HotKeyService.With(shortcuts, (ShortcutAction)42, null));
        Should.Throw<ArgumentNullException>(() => HotKeyService.TextFor(null!, ShortcutAction.ShowWindow));
        Should.Throw<ArgumentNullException>(() => HotKeyService.With(null!, ShortcutAction.ShowWindow, null));
    }

    [Fact]
    public void WhatItNeeds_IsRequired()
    {
        Should.Throw<ArgumentNullException>(() => new HotKeyService(null!, settings, _ => true));
        Should.Throw<ArgumentNullException>(() => new HotKeyService(registrar, null!, _ => true));
        Should.Throw<ArgumentNullException>(() => new HotKeyService(registrar, settings, null!));
    }

    private static HotKeyGesture Gesture(string text)
    {
        HotKeyGesture.TryParse(text, out var gesture, out var problem).ShouldBeTrue(problem);
        return gesture;
    }

    private HotKeyService Service() => new(registrar, settings, action =>
    {
        ran.Add(action);
        return true;
    });

    /// <summary>Windows' side, as a test plays it: some combinations are taken by other programs.</summary>
    private sealed class FakeRegistrar : IHotKeyRegistrar
    {
        public event Action<int>? Pressed;

        public HashSet<string> Taken { get; } = [];

        public Dictionary<int, string> Held { get; } = [];

        public List<string> Calls { get; } = [];

        public bool Register(int id, HotKeyModifiers modifiers, int virtualKey)
        {
            var text = new HotKeyGesture(modifiers, virtualKey).ToString();
            Calls.Add($"register {id} {text}");
            if (Taken.Contains(text))
            {
                return false;
            }

            Held[id] = text;
            return true;
        }

        public void Unregister(int id)
        {
            Calls.Add($"unregister {id}");
            Held.Remove(id);
        }

        public void Press(int id) => Pressed?.Invoke(id);
    }
}
