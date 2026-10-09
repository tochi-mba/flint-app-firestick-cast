using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Flint.App.Services;
using Flint.App.ViewModels.Settings;

namespace Flint.App.Views;

/// <summary>The Shortcuts section, which also takes the combination a row is waiting for.</summary>
public partial class ShortcutsSettingsSection : UserControl
{
    /// <summary>Initialises the section.</summary>
    public ShortcutsSettingsSection()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// While a row waits, the next combination is its new shortcut: Escape cancels, and a modifier
    /// on its own is the person still choosing.
    /// </summary>
    internal void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if ((DataContext as ShortcutsSettingsViewModel)?.Rows.FirstOrDefault(row => row.IsCapturing) is not { } row)
        {
            return;
        }

        e.Handled = true;
        if (e.Key is Key.Escape)
        {
            row.CancelCapture();
        }
        else if (ShortcutKeys.Of(e.Key, e.KeyModifiers) is { } pressed)
        {
            row.Capture(pressed);
        }
    }
}
