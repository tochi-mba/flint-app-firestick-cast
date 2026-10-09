using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.App.Views;
using Flint.Core.Shortcuts;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>Showing, hiding and quitting through a real window, and shortcuts pressed in it.</summary>
public sealed class WindowControlTests
{
    [AvaloniaFact]
    public void Showing_RestoresAMinimisedWindow_AndHidingHidesIt()
    {
        var window = new Window { Width = 400, Height = 300 };
        var shutDowns = 0;
        var control = new WindowControl(window, () => shutDowns++);

        control.Show();
        window.IsVisible.ShouldBeTrue();
        window.WindowState = WindowState.Minimized;
        control.Show();
        window.WindowState.ShouldBe(WindowState.Normal);

        control.Hide();
        window.IsVisible.ShouldBeFalse();
        control.Shutdown();
        shutDowns.ShouldBe(1);
        window.Close();
    }

    [AvaloniaFact]
    public void AShortcutPressedInTheWindow_RunsWhenShortcutsWorkOnlyThere()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        var recorded = new RecordingWindow();
        shell.UseWindow(recorded);
        shell.SettingsService.Update(current => current with { Shortcuts = current.Shortcuts with { WorkInBackground = false } });
        using var service = new HotKeyService(new NoClaims(), shell.SettingsService, shell.RunShortcut);
        shell.UseShortcuts(service);
        var window = new MainWindow { DataContext = shell, Width = 1180, Height = 780 };
        window.Show();

        var pressed = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift };
        window.RaiseEvent(pressed);

        pressed.Handled.ShouldBeTrue();
        recorded.Shown.ShouldBe(1);
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift, Handled = true });
        recorded.Shown.ShouldBe(1, "a key something in the window already took is not a shortcut");
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Q, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt });
        recorded.Shown.ShouldBe(1, "not a shortcut");
        window.Close();
    }

    private sealed class NoClaims : IHotKeyRegistrar
    {
        public event Action<int>? Pressed
        {
            add { }
            remove { }
        }

        public bool Register(int id, HotKeyModifiers modifiers, int virtualKey) => true;

        public void Unregister(int id)
        {
        }
    }
}
