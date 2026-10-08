using Avalonia;
using Avalonia.Controls;
using Flint.App.ViewModels;

namespace Flint.App.Views;

/// <summary>The Screen page, which reads the displays again whenever it opens or Windows changes them.</summary>
public partial class ScreenPage : UserControl
{
    private Screens? screens;

    public ScreenPage()
    {
        InitializeComponent();
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

    /// <summary>Windows reported a display change: one plugged in, unplugged or moved.</summary>
    internal void OnScreensChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh() => (DataContext as ScreenPageViewModel)?.RefreshDisplays();
}
