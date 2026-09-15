using Avalonia;
using Flint.App.Services;

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
