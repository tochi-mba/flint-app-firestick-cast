using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flint.App.Services;
using Flint.App.ViewModels.Settings;
using Flint.App.Views;
using Flint.Core.Settings;
using Flint.Core.Shortcuts;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>The Shortcuts section: changing, clearing, refusing, moving and restoring shortcuts.</summary>
public sealed class ShortcutsSettingsTests : IDisposable
{
    private readonly SettingsService settings = new(new InMemoryAppSettingsStore());

    public void Dispose() => settings.Dispose();

    [Fact]
    public void EveryRow_ShowsItsShortcut_OrNone()
    {
        var section = new ShortcutsSettingsViewModel(settings);

        section.Title.ShouldBe("Shortcuts");
        Row(section, ShortcutAction.StartStopSharing).Shortcut.ShouldBe("Ctrl+Alt+Shift+S");
        Row(section, ShortcutAction.Disconnect).Shortcut.ShouldBe("None");
        section.Rows.Count.ShouldBe(9);
        section.Settings.Single(setting => setting.Matches("pause or resume")).ShouldBe(Row(section, ShortcutAction.PauseResumeSharing).Text);
        section.Settings.ShouldContain(section.MediaKeysText);
        section.Settings.ShouldContain(section.WorkInBackgroundText);
    }

    [Fact]
    public void ChangingARowThatIsAlreadyWaiting_NeverStopsItWaiting()
    {
        var section = new ShortcutsSettingsViewModel(settings);
        var row = Row(section, ShortcutAction.Disconnect);
        row.ChangeCommand.Execute(null);
        var seen = new List<bool>();
        row.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(row.IsCapturing))
            {
                seen.Add(row.IsCapturing);
            }
        };

        row.ChangeCommand.Execute(null);

        seen.ShouldBeEmpty("stopping and starting again would release and claim the shortcuts for nothing");
        row.IsCapturing.ShouldBeTrue();
    }

    [Fact]
    public void Changing_WaitsForACombination_AndTakesIt()
    {
        var section = new ShortcutsSettingsViewModel(settings);
        var row = Row(section, ShortcutAction.Disconnect);

        row.ChangeCommand.Execute(null);
        row.IsCapturing.ShouldBeTrue();
        section.IsCapturing.ShouldBeTrue();
        row.Status.ShouldBe("Press the new shortcut. Escape cancels.");

        row.Capture(Gesture("Ctrl+Alt+D"));

        row.IsCapturing.ShouldBeFalse();
        row.Status.ShouldBeNull();
        settings.Current.Shortcuts.Disconnect.ShouldBe("Ctrl+Alt+D");
        row.Shortcut.ShouldBe("Ctrl+Alt+D");
    }

    [Fact]
    public void ACombinationThatCannotBeUsed_IsRefusedWithTheReason_AndTheRowKeepsWaiting()
    {
        var section = new ShortcutsSettingsViewModel(settings);
        var row = Row(section, ShortcutAction.ShowWindow);
        row.ChangeCommand.Execute(null);

        row.Capture(new HotKeyGesture(HotKeyModifiers.Shift, 'S'));

        row.Status.ShouldBe("Hold Ctrl or Alt too, so the key still types as usual everywhere else.");
        row.IsCapturing.ShouldBeTrue();
        settings.Current.Shortcuts.ShowWindow.ShouldBe("Ctrl+Alt+Shift+F");
    }

    [Fact]
    public void ACombinationAnotherActionHas_AsksToMoveIt_AndMovingLeavesTheOtherWithout()
    {
        var section = new ShortcutsSettingsViewModel(settings);
        var row = Row(section, ShortcutAction.ShowWindow);
        row.ChangeCommand.Execute(null);

        row.Capture(Gesture("Ctrl+Alt+Shift+S"));

        row.IsAskingToMove.ShouldBeTrue();
        row.Status.ShouldBe("Ctrl+Alt+Shift+S is already the shortcut to start or stop sharing. Move it here?");
        row.MoveCommand.Execute(null);

        settings.Current.Shortcuts.ShowWindow.ShouldBe("Ctrl+Alt+Shift+S");
        settings.Current.Shortcuts.StartStopSharing.ShouldBeNull();
        row.IsAskingToMove.ShouldBeFalse();
        row.IsCapturing.ShouldBeFalse();
    }

    [Fact]
    public void KeepingIt_LeavesBothAsTheyWere()
    {
        var section = new ShortcutsSettingsViewModel(settings);
        var row = Row(section, ShortcutAction.ShowWindow);
        row.ChangeCommand.Execute(null);
        row.Capture(Gesture("Ctrl+Alt+Shift+S"));

        row.KeepItCommand.Execute(null);
        row.MoveCommand.Execute(null);

        settings.Current.Shortcuts.ShowWindow.ShouldBe("Ctrl+Alt+Shift+F");
        settings.Current.Shortcuts.StartStopSharing.ShouldBe("Ctrl+Alt+Shift+S");
        row.Status.ShouldBeNull();
    }

    [Fact]
    public void OnlyOneRowWaits_AndAPressWhileNotWaitingIsIgnored()
    {
        var section = new ShortcutsSettingsViewModel(settings);
        var first = Row(section, ShortcutAction.NextItem);
        var second = Row(section, ShortcutAction.PreviousItem);

        first.ChangeCommand.Execute(null);
        second.ChangeCommand.Execute(null);

        first.IsCapturing.ShouldBeFalse();
        second.IsCapturing.ShouldBeTrue();
        first.Capture(Gesture("Ctrl+Alt+N"));
        settings.Current.Shortcuts.NextItem.ShouldBe("Ctrl+Alt+Shift+Right");
    }

    [Fact]
    public void Clearing_AndRestoring_WriteTheSettings()
    {
        var section = new ShortcutsSettingsViewModel(settings);

        Row(section, ShortcutAction.VolumeUp).ClearCommand.Execute(null);
        settings.Current.Shortcuts.VolumeUp.ShouldBeNull();
        Row(section, ShortcutAction.VolumeUp).Shortcut.ShouldBe("None");
        section.WorkInBackground = false;
        section.UseMediaKeys = true;
        section.WorkInBackground.ShouldBeFalse();
        section.UseMediaKeys.ShouldBeTrue();

        section.Reset.AskCommand.Execute(null);
        section.Reset.ConfirmCommand.Execute(null);

        settings.Current.Shortcuts.ShouldBe(new ShortcutSettings());
    }

    [Fact]
    public void WithTheService_AssigningGoesThroughWindows_AndProblemsShowOnTheirRows()
    {
        var registrar = new TakenRegistrar("Ctrl+Alt+Shift+P", "Ctrl+Alt+K");
        using var service = new HotKeyService(registrar, settings, _ => true);
        var section = new ShortcutsSettingsViewModel(settings);
        section.UseService(service);

        Row(section, ShortcutAction.PauseResumeSharing).Problem.ShouldBe("Another program already uses Ctrl+Alt+Shift+P.");
        var row = Row(section, ShortcutAction.ShowWindow);
        row.ChangeCommand.Execute(null);
        row.Capture(Gesture("Ctrl+Alt+K"));

        row.Status.ShouldBe("Another program already uses Ctrl+Alt+K.");
        row.IsCapturing.ShouldBeFalse();
        settings.Current.Shortcuts.ShowWindow.ShouldBe("Ctrl+Alt+Shift+F");

        Row(section, ShortcutAction.PauseResumeSharing).ChangeCommand.Execute(null);
        Row(section, ShortcutAction.PauseResumeSharing).Capture(Gesture("Ctrl+Alt+P"));
        Row(section, ShortcutAction.PauseResumeSharing).Problem.ShouldBeNull();
        section.UseService(service);
        Should.Throw<ArgumentNullException>(() => section.UseService(null!));
    }

    [AvaloniaFact]
    public void ASectionWithNothingBehindIt_IgnoresKeys()
    {
        var section = new ShortcutsSettingsSection();

        Should.NotThrow(() => Press(section, Key.D, KeyModifiers.Control | KeyModifiers.Alt));
        Should.Throw<ArgumentNullException>(() => new ShortcutsSettingsViewModel(null!));
    }

    [AvaloniaFact]
    public void TheSection_TakesTheNextCombination_AndEscapeCancels()
    {
        var model = new ShortcutsSettingsViewModel(settings);
        var section = new ShortcutsSettingsSection { DataContext = model };
        var window = new Window { Width = 900, Height = 1400, Content = section };
        try
        {
            window.Show();
            var row = Row(model, ShortcutAction.Disconnect);

            Press(section, Key.D, KeyModifiers.Control | KeyModifiers.Alt);
            settings.Current.Shortcuts.Disconnect.ShouldBeNull("nothing is waiting, so the key is left alone");

            row.ChangeCommand.Execute(null);
            Press(section, Key.LeftCtrl, KeyModifiers.Control);
            row.IsCapturing.ShouldBeTrue("a modifier on its own is the person still choosing");
            Press(section, Key.Escape, KeyModifiers.None);
            row.IsCapturing.ShouldBeFalse();

            row.ChangeCommand.Execute(null);
            Press(section, Key.D, KeyModifiers.Control | KeyModifiers.Alt);
            settings.Current.Shortcuts.Disconnect.ShouldBe("Ctrl+Alt+D");
            window.UpdateLayout();
            section.FindControl<ItemsControl>("ShortcutRows")!.ItemCount.ShouldBe(9);
        }
        finally
        {
            window.Close();
        }
    }

    [Theory]
    [InlineData(Key.A, KeyModifiers.Control, "Ctrl+A")]
    [InlineData(Key.Z, KeyModifiers.Alt | KeyModifiers.Shift, "Alt+Shift+Z")]
    [InlineData(Key.D7, KeyModifiers.Control, "Ctrl+7")]
    [InlineData(Key.F12, KeyModifiers.Control, "Ctrl+F12")]
    [InlineData(Key.F24, KeyModifiers.Control, "Ctrl+F24")]
    [InlineData(Key.Space, KeyModifiers.Control, "Ctrl+Space")]
    [InlineData(Key.PageUp, KeyModifiers.Control, "Ctrl+PageUp")]
    [InlineData(Key.PageDown, KeyModifiers.Control, "Ctrl+PageDown")]
    [InlineData(Key.End, KeyModifiers.Control, "Ctrl+End")]
    [InlineData(Key.Home, KeyModifiers.Control, "Ctrl+Home")]
    [InlineData(Key.Left, KeyModifiers.Control, "Ctrl+Left")]
    [InlineData(Key.Up, KeyModifiers.Control, "Ctrl+Up")]
    [InlineData(Key.Right, KeyModifiers.Control, "Ctrl+Right")]
    [InlineData(Key.Down, KeyModifiers.Control, "Ctrl+Down")]
    [InlineData(Key.Insert, KeyModifiers.Control, "Ctrl+Insert")]
    [InlineData(Key.Delete, KeyModifiers.Control, "Ctrl+Delete")]
    [InlineData(Key.Tab, KeyModifiers.Control, "Ctrl+Tab")]
    [InlineData(Key.S, KeyModifiers.Meta, "Win+S")]
    public void APressedKey_IsTheCombinationShortcutsAreMadeOf(Key key, KeyModifiers modifiers, string expected)
    {
        ShortcutKeys.Of(key, modifiers)!.Value.ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.Escape)]
    [InlineData(Key.Enter)]
    public void AKeyNoShortcutUses_IsNotACombination(Key key)
    {
        ShortcutKeys.Of(key, KeyModifiers.Control).ShouldBeNull();
    }

    private static ShortcutRowViewModel Row(ShortcutsSettingsViewModel section, ShortcutAction action) =>
        section.Rows.Single(row => row.Action == action);

    private static HotKeyGesture Gesture(string text)
    {
        HotKeyGesture.TryParse(text, out var gesture, out var problem).ShouldBeTrue(problem);
        return gesture;
    }

    private static void Press(ShortcutsSettingsSection section, Key key, KeyModifiers modifiers) =>
        section.OnKeyDown(section, new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });

    /// <summary>Windows, with some combinations already taken by other programs.</summary>
    private sealed class TakenRegistrar(params string[] taken) : IHotKeyRegistrar
    {
        public event Action<int>? Pressed
        {
            add { }
            remove { }
        }

        public bool Register(int id, HotKeyModifiers modifiers, int virtualKey) =>
            !taken.Contains(new HotKeyGesture(modifiers, virtualKey).ToString());

        public void Unregister(int id)
        {
        }
    }
}
