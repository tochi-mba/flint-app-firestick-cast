using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Flint.App.Views;

public partial class SettingsPage : UserControl
{
    private const double NarrowWidth = 760;

    public SettingsPage()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ArrangeSections(Bounds.Width);
    }

    private void ArrangeSections(double width)
    {
        var narrow = width < NarrowWidth;
        SectionLayout.ColumnDefinitions = narrow ? new ColumnDefinitions("*") : new ColumnDefinitions("180,*");
        SectionLayout.RowDefinitions = narrow ? new RowDefinitions("Auto,*") : new RowDefinitions("*");
        Grid.SetColumn(SectionList, 0);
        Grid.SetRow(SectionList, 0);
        Grid.SetColumn(SectionScroller, narrow ? 0 : 1);
        Grid.SetRow(SectionScroller, narrow ? 1 : 0);
        SectionList.Margin = narrow ? new Avalonia.Thickness(0, 0, 0, 12) : new Avalonia.Thickness(0, 0, 20, 0);
        SectionList.ItemsPanel = new FuncTemplate<Panel?>(() => narrow
            ? new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal }
            : new StackPanel());
    }
}
