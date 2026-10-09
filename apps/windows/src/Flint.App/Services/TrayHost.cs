using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Flint.App.ViewModels;
using Flint.Core.Settings;

namespace Flint.App.Services;

/// <summary>Shows the tray model as Windows' own notification-area icon and menu.</summary>
/// <remarks>The thin layer: what the icon says and offers is decided by <see cref="TrayViewModel"/>.</remarks>
internal sealed class TrayHost : IDisposable
{
    private readonly TrayViewModel tray;
    private readonly ISettingsService settings;
    private readonly TrayIcon icon;
    private readonly Dictionary<TrayState, WindowIcon> icons = [];

    /// <summary>Puts the icon in the notification area for <paramref name="application"/>.</summary>
    public TrayHost(Application application, TrayViewModel tray, ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(application);
        this.tray = tray ?? throw new ArgumentNullException(nameof(tray));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        icon = new TrayIcon();
        icon.Clicked += OnClicked;
        Show();
        tray.PropertyChanged += OnTrayChanged;
        settings.Changed += OnSettingsChanged;
        TrayIcon.SetIcons(application, [icon]);
    }

    /// <summary>The icon as Windows shows it, for tests.</summary>
    internal TrayIcon Icon => icon;

    /// <inheritdoc />
    public void Dispose()
    {
        tray.PropertyChanged -= OnTrayChanged;
        settings.Changed -= OnSettingsChanged;
        icon.Clicked -= OnClicked;
        icon.IsVisible = false;
        icon.Dispose();
    }

    /// <summary>The menu Windows shows for <paramref name="items"/>: the status line, then each choice.</summary>
    internal static NativeMenu MenuFor(IReadOnlyList<TrayMenuItem> items)
    {
        var menu = new NativeMenu();
        foreach (var item in items)
        {
            if (item.Label is "Settings")
            {
                menu.Items.Add(new NativeMenuItemSeparator());
            }

            menu.Items.Add(new NativeMenuItem(item.Label)
            {
                Command = item.Command,
                IsEnabled = item.Command is not null,
            });
            if (item.Command is null)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
            }
        }

        return menu;
    }

    private void Show()
    {
        icon.ToolTipText = tray.Tooltip;
        icon.Icon = IconFor(tray.State);
        icon.Menu = MenuFor(tray.Menu);
        icon.IsVisible = settings.Current.Tray.ShowIcon;
    }

    private WindowIcon IconFor(TrayState state)
    {
        if (!icons.TryGetValue(state, out var drawn))
        {
            drawn = new WindowIcon(TrayIconArt.Draw(state));
            icons[state] = drawn;
        }

        return drawn;
    }

    /// <summary>A click on the icon, which Windows raises; called directly by tests, which have no tray.</summary>
    internal void OnClicked(object? sender, EventArgs e) => tray.OnClicked();

    private void OnTrayChanged(object? sender, PropertyChangedEventArgs change) => Show();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs change) =>
        icon.IsVisible = change.Current.Tray.ShowIcon;
}
