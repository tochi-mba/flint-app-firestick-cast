using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.App.Views;
using Flint.Core.Settings;
using Flint.Platform.Windows;

namespace Flint.App;

/// <summary>
/// The Flint application, a REX Technologies product.
/// </summary>
public sealed class FlintApplication : Application
{
    /// <summary>This copy's single-instance identity, which a second launch asks to show the window.</summary>
    internal static FlintSingleInstance? Instance { get; set; }

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        DevFileLog.StartUiWatchdog();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var started = Start(this, MainWindowViewModel.CreateDefault(), desktop.Args, () => desktop.Shutdown(), Instance, GlobalShortcuts.Start);

            // Flint quits when the person says so, not when its window closes: it may be in the tray.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => started.Dispose();
            desktop.MainWindow = started.ShowAtStart ? started.Window : null;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>Builds the window, the tray icon and the shortcuts around <paramref name="shell"/>.</summary>
    /// <param name="application">The application the tray icon belongs to.</param>
    /// <param name="shell">The shell.</param>
    /// <param name="args">The arguments Flint was started with.</param>
    /// <param name="shutdown">Quits the application.</param>
    /// <param name="instance">This copy's identity, which a second launch asks to show the window.</param>
    /// <param name="claimShortcuts">Claims the shortcuts from Windows, or returns null when it will not.</param>
    internal static StartedFlint Start(
        Application application,
        MainWindowViewModel shell,
        IReadOnlyList<string>? args,
        Action shutdown,
        FlintSingleInstance? instance,
        Func<MainWindowViewModel, GlobalShortcuts?> claimShortcuts)
    {
        var window = new MainWindow { DataContext = shell };
        shell.UseWindow(new WindowControl(window, shutdown));
        var tray = new TrayHost(application, new TrayViewModel(shell), shell.SettingsService);
        var shortcuts = claimShortcuts(shell);
        instance?.WhenAskedToShow(() => Dispatcher.UIThread.Post(shell.ShowWindow));

        var minimized = (args ?? []).Contains(RunAtSignIn.MinimizedArgument, StringComparer.OrdinalIgnoreCase);
        return new StartedFlint(
            window,
            !WindowClosePolicy.StartsHidden(shell.SettingsService.Current.Tray, minimized),
            () =>
            {
                shortcuts?.Dispose();
                tray.Dispose();
                shell.Dispose();
            });
    }
}

/// <summary>Flint, built and ready for its lifetime to show and, in the end, put away.</summary>
/// <param name="Window">The window.</param>
/// <param name="ShowAtStart">Whether the window opens now, rather than waiting in the tray.</param>
/// <param name="Disposing">Puts away the shortcuts, the tray icon and the shell.</param>
internal sealed record StartedFlint(MainWindow Window, bool ShowAtStart, Action Disposing) : IDisposable
{
    /// <inheritdoc />
    public void Dispose() => Disposing();
}
