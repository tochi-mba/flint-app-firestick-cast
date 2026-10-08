using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Flint.Core;

namespace Flint.App.Controls;

/// <summary>
/// A small map of the displays, drawn to scale in the arrangement Windows uses, to pick one from.
/// </summary>
/// <remarks>
/// Each display is a real button, so it can be reached by Tab, clicked, and read by a screen
/// reader with its size. The arrow keys move to the nearest display in that direction on the
/// map, not to the next one in a list, because the map is how the person sees them.
/// </remarks>
public sealed class DisplayArrangementPicker : Panel
{
    /// <summary>The displays to draw.</summary>
    public static readonly StyledProperty<IReadOnlyList<DisplayInfo>?> DisplaysProperty =
        AvaloniaProperty.Register<DisplayArrangementPicker, IReadOnlyList<DisplayInfo>?>(nameof(Displays));

    /// <summary>The chosen display.</summary>
    public static readonly StyledProperty<DisplayInfo?> SelectedDisplayProperty =
        AvaloniaProperty.Register<DisplayArrangementPicker, DisplayInfo?>(
            nameof(SelectedDisplay),
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>The tallest the map is drawn.</summary>
    public const double MapHeight = 150;

    /// <summary>The widest the map is drawn, so it sits beside the page's text rather than across it.</summary>
    public const double MapWidth = 560;

    /// <summary>The space left between two displays that touch.</summary>
    private const double Gap = 4;

    static DisplayArrangementPicker()
    {
        DisplaysProperty.Changed.AddClassHandler<DisplayArrangementPicker>((picker, _) => picker.Rebuild());
        SelectedDisplayProperty.Changed.AddClassHandler<DisplayArrangementPicker>((picker, _) => picker.MarkSelected());
    }

    /// <summary>The displays to draw.</summary>
    public IReadOnlyList<DisplayInfo>? Displays
    {
        get => GetValue(DisplaysProperty);
        set => SetValue(DisplaysProperty, value);
    }

    /// <summary>The chosen display.</summary>
    public DisplayInfo? SelectedDisplay
    {
        get => GetValue(SelectedDisplayProperty);
        set => SetValue(SelectedDisplayProperty, value);
    }

    /// <summary>The display nearest <paramref name="from"/> in the direction of <paramref name="key"/>.</summary>
    /// <returns>That display, or <paramref name="from"/> when there is none that way.</returns>
    public static DisplayInfo Nearest(IReadOnlyList<DisplayInfo> displays, DisplayInfo from, Key key)
    {
        ArgumentNullException.ThrowIfNull(displays);
        ArgumentNullException.ThrowIfNull(from);

        var (fromX, fromY) = Centre(from);
        DisplayInfo? best = null;
        var bestDistance = double.MaxValue;
        foreach (var display in displays)
        {
            var (x, y) = Centre(display);
            var (along, across) = key switch
            {
                Key.Left => (fromX - x, Math.Abs(y - fromY)),
                Key.Right => (x - fromX, Math.Abs(y - fromY)),
                Key.Up => (fromY - y, Math.Abs(x - fromX)),
                Key.Down => (y - fromY, Math.Abs(x - fromX)),
                _ => (0d, 0d),
            };

            // Only displays that lie more that way than to the side, so Down never lands on a
            // display that is mostly to the left. The sideways distance counts double, so the one
            // straight ahead wins over one diagonally closer.
            if (along <= 0 || across > along)
            {
                continue;
            }

            var distance = along + (2 * across);
            if (distance < bestDistance)
            {
                best = display;
                bestDistance = distance;
            }
        }

        return best ?? from;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var (scale, bounds) = Layout(availableSize.Width);
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        return new Size(bounds.Width * scale, bounds.Height * scale);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var (scale, bounds) = Layout(finalSize.Width);
        foreach (var child in Children)
        {
            // Every child is a tile this map made for one display.
            var display = (DisplayInfo)child.Tag!;
            var rect = new Rect(
                ((display.Left - bounds.X) * scale) + (Gap / 2),
                ((display.Top - bounds.Y) * scale) + (Gap / 2),
                Math.Max(1, (display.Width * scale) - Gap),
                Math.Max(1, (display.Height * scale) - Gap));
            child.Arrange(rect);
        }

        return finalSize;
    }

    /// <summary>The scale that fits every display into the width and the map's size, and their bounds.</summary>
    private (double Scale, Rect Bounds) Layout(double width)
    {
        width = Math.Min(width, MapWidth);
        if (Displays is not { Count: > 0 } displays)
        {
            return (0, default);
        }

        var left = displays.Min(display => display.Left);
        var top = displays.Min(display => display.Top);
        var right = displays.Max(display => display.Left + display.Width);
        var bottom = displays.Max(display => display.Top + display.Height);
        var bounds = new Rect(left, top, right - left, bottom - top);
        var scale = Math.Min(width / bounds.Width, MapHeight / bounds.Height);
        return (scale, bounds);
    }

    private static (double X, double Y) Centre(DisplayInfo display) =>
        (display.Left + (display.Width / 2d), display.Top + (display.Height / 2d));

    private void Rebuild()
    {
        Children.Clear();
        foreach (var display in Displays ?? [])
        {
            var tile = new Button
            {
                Tag = display,
                Content = new TextBlock
                {
                    Text = display.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            // The tile look lives in the app's resources, which a map not yet in a window can reach.
            tile.Theme = Application.Current!.FindResource("DisplayTile") as ControlTheme;
            AutomationProperties.SetName(tile, display.Describe());
            ToolTip.SetTip(tile, display.Describe());
            tile.Click += (_, _) => SelectedDisplay = display;
            tile.KeyDown += OnTileKeyDown;
            Children.Add(tile);
        }

        MarkSelected();
        InvalidateMeasure();
    }

    private void MarkSelected()
    {
        foreach (var child in Children)
        {
            child.Classes.Set("selected", Equals(child.Tag, SelectedDisplay));
        }
    }

    private void OnTileKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)
            || sender is not Control { Tag: DisplayInfo from }
            || Displays is not { } displays)
        {
            return;
        }

        var next = Nearest(displays, from, e.Key);
        SelectedDisplay = next;
        Children.First(child => Equals(child.Tag, next)).Focus(NavigationMethod.Directional);
        e.Handled = true;
    }
}
