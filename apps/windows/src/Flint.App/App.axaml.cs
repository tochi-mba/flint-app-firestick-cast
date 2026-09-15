using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.App.Views;

namespace Flint.App;

/// <summary>
/// The Flint application, a REX Technologies product.
/// </summary>
public sealed class FlintApplication : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        DevFileLog.StartUiWatchdog();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = MainWindowViewModel.CreateDefault(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
