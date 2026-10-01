using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Flint.App.Controls;

namespace Flint.App.Tests.Snapshots;

/// <summary>
/// The input controls Settings is built from, in every state a person can see them in.
/// </summary>
/// <remarks>
/// Focus is photographed one control at a time, because only one control can hold it. Each focus
/// image is the reason the ring exists: a keyboard user must be able to see where they are.
/// </remarks>
public sealed class SettingsControlSnapshotTests
{
    private static readonly string[] Sizes = ["100%"];
    private static readonly string[] Destinations = ["TV only", "TV and this PC"];
    private static readonly string[] Repeats = ["Off", "One", "All"];

    [AvaloniaFact]
    public void Inputs_InEveryState()
    {
        var sheet = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 18,
            Children =
            {
                Row(
                    Toggle(on: false),
                    Toggle(on: true),
                    Toggle(on: false, enabled: false),
                    Toggle(on: true, enabled: false)),
                Row(
                    new Slider { Theme = Theme("FlintSlider"), Value = 40, Width = 220 },
                    new Slider { Theme = Theme("FlintSlider"), Value = 70, Width = 220, IsEnabled = false }),
                Row(
                    new ComboBox { Theme = Theme("FlintComboBox"), ItemsSource = Sizes, SelectedIndex = 0 },
                    new ComboBox { Theme = Theme("FlintComboBox"), ItemsSource = Sizes, SelectedIndex = 0, IsEnabled = false }),
                Row(
                    new TextBox { Theme = Theme("FlintTextBox"), Width = 220, PlaceholderText = "Search settings" },
                    new TextBox { Theme = Theme("FlintTextBox"), Width = 220, Text = "folder" }),
                Row(
                    new SegmentedChoice { ItemsSource = Destinations, SelectedIndex = 0 },
                    new SegmentedChoice { ItemsSource = Repeats, SelectedIndex = 2, IsEnabled = false }),
                new SettingRow
                {
                    Title = "Keep this PC awake while sharing or playing",
                    Description = "Stops Windows sleeping or turning the screen off while your screen or a file is on the TV.",
                    Content = Toggle(on: true),
                },
            },
        };

        Snapshot.Matches("control-settings-inputs", sheet, new PixelSize(760, 560));
    }

    [AvaloniaFact]
    public void ToggleOff_Focused() => Focused("control-toggle-off-focused", Toggle(on: false));

    [AvaloniaFact]
    public void ToggleOn_Focused() => Focused("control-toggle-on-focused", Toggle(on: true));

    [AvaloniaFact]
    public void Slider_Focused() =>
        Focused("control-slider-focused", new Slider { Theme = Theme("FlintSlider"), Value = 40, Width = 220 });

    [AvaloniaFact]
    public void ComboBox_Focused() =>
        Focused("control-combobox-focused", new ComboBox { Theme = Theme("FlintComboBox"), ItemsSource = Sizes, SelectedIndex = 0 });

    [AvaloniaFact]
    public void TextBox_Focused() =>
        Focused("control-textbox-focused", new TextBox { Theme = Theme("FlintTextBox"), Width = 220, PlaceholderText = "Search settings" });

    [AvaloniaFact]
    public void SegmentedChoice_Focused()
    {
        var choice = new SegmentedChoice { ItemsSource = Destinations, SelectedIndex = 1 };

        Snapshot.Matches(
            "control-segmented-focused",
            Padded(choice),
            window => choice.ContainerFromIndex(1)!.Focus(NavigationMethod.Tab),
            new PixelSize(360, 90));
    }

    [AvaloniaFact]
    public void ContentButton_Focused()
    {
        var button = new Button
        {
            Theme = Theme("ContentOutlineButton"),
            Width = 300,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Theme = Theme("LabelSmall"), Text = "Privacy and data" },
                    new TextBlock { Theme = Theme("TitleMedium"), Text = "Open the data folder" },
                },
            },
        };

        Focused("control-content-button-focused", button);
    }

    private static void Focused(string name, Control control) =>
        Snapshot.Matches(name, Padded(control), _ => control.Focus(NavigationMethod.Tab), new PixelSize(360, 90));

    private static Control Padded(Control control) => new Border
    {
        Padding = new Thickness(24),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Child = control,
    };

    private static ToggleSwitch Toggle(bool on, bool enabled = true) => new()
    {
        Theme = Theme("FlintToggleSwitch"),
        IsChecked = on,
        IsEnabled = enabled,
    };

    private static StackPanel Row(params Control[] controls)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        foreach (var control in controls)
        {
            row.Children.Add(control);
        }

        return row;
    }

    private static ControlTheme Theme(string key) => (ControlTheme)Application.Current!.FindResource(key)!;
}
