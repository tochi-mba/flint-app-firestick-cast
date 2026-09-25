using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Protocol;

namespace Flint.App.Views;

/// <summary>The desktop's control surface for the browser running on the television.</summary>
/// <remarks>
/// The live preview stage is the primary pointer/scroll surface. Letterboxed gutters are inert.
/// The slim remote strip keeps D-pad and text discoverable for assistive technology.
/// </remarks>
public partial class BrowserPage : UserControl
{
    private const double CursorRadius = 18;
    /// <summary>Pixels applied per Avalonia wheel notch so one click is visible on a 1080p TV.</summary>
    private const int PixelsPerWheelNotch = 120;
    private bool previewPressed;
    private BrowserPageViewModel? subscribedViewModel;

    /// <summary>Builds the page.</summary>
    public BrowserPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // Tunnel wheel first so the outer page ScrollViewer cannot eat trackpad/mouse scroll.
        PreviewSurface.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnPreviewPointerWheelChanged,
            RoutingStrategies.Tunnel);
        PreviewSurface.AddHandler(
            Gestures.ScrollGestureEvent,
            OnPreviewScrollGesture,
            RoutingStrategies.Bubble);
    }

    /// <summary>The page's view model, or <see langword="null"/> before one is attached.</summary>
    private BrowserPageViewModel? ViewModel => DataContext as BrowserPageViewModel;

    private void OnHelpAdvanceOrDismiss(object? sender, RoutedEventArgs args)
    {
        var model = ViewModel;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(model, ViewModel) && model is { ShowNavigation: true } &&
                !model.Help.IsExpanded && !model.Dialog.IsVisible && HelpButton.IsEffectivelyVisible)
                HelpButton.Focus();
        });
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            subscribedViewModel.Workspace.PropertyChanged -= OnWorkspaceLayoutChanged;
            subscribedViewModel = null;
        }

        if (DataContext is BrowserPageViewModel viewModel)
        {
            subscribedViewModel = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.Workspace.PropertyChanged += OnWorkspaceLayoutChanged;
            SyncMosaicGrid();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(BrowserPageViewModel.ShowWorkspaceMosaic) or null)
        {
            SyncMosaicGrid();
        }
    }

    private void OnWorkspaceLayoutChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(BrowserWorkspaceViewModel.Layout)
            or nameof(BrowserWorkspaceViewModel.MosaicColumnCount)
            or nameof(BrowserWorkspaceViewModel.ColumnSplit)
            or nameof(BrowserWorkspaceViewModel.RowSplit)
            or nameof(BrowserWorkspaceViewModel.MosaicRowCount)
            or nameof(BrowserWorkspaceViewModel.HasOpenPanes)
            or null)
        {
            SyncMosaicGrid();
        }
    }

    /// <summary>
    /// UniformGrid inside an ItemsPanelTemplate cannot bind Columns/Rows reliably under compiled
    /// bindings, so the stage mirrors the workspace layout here.
    /// </summary>
    private void SyncMosaicGrid()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        void Apply()
        {
            if (this.FindControl<ItemsControl>("WorkspacePaneGrid") is not { } items)
            {
                return;
            }

            if (items.ItemsPanelRoot is not Flint.App.Controls.WorkspaceMosaicPanel grid)
            {
                return;
            }

            grid.Columns = Math.Max(1, viewModel.Workspace.MosaicColumnCount);
            grid.Rows = Math.Max(1, viewModel.Workspace.MosaicRowCount);
            grid.ColumnSplit = viewModel.Workspace.ColumnSplit;
            grid.RowSplit = viewModel.Workspace.RowSplit;
            grid.InvalidateArrange();
        }

        Apply();
        Avalonia.Threading.Dispatcher.UIThread.Post(Apply, Avalonia.Threading.DispatcherPriority.Loaded);
        Avalonia.Threading.Dispatcher.UIThread.Post(Apply, Avalonia.Threading.DispatcherPriority.Render);
    }

    /// <summary>Enter in the address bar opens the resolved site or Google search on the TV.</summary>
    private void OnAddressKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key is not Key.Enter and not Key.Return || ViewModel is not { } viewModel)
        {
            return;
        }

        if (!viewModel.NavigateCommand.CanExecute(null))
        {
            return;
        }

        args.Handled = true;
        viewModel.NavigateCommand.Execute(null);
    }

    private void OnPreviewPointerEntered(object? sender, PointerEventArgs args)
    {
        if (sender is not Control surface || ViewModel is not { CanInteractWithPreview: true })
        {
            return;
        }

        MovePreviewCursor(surface, args.GetPosition(surface), visible: true);
    }

    private void OnPreviewPointerExited(object? sender, PointerEventArgs args)
    {
        if (previewPressed)
        {
            return;
        }

        PreviewCursor.IsVisible = false;
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not Control surface || ViewModel is not { } viewModel)
        {
            return;
        }

        var point = args.GetCurrentPoint(surface);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Mosaic chrome buttons sit above the preview; only map hits that land on the image.
        if (!TryMapPreviewPoint(surface, point.Position, viewModel, out var mapped))
        {
            return;
        }

        previewPressed = true;
        surface.Focus();
        args.Pointer.Capture(surface);
        args.Handled = true;
        MovePreviewCursor(surface, point.Position, visible: true);
        _ = viewModel.SendPointerAsync(
            BrowserPointerAction.Down,
            mapped.X,
            mapped.Y,
            buttons: 1,
            mapped.NavigationId,
            mapped.FrameId);
    }

    private void OnPreviewPointerMoved(object? sender, PointerEventArgs args)
    {
        if (sender is not Control surface || ViewModel is not { } viewModel)
        {
            return;
        }

        var point = args.GetCurrentPoint(surface);
        MovePreviewCursor(surface, point.Position, visible: viewModel.CanInteractWithPreview);

        if (!TryMapPreviewPoint(surface, point.Position, viewModel, out var mapped))
        {
            return;
        }

        args.Handled = previewPressed;
        var buttons = previewPressed ? 1 : 0;
        _ = viewModel.SendPointerAsync(
            BrowserPointerAction.Move,
            mapped.X,
            mapped.Y,
            buttons,
            mapped.NavigationId,
            mapped.FrameId);
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (!previewPressed || sender is not Control surface || ViewModel is not { } viewModel)
        {
            return;
        }

        previewPressed = false;
        args.Pointer.Capture(null);
        args.Handled = true;
        var position = args.GetPosition(surface);
        MovePreviewCursor(surface, position, visible: true);
        if (!TryMapPreviewPoint(surface, position, viewModel, out var mapped))
        {
            _ = viewModel.SendPointerAsync(BrowserPointerAction.Cancel, 0, 0, buttons: 0);
            return;
        }

        _ = viewModel.SendPointerAsync(
            BrowserPointerAction.Up,
            mapped.X,
            mapped.Y,
            buttons: 0,
            mapped.NavigationId,
            mapped.FrameId);
    }

    private void OnPreviewPointerCaptureLost(object? sender, PointerCaptureLostEventArgs args)
    {
        if (!previewPressed || ViewModel is not { } viewModel)
        {
            previewPressed = false;
            return;
        }

        previewPressed = false;
        PreviewCursor.IsVisible = false;
        _ = viewModel.SendPointerAsync(BrowserPointerAction.Cancel, 0, 0, buttons: 0);
    }

    private void OnPreviewPointerWheelChanged(object? sender, PointerWheelEventArgs args)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var surface = PreviewSurface;
        var deltaX = (int)Math.Round(args.Delta.X * PixelsPerWheelNotch);
        var deltaY = (int)Math.Round(args.Delta.Y * PixelsPerWheelNotch);
        if (deltaX == 0 && deltaY == 0)
        {
            return;
        }

        var position = args.GetPosition(surface);
        if (!TryMapPreviewPoint(surface, position, viewModel, out var mapped))
        {
            return;
        }

        args.Handled = true;
        MovePreviewCursor(surface, position, visible: true);
        _ = viewModel.SendScrollAsync(
            mapped.X,
            mapped.Y,
            deltaX,
            deltaY,
            mapped.NavigationId,
            mapped.FrameId);
    }

    private void OnPreviewScrollGesture(object? sender, ScrollGestureEventArgs args)
    {
        if (ViewModel is not { } viewModel || previewPressed)
        {
            return;
        }

        var deltaX = (int)Math.Round(args.Delta.X);
        var deltaY = (int)Math.Round(args.Delta.Y);
        if (deltaX == 0 && deltaY == 0)
        {
            return;
        }

        var surface = PreviewSurface;
        var position = new Point(surface.Bounds.Width / 2, surface.Bounds.Height / 2);
        if (!TryMapPreviewPoint(surface, position, viewModel, out var mapped))
        {
            return;
        }

        args.Handled = true;
        MovePreviewCursor(surface, position, visible: true);
        _ = viewModel.SendScrollAsync(
            mapped.X,
            mapped.Y,
            -deltaX,
            -deltaY,
            mapped.NavigationId,
            mapped.FrameId);
    }

    private static bool TryMapPreviewPoint(
        Control surface,
        Point position,
        BrowserPageViewModel viewModel,
        out BrowserPreviewInteraction.MappedPoint mapped) =>
        BrowserPreviewInteraction.TryMapPointer(
            position.X,
            position.Y,
            surface.Bounds.Width,
            surface.Bounds.Height,
            viewModel.Preview.FrameWidth,
            viewModel.Preview.FrameHeight,
            viewModel.CanInteractWithPreview,
            viewModel.Preview.IsLive && viewModel.Preview.HasFrame,
            viewModel.Preview.NavigationId,
            viewModel.Preview.FrameId,
            viewModel.ActiveNavigationIdForInteraction,
            out mapped);

    private void MovePreviewCursor(Control surface, Point position, bool visible)
    {
        PreviewCursor.IsVisible = visible && ViewModel?.CanInteractWithPreview == true;
        var x = Math.Clamp(position.X, 0, Math.Max(0, surface.Bounds.Width));
        var y = Math.Clamp(position.Y, 0, Math.Max(0, surface.Bounds.Height));
        Canvas.SetLeft(PreviewCursor, x - CursorRadius);
        Canvas.SetTop(PreviewCursor, y - CursorRadius);
    }

    /// <summary>
    /// Turns a key press on the remote pad into the semantic key the protocol carries.
    /// </summary>
    private void OnRemoteKeyDown(object? sender, KeyEventArgs args)
    {
        var key = args.Key switch
        {
            Key.Up => BrowserSemanticKey.Up,
            Key.Down => BrowserSemanticKey.Down,
            Key.Left => BrowserSemanticKey.Left,
            Key.Right => BrowserSemanticKey.Right,
            Key.Enter or Key.Space => BrowserSemanticKey.Select,
            Key.Back => BrowserSemanticKey.Back,
            Key.Tab => args.KeyModifiers.HasFlag(KeyModifiers.Shift)
                ? BrowserSemanticKey.ShiftTab
                : BrowserSemanticKey.Tab,
            Key.Escape => BrowserSemanticKey.Escape,
            Key.PageUp => BrowserSemanticKey.PageUp,
            Key.PageDown => BrowserSemanticKey.PageDown,
            Key.Home => BrowserSemanticKey.Home,
            Key.End => BrowserSemanticKey.End,
            Key.F5 => BrowserSemanticKey.Refresh,
            _ => (BrowserSemanticKey?)null,
        };

        if (key is not { } semantic || ViewModel is not { } viewModel)
        {
            return;
        }

        args.Handled = true;
        viewModel.SendKeyCommand.Execute(semantic);
    }

    /// <summary>Sends typed characters straight to the page on the television.</summary>
    private void OnRemoteTextInput(object? sender, TextInputEventArgs args)
    {
        if (args.Text is not { Length: > 0 } text || ViewModel is not { } viewModel)
        {
            return;
        }

        args.Handled = true;
        viewModel.SendTextCommand.Execute(text);
    }

    /// <summary>Sends the key a pressed remote button carries.</summary>
    private void OnRemoteKeyPressed(object? sender, Controls.RemoteKeyEventArgs args)
    {
        ViewModel?.SendKeyCommand.Execute(args.Key);
    }

    /// <summary>Clicking the dimmed scrim closes the MORE drawer without eating clicks on the drawer.</summary>
    private void OnMoreScrimPressed(object? sender, PointerPressedEventArgs args)
    {
        if (ViewModel is not { IsMorePanelOpen: true } viewModel)
        {
            return;
        }

        viewModel.ToggleMorePanelCommand.Execute(null);
        args.Handled = true;
    }

    /// <summary>Stops scrim-close from firing when interacting inside the drawer.</summary>
    private void OnMoreDrawerPressed(object? sender, PointerPressedEventArgs args) =>
        args.Handled = true;
}
