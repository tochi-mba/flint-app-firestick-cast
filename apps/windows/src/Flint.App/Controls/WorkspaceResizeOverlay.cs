using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Flint.App.ViewModels;

namespace Flint.App.Controls;

/// <summary>Pointer and keyboard divider handles; empty canvas space passes through to panes.</summary>
public sealed class WorkspaceResizeOverlay : Canvas
{
    private readonly Border columnHandle;
    private readonly Border rowHandle;
    private BrowserWorkspaceViewModel? workspace;
    private IPointer? pointer;
    private bool draggingColumn;
    private BrowserWorkspaceLayout dragLayout;

    public WorkspaceResizeOverlay()
    {
        columnHandle = Handle(true);
        rowHandle = Handle(false);
        Children.Add(columnHandle);
        Children.Add(rowHandle);
        DataContextChanged += (_, _) => Bind(DataContext as BrowserWorkspaceViewModel);
        DetachedFromVisualTree += (_, _) => { Cancel(); Bind(null); };
        AttachedToVisualTree += (_, _) => Bind(DataContext as BrowserWorkspaceViewModel);
    }

    private Border Handle(bool column)
    {
        var handle = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#B3E9BB57")),
            CornerRadius = new CornerRadius(4),
            Focusable = true,
            Cursor = new Cursor(column ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth),
        };
        Avalonia.Automation.AutomationProperties.SetName(handle, column ? "Resize columns" : "Resize rows");
        ToolTip.SetTip(handle, "Drag to resize. Arrow keys adjust; Home makes panes equal.");
        handle.PointerPressed += (_, e) =>
        {
            if (workspace?.CanResize != true || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            draggingColumn = column; dragLayout = workspace.Layout;
            pointer = e.Pointer; pointer.Capture(handle); handle.Focus(); e.Handled = true;
        };
        handle.PointerMoved += async (_, e) =>
        {
            if (pointer != e.Pointer || workspace?.CanResize != true) return;
            var point = e.GetPosition(this);
            var extent = draggingColumn ? Bounds.Width : Bounds.Height;
            if (extent <= 0) return;
            var fraction = (int)Math.Round(Math.Clamp((draggingColumn ? point.X : point.Y) / extent, .15, .85) * 10000);
            await workspace.ResizeAsync(draggingColumn ? fraction : workspace.ColumnSplit,
                draggingColumn ? workspace.RowSplit : fraction);
            e.Handled = true;
        };
        handle.PointerReleased += (_, e) => { if (pointer == e.Pointer) { Cancel(); e.Handled = true; } };
        handle.PointerCaptureLost += (_, _) => pointer = null;
        handle.KeyDown += async (_, e) =>
        {
            if (workspace?.CanResize != true) return;
            var delta = e.Key is Key.Left or Key.Up ? -500 : e.Key is Key.Right or Key.Down ? 500 : 0;
            if (e.Key == Key.Escape) { Cancel(); e.Handled = true; return; }
            if (delta == 0 && e.Key != Key.Home) return;
            e.Handled = true;
            await workspace.ResizeAsync(e.Key == Key.Home ? 5000 : workspace.ColumnSplit + (column ? delta : 0),
                e.Key == Key.Home ? 5000 : workspace.RowSplit + (column ? 0 : delta));
        };
        handle.GotFocus += (_, _) => handle.BorderThickness = new Thickness(2);
        handle.LostFocus += (_, _) => handle.BorderThickness = new Thickness(0);
        handle.BorderBrush = Brushes.White;
        return handle;
    }

    private void Bind(BrowserWorkspaceViewModel? next)
    {
        if (ReferenceEquals(workspace, next)) return;
        Cancel();
        if (workspace is not null) workspace.PropertyChanged -= Changed;
        workspace = next;
        if (workspace is not null) workspace.PropertyChanged += Changed;
        InvalidateArrange();
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (workspace?.CanResize != true || pointer is not null && workspace.Layout != dragLayout) Cancel();
        InvalidateArrange();
    }

    private void Cancel() { var current = pointer; pointer = null; current?.Capture(null); }

    protected override Size ArrangeOverride(Size finalSize)
    {
        columnHandle.IsVisible = workspace?.CanResize == true && workspace.MosaicColumnCount > 1;
        rowHandle.IsVisible = workspace?.CanResize == true && workspace.MosaicRowCount > 1;
        columnHandle.Arrange(new Rect(Math.Max(0, finalSize.Width * (workspace?.ColumnSplit ?? 5000) / 10000d - 6), 0, 12, finalSize.Height));
        rowHandle.Arrange(new Rect(0, Math.Max(0, finalSize.Height * (workspace?.RowSplit ?? 5000) / 10000d - 6), finalSize.Width, 12));
        return finalSize;
    }
}
