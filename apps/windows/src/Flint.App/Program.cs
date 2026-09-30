using Avalonia;
using Flint.App.Services;
using Flint.Platform.Windows;
using Velopack;

namespace Flint.App;

/// <summary>Entry point for the Flint desktop shell.</summary>
internal static class Program
{
    /// <summary>
    /// Starts Avalonia.
    /// </summary>
    /// <remarks>
    /// Kept free of application logic beyond opening the developer file log so Avalonia
    /// <c>LogToTrace</c> and shell diagnostics land where agents can read them.
    /// </remarks>
    [STAThread]
    public static void Main(string[] args)
    {
        // Before anything else, including the log. The installer runs this same executable to carry
        // out its hooks and expects it to do that one job and exit; anything started first would run
        // during an install, an update and an uninstall as well.
        // Wiring only: what each hook does is InstallLifecycle's, where it is tested.
        var lifecycle = InstallLifecycle.ForThisInstall();
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => lifecycle.AfterInstall())
            .OnAfterUpdateFastCallback(_ => lifecycle.AfterUpdate())
            .OnBeforeUninstallFastCallback(_ => lifecycle.BeforeUninstall())
            .Run();

        using var instance = FlintSingleInstance.TryAcquire();
        if (instance is null)
        {
            return;
        }

        DevFileLog.Start();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Builds the Avalonia application. Also used by the headless test host, which is why it is
    /// public and separate from <see cref="Main"/>.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<FlintApplication>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
