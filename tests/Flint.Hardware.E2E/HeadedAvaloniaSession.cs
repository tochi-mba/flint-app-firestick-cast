using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Flint.App;

namespace Flint.Hardware.E2E;

/// <summary>
/// Boots a real Win32 Avalonia window so a human can watch the hardware E2E drive the shell.
/// </summary>
public static class HeadedAvaloniaSession
{
    /// <summary>
    /// Runs <paramref name="body"/> on the UI thread inside a classic desktop lifetime, then shuts
    /// down. Blocks the calling thread until the loop exits.
    /// </summary>
    public static void Run(Func<Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var lifetime = new ClassicDesktopStyleApplicationLifetime
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };

                AppBuilder.Configure<FlintApplication>()
                    .UsePlatformDetect()
                    .WithInterFont()
                    .LogToTrace()
                    .SetupWithLifetime(lifetime);

                lifetime.Startup += (_, _) =>
                {
                    _ = Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        try
                        {
                            await body().ConfigureAwait(true);
                        }
                        catch (Exception exception)
                        {
                            failure = exception;
                        }
                        finally
                        {
                            lifetime.Shutdown();
                        }
                    });
                };

                lifetime.Start(Array.Empty<string>());
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = false;
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
