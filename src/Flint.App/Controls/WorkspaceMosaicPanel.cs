using Avalonia;
using Avalonia.Controls;

namespace Flint.App.Controls;

/// <summary>Places desktop pane controls at the TV's authoritative divider fractions.</summary>
public sealed class WorkspaceMosaicPanel : Panel
{
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    public int ColumnSplit { get; set; } = 5000;
    public int RowSplit { get; set; } = 5000;

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 0,
            double.IsFinite(availableSize.Height) ? availableSize.Height : 0);
    }

    protected override Size ArrangeOverride(Size size)
    {
        for (var i = 0; i < Children.Count; i++)
            Children[i].Arrange(Frame(i, size, Columns, Rows, ColumnSplit, RowSplit));
        return size;
    }

    internal static Rect Frame(int index, Size size, int columns, int rows, int column, int row)
    {
        var x = size.Width * Math.Clamp(column, 1500, 8500) / 10000d;
        var y = size.Height * Math.Clamp(row, 1500, 8500) / 10000d;
        var right = columns > 1 && index % columns != 0;
        var bottom = rows > 1 && index / Math.Max(1, columns) != 0;
        return new Rect(right ? x : 0, bottom ? y : 0,
            columns > 1 ? right ? size.Width - x : x : size.Width,
            rows > 1 ? bottom ? size.Height - y : y : size.Height);
    }
}
