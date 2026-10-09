using Flint.App.ViewModels;
using Flint.Platform.Windows;

namespace Flint.App.Services;

/// <summary>Flint's shortcuts, claimed from Windows for an invisible window of their own.</summary>
/// <remarks>
/// Their own window rather than Flint's, because Flint's has no handle until it is first shown,
/// and shortcuts must work while Flint waits in the tray.
/// </remarks>
internal sealed class GlobalShortcuts : IDisposable
{
    private readonly MessageWindow window;
    private readonly GlobalHotKeys keys;
    private readonly HotKeyService service;

    private GlobalShortcuts(MessageWindow window, GlobalHotKeys keys, HotKeyService service)
    {
        this.window = window;
        this.keys = keys;
        this.service = service;
    }

    /// <summary>Claims the shortcuts the settings name, for <paramref name="shell"/> to act on.</summary>
    /// <returns>The claim, or null when Windows will not make the window; Flint then has no shortcuts.</returns>
    public static GlobalShortcuts? Start(MainWindowViewModel shell) => Start(shell, MessageWindow.TryCreate);

    /// <summary>Claims the shortcuts for a window made by <paramref name="createWindow"/>; for tests.</summary>
    internal static GlobalShortcuts? Start(MainWindowViewModel shell, Func<MessageWindow?> createWindow)
    {
        ArgumentNullException.ThrowIfNull(shell);
        return createWindow() is { } window ? Claim(shell, window) : null;
    }

    /// <summary>The window presses arrive at, for tests.</summary>
    internal MessageWindow Window => window;

    /// <inheritdoc />
    public void Dispose()
    {
        service.Dispose();
        keys.Dispose();
        window.Dispose();
    }

    private static GlobalShortcuts Claim(MainWindowViewModel shell, MessageWindow window)
    {
        var keys = new GlobalHotKeys(window.Handle);
        window.Received += (message, wParam, _) => keys.OnMessage(message, wParam);
        var service = new HotKeyService(keys, shell.SettingsService, shell.RunShortcut);
        shell.UseShortcuts(service);
        return new GlobalShortcuts(window, keys, service);
    }
}
