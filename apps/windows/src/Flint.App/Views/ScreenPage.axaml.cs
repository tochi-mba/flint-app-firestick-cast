using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Flint.App.ViewModels;

namespace Flint.App.Views;

/// <summary>The Screen page, which reads the displays again whenever it opens or Windows changes them.</summary>
public partial class ScreenPage : UserControl
{
    private Screens? screens;
    private ScreenPageViewModel? watched;

    public ScreenPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch(DataContext as ScreenPageViewModel);
        PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty && IsVisible)
            {
                Refresh();
            }
        };
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        screens = TopLevel.GetTopLevel(this)?.Screens;
        if (screens is not null)
        {
            screens.Changed += OnScreensChanged;
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (screens is not null)
        {
            screens.Changed -= OnScreensChanged;
            screens = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Follows the page's view model, to move focus to RESUME when the share pauses.</summary>
    private void Watch(ScreenPageViewModel? screen)
    {
        if (watched is not null)
        {
            watched.PropertyChanged -= OnScreenChanged;
        }

        watched = screen;
        if (watched is not null)
        {
            watched.PropertyChanged += OnScreenChanged;
        }
    }

    /// <summary>Once paused, RESUME has focus, so the next press of Enter or Space resumes.</summary>
    private void OnScreenChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (change.PropertyName is nameof(ScreenPageViewModel.IsPaused) && ((ScreenPageViewModel)sender!).IsPaused)
        {
            Dispatcher.UIThread.Post(() => ResumeButton.Focus(), DispatcherPriority.Loaded);
        }
    }

    /// <summary>Windows reported a display change: one plugged in, unplugged or moved.</summary>
    internal void OnScreensChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh() => (DataContext as ScreenPageViewModel)?.RefreshDisplays();
}
