using Avalonia.Controls;
using Flint.App.ViewModels;

namespace Flint.App.Services;

/// <summary>Shows and hides the real window, and quits through the application.</summary>
/// <param name="window">Flint's window.</param>
/// <param name="shutdown">Quits the application.</param>
internal sealed class WindowControl(Window window, Action shutdown) : IWindowControl
{
    /// <inheritdoc />
    public void Show()
    {
        if (window.WindowState is WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();
    }

    /// <inheritdoc />
    public void Hide() => window.Hide();

    /// <inheritdoc />
    public void Shutdown() => shutdown();
}
